using System;
using System.Diagnostics;

namespace ChsbKiosk.Core.Exam;

public sealed class ExamClock
{
	private readonly Func<long> _monotonicMs;

	private readonly object _sync = new object();

	private DateTimeOffset _serverAtSync;

	private long _monotonicAtSync;

	private bool _synced;

	public bool IsSynced
	{
		get
		{
			lock (_sync)
			{
				return _synced;
			}
		}
	}

	public DateTimeOffset Now
	{
		get
		{
			lock (_sync)
			{
				if (!_synced)
				{
					return DateTimeOffset.UtcNow;
				}
				return _serverAtSync.AddMilliseconds(_monotonicMs() - _monotonicAtSync);
			}
		}
	}

	public ExamClock(Func<long>? monotonicMs = null)
	{
		_monotonicMs = monotonicMs ?? ((Func<long>)(() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency));
	}

	public void Sync(DateTimeOffset serverTime)
	{
		lock (_sync)
		{
			_serverAtSync = serverTime;
			_monotonicAtSync = _monotonicMs();
			_synced = true;
		}
	}
}
