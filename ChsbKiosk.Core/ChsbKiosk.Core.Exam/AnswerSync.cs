using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Core.Exam;

public sealed class AnswerSync : IDisposable
{
	private static readonly TimeSpan[] Backoff = new TimeSpan[5]
	{
		TimeSpan.FromSeconds(2L),
		TimeSpan.FromSeconds(4L),
		TimeSpan.FromSeconds(8L),
		TimeSpan.FromSeconds(15L),
		TimeSpan.FromSeconds(30L)
	};

	private readonly ExamSession _session;

	private readonly IAnswerApi _api;

	private readonly Func<TimeSpan, CancellationToken, Task> _delay;

	private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, int.MaxValue);

	private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

	private Task? _loop;

	private volatile bool _paused;

	private int _failures;

	public SyncStatus Status { get; private set; }

	public event Action<SyncStatus>? StatusChanged;

	public event Action<SyncStopReason>? Stopped;

	public event Action? Saved;

	public AnswerSync(ExamSession session, IAnswerApi api, Func<TimeSpan, CancellationToken, Task>? delay = null)
	{
		_session = session;
		_api = api;
		_delay = delay ?? new Func<TimeSpan, CancellationToken, Task>(Task.Delay);
	}

	public void Start()
	{
		if (_loop == null)
		{
			_loop = Task.Run(() => RunAsync(_shutdown.Token));
		}
	}

	public void Kick()
	{
		if (!_shutdown.IsCancellationRequested)
		{
			_signal.Release();
		}
	}

	public void Resume()
	{
		_paused = false;
		Kick();
	}

	public void Pause()
	{
		_paused = true;
	}

	public async Task<ApiResult<Empty>> FlushAsync(CancellationToken ct)
	{
		for (int attempt = 0; attempt < 4; attempt++)
		{
			ApiResult<Empty> apiResult = await SendPendingAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (apiResult.Ok && _session.PendingCount == 0)
			{
				return apiResult;
			}
			if (!apiResult.Ok && !apiResult.IsTransient)
			{
				return apiResult;
			}
			if (!apiResult.Ok)
			{
				await _delay(TimeSpan.FromSeconds(1.5 * (double)(attempt + 1)), ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return (_session.PendingCount == 0) ? ApiResult<Empty>.Success(Empty.Value, 200) : ApiResult<Empty>.Network("Javoblarni serverga yuborib bo'lmadi. Internet aloqasini tekshiring.");
	}

	private async Task RunAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await _signal.WaitAsync(TimeSpan.FromSeconds(10L), ct).ConfigureAwait(continueOnCapturedContext: false);
				while (_signal.CurrentCount > 0)
				{
					await _signal.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (_paused || _session.PendingCount == 0)
				{
					continue;
				}
				ApiResult<Empty> apiResult = await SendPendingAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				if (apiResult.Ok)
				{
					_failures = 0;
					if (_session.PendingCount > 0)
					{
						Kick();
					}
				}
				else if (apiResult.IsTransient)
				{
					TimeSpan arg = Backoff[Math.Min(_failures, Backoff.Length - 1)];
					_failures++;
					await _delay(arg, ct).ConfigureAwait(continueOnCapturedContext: false);
					Kick();
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception)
			{
				try
				{
					await _delay(TimeSpan.FromSeconds(3L), ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
	}

	private async Task<ApiResult<Empty>> SendPendingAsync(CancellationToken ct)
	{
		await _sendGate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			IReadOnlyList<PendingAnswer> pending = _session.GetPending();
			if (pending.Count == 0)
			{
				SetStatus(SyncStatus.Synced);
				return ApiResult<Empty>.Success(Empty.Value, 200);
			}
			SetStatus(SyncStatus.Saving);
			ApiResult<Empty> apiResult;
			if (pending.Count == 1)
			{
				apiResult = await _api.SaveAnswerAsync(_session.TestResultId, ToInput(pending[0]), ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				ApiResult<BatchSaveResponse> apiResult2 = await _api.SaveAnswersBatchAsync(_session.TestResultId, pending.Select(ToInput).ToList(), ct).ConfigureAwait(continueOnCapturedContext: false);
				apiResult = (apiResult2.Ok ? ApiResult<Empty>.Success(Empty.Value, apiResult2.Status) : new ApiResult<Empty>
				{
					Ok = false,
					Status = apiResult2.Status,
					Error = apiResult2.Error,
					Code = apiResult2.Code,
					IsNetworkError = apiResult2.IsNetworkError
				});
			}
			if (apiResult.Ok)
			{
				_session.MarkSaved(pending);
				SetStatus((_session.PendingCount != 0) ? SyncStatus.Saving : SyncStatus.Synced);
				this.Saved?.Invoke();
				return apiResult;
			}
			if (apiResult.IsTransient)
			{
				SetStatus(SyncStatus.Offline);
				return apiResult;
			}
			SyncStopReason? syncStopReason = apiResult.Status switch
			{
				423 => SyncStopReason.Locked, 
				409 => SyncStopReason.TimeExpired, 
				401 => SyncStopReason.Unauthorized, 
				404 => SyncStopReason.AttemptClosed, 
				_ => null, 
			};
			if (syncStopReason.HasValue)
			{
				_paused = true;
				this.Stopped?.Invoke(syncStopReason.Value);
			}
			else
			{
				SetStatus(SyncStatus.Offline);
			}
			return apiResult;
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private static AnswerInput ToInput(PendingAnswer pending)
	{
		return new AnswerInput
		{
			QuestionId = pending.QuestionId,
			SelectedAnswer = pending.SelectedAnswer,
			TimeSpentSec = pending.TimeSpentSec
		};
	}

	private void SetStatus(SyncStatus status)
	{
		if (Status != status)
		{
			Status = status;
			this.StatusChanged?.Invoke(status);
		}
	}

	public void Dispose()
	{
		_shutdown.Cancel();
		_shutdown.Dispose();
	}
}
