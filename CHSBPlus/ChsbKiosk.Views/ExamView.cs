using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Exam;
using ChsbKiosk.Core.Proctor;
using ChsbKiosk.Core.Storage;
using ChsbKiosk.Proctor;
using ChsbKiosk.Rendering;

namespace ChsbKiosk.Views;

public partial class ExamView : UserControl, IDisposable, IExitGuard, IRefreshableView, IComponentConnector
{
	private enum Phase
	{
		Loading,
		Running,
		Warning,
		Locked,
		Submitting,
		Finished
	}

	private static readonly TimeSpan ServerGrace = TimeSpan.FromSeconds(125L);

	private static readonly TimeSpan RenewInterval = TimeSpan.FromMinutes(30L);

	private static readonly Brush PrimaryBrush = Frozen(27, 41, 75);

	private static readonly Brush BlueSoftBrush = Frozen(219, 234, 254);

	private static readonly Brush BlueInkBrush = Frozen(29, 78, 216);

	private static readonly Brush SuccessBrush = Frozen(22, 163, 74);

	private static readonly Brush SuccessSoftBrush = Frozen(220, 252, 231);

	private static readonly Brush SuccessInkBrush = Frozen(22, 101, 52);

	private static readonly Brush YellowSoftBrush = Frozen(254, 249, 195);

	private static readonly Brush YellowInkBrush = Frozen(133, 77, 14);

	private static readonly Brush WarningBrush = Frozen(217, 119, 6);

	private static readonly Brush WarningSoftBrush = Frozen(254, 243, 199);

	private static readonly Brush WarningInkBrush = Frozen(120, 53, 15);

	private static readonly Brush DangerBrush = Frozen(239, 68, 68);

	private static readonly Brush DangerSoftBrush = Frozen(254, 226, 226);

	private static readonly Brush DangerInkBrush = Frozen(185, 28, 28);

	private static readonly Brush Gray100Brush = Frozen(243, 244, 246);

	private static readonly Brush Gray600Brush = Frozen(75, 85, 99);

	private static readonly Brush TextBrush = Frozen(22, 33, 60);

	private static readonly Brush MutedBrush = Frozen(92, 111, 138);

	private static readonly Brush IdleDotBrush = Frozen(148, 163, 184);

	private readonly IShell _shell;

	private readonly AppServices _services;

	private readonly string _testId;

	private readonly string _testResultId;

	private readonly StartResponse _initial;

	private readonly List<Button> _navButtons = new List<Button>();

	private readonly List<ClientSignal> _signals = new List<ClientSignal>();

	private readonly DispatcherTimer _tickTimer;

	private readonly DispatcherTimer _heartbeatTimer;

	private readonly DispatcherTimer _countdownTimer;

	private readonly DispatcherTimer _noticeTimer;

	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

	private ExamSession _session;

	private AnswerSync? _sync;

	private QuestionRenderer? _renderer;

	private CameraProctor? _proctor;

	private CameraMode _cameraMode;

	private Phase _phase;

	private int _index;

	private bool _disposed;

	private bool _rendererReady;

	private bool _rendererFailed;

	private int _warningsUsed;

	private ProctorIncident? _incident;

	private int _countdownLeft;

	private bool _countdownFinal;

	private bool _countdownRecovered;

	private ProctorCondition _cameraCondition;

	private long _cameraNotOkSinceMs;

	private bool _proctorRestartPending;

	private WriteableBitmap? _previewBitmap;

	private int _previewBusy;

	private bool _lockIsInterruption;

	private bool _lockReported = true;

	private ProctorCondition _lockCondition;

	private bool _unlocking;

	private bool _serverUnlocked;

	private bool _expiredSubmitTried;

	private bool _refreshingLock;

	private long _lastTickMs;

	private double _timeCarrySec;

	private long _lastRenewMs;

	private bool _heartbeatBusy;

	public string? ExitWarning
	{
		get
		{
			Phase phase = _phase;
			if ((uint)(phase - 1) > 3u)
			{
				return null;
			}
			return "Test davom etmoqda. Ilovadan chiqsangiz ham test vaqti to'xtamaydi.";
		}
	}

	private TimeSpan Remaining => _session.EndsAt - _services.Clock.Now;

	public ExamView(IShell shell, StartResponse start, CameraMode cameraMode)
	{
		_shell = shell;
		_services = shell.Services;
		_initial = start;
		_cameraMode = cameraMode;
		_testId = start.Test.Id;
		_testResultId = start.TestResult.Id;
		InitializeComponent();
		AttemptLocalState attempt = _services.Store.GetAttempt(_testResultId);
		_session = ExamSession.FromStart(start, ToPending(attempt.Pending));
		_warningsUsed = Math.Max(0, attempt.WarningsUsed);
		_index = Math.Clamp(attempt.LastQuestionIndex, 0, Math.Max(0, _session.Questions.Count - 1));
		_services.Clock.Sync(start.ServerTime);
		_tickTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(500L)
		};
		_tickTimer.Tick += delegate
		{
			OnTick();
		};
		_heartbeatTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(_services.HeartbeatSec)
		};
		_heartbeatTimer.Tick += delegate
		{
			HeartbeatAsync();
		};
		_countdownTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_countdownTimer.Tick += delegate
		{
			OnCountdownTick();
		};
		_noticeTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(7L)
		};
		_noticeTimer.Tick += delegate
		{
			_noticeTimer.Stop();
			NoticeBar.Visibility = Visibility.Collapsed;
		};
		TitleText.Text = _session.Title;
		StudentText.Text = _services.Store.StudentName ?? "";
		StudentLoginText.Text = (string.IsNullOrWhiteSpace(_services.StudentLogin) ? "" : ("Login: " + _services.StudentLogin));
		StudentLoginText.Visibility = ((StudentLoginText.Text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		BuildNavigator();
		UpdateTimerText();
	}

	public void Refresh()
	{
		if (_phase == Phase.Running && _rendererReady)
		{
			ShowQuestion(_index);
		}
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		base.Loaded -= OnLoaded;
		try
		{
			await InitializeAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Test ekranini ishga tushirishda xato");
			RendererStatusText.Text = "Test ekranini ishga tushirib bo'lmadi. Nazoratchini chaqiring.";
		}
	}

	private async Task InitializeAsync()
	{
		_shell.FocusLost += OnShellFocusLost;
		try
		{
			_renderer = new QuestionRenderer(QuestionView, _services.Config, _services.Api);
			_renderer.OptionSelected += OnOptionSelected;
			await _renderer.InitializeAsync();
			_rendererReady = true;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Savol chizuvchi ishga tushmadi");
			_rendererFailed = true;
			UpdateOverlayState();
			RendererStatusText.Text = "Savollarni ko'rsatish moduli ishga tushmadi (WebView2 Runtime o'rnatilmagan bo'lishi mumkin). Nazoratchini chaqiring — test vaqti saqlanib qoladi.";
			QueueSignal("KIOSK_RENDERER_FAILED", ex.GetType().Name);
		}
		if (!_disposed)
		{
			AttachSync();
			_lastTickMs = _stopwatch.ElapsedMilliseconds;
			_lastRenewMs = _stopwatch.ElapsedMilliseconds;
			_tickTimer.Start();
			_heartbeatTimer.Start();
			QueueSignal("KIOSK_START", $"camera={_cameraMode}; monitors={MonitorCount()}");
			QueueSignal("PROCTOR_CONSENT");
			string pendingViolationType = _services.Store.GetAttempt(_testResultId).PendingViolationType;
			if (_initial.TestResult.CheatLocked)
			{
				CheatStatus cheatStatus = _initial.CheatStatus;
				EnterLock(cheatStatus?.Reason ?? _initial.TestResult.CheatReason ?? "Test bloklangan", cheatStatus?.IsInterruption ?? false, ProctorCondition.Ok, reported: true);
				UpdateLockCounters(_initial.TestResult.ViolationCount, _initial.TestResult.UnlockCount, _initial.TestResult.MaxUnlocks);
			}
			else if (!string.IsNullOrEmpty(pendingViolationType))
			{
				BlockAsync(ParseCondition(pendingViolationType), null, new SnapshotDetails());
			}
			else
			{
				_phase = Phase.Running;
				ShowQuestion(_index);
			}
			SetupCamera();
			UpdateOverlayState();
			FlushSignalsAsync();
			LoadProctorStateAsync();
		}
	}

	private async Task LoadProctorStateAsync()
	{
		try
		{
			ApiResult<ProctorStateResponse> apiResult = await _services.Api.GetProctorStateAsync(_testResultId);
			if (!_disposed && apiResult.Ok && apiResult.Value != null && apiResult.Value.WarningCount > _warningsUsed)
			{
				_warningsUsed = apiResult.Value.WarningCount;
				UpdateWarningsText();
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Nazorat holatini olishda xato");
		}
	}

	private void AttachSync()
	{
		_sync?.Dispose();
		AnswerSync sync = new AnswerSync(_session, new ApiClientAnswerApi(_services.Api));
		sync.StatusChanged += delegate(SyncStatus status)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnSyncStatus(sync, status);
			});
		};
		sync.Saved += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnSyncSaved(sync);
			});
		};
		sync.Stopped += delegate(SyncStopReason reason)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnSyncStopped(sync, reason);
			});
		};
		_sync = sync;
		sync.Start();
		if (_session.PendingCount > 0)
		{
			sync.Kick();
		}
		OnSyncStatus(sync, (_session.PendingCount > 0) ? SyncStatus.Saving : SyncStatus.Synced);
	}

	private void BuildNavigator()
	{
		NavigatorGrid.Children.Clear();
		_navButtons.Clear();
		NavigatorGrid.Columns = ((_session.Questions.Count > 45) ? 6 : 5);
		for (int i = 0; i < _session.Questions.Count; i++)
		{
			int index = i;
			Button button = new Button
			{
				Content = (i + 1).ToString(),
				Style = (Style)FindResource("Btn.Nav"),
				Focusable = false
			};
			button.Click += delegate
			{
				GoTo(index);
			};
			_navButtons.Add(button);
			NavigatorGrid.Children.Add(button);
		}
		UpdateNavigator();
	}

	private void UpdateNavigator()
	{
		for (int i = 0; i < _navButtons.Count; i++)
		{
			Button button = _navButtons[i];
			string id = _session.Questions[i].Id;
			bool flag = _session.GetAnswer(id) != null;
			if (i == _index)
			{
				button.Background = PrimaryBrush;
				button.Foreground = Brushes.White;
			}
			else if (flag && _session.IsDirty(id))
			{
				button.Background = YellowSoftBrush;
				button.Foreground = YellowInkBrush;
			}
			else if (flag)
			{
				button.Background = SuccessSoftBrush;
				button.Foreground = SuccessInkBrush;
			}
			else
			{
				button.Background = Gray100Brush;
				button.Foreground = Gray600Brush;
			}
		}
		int count = _session.Questions.Count;
		int num = Math.Clamp(_session.AnsweredCount, 0, count);
		AnsweredText.Text = $"Javob berilgan: {num} ta  ·  jami {count} ta";
		ProgressPercentText.Text = ((count > 0) ? $"{Math.Round((double)num * 100.0 / (double)count):0}%" : "0%");
		ProgressDone.Width = new GridLength(num, GridUnitType.Star);
		ProgressLeft.Width = new GridLength(Math.Max(0, count - num), GridUnitType.Star);
	}

	private void ShowQuestion(int index)
	{
		if (_session.Questions.Count != 0)
		{
			_index = Math.Clamp(index, 0, _session.Questions.Count - 1);
			ExamQuestion examQuestion = _session.Questions[_index];
			QuestionNumberText.Text = $"Savol {_index + 1}";
			PositionText.Text = $"{_index + 1} / {_session.Questions.Count}";
			if (string.IsNullOrWhiteSpace(examQuestion.SubjectName))
			{
				SubjectBadge.Visibility = Visibility.Collapsed;
			}
			else
			{
				SubjectText.Text = examQuestion.SubjectName;
				SubjectBadge.Visibility = Visibility.Visible;
			}
			PrevButton.IsEnabled = _index > 0;
			NextButton.IsEnabled = _index < _session.Questions.Count - 1;
			_renderer?.Show(examQuestion, _session.GetAnswer(examQuestion.Id), _phase == Phase.Running);
			UpdateSavedText();
			UpdateNavigator();
			if (_index < _navButtons.Count)
			{
				_navButtons[_index].BringIntoView();
			}
		}
	}

	private void GoTo(int index)
	{
		if (_phase == Phase.Running && index != _index)
		{
			ShowQuestion(index);
			PersistLocal();
		}
	}

	private void OnPrevClick(object sender, RoutedEventArgs e)
	{
		GoTo(_index - 1);
	}

	private void OnNextClick(object sender, RoutedEventArgs e)
	{
		GoTo(_index + 1);
	}

	private void OnOptionSelected(string key)
	{
		if (_phase == Phase.Running && !_disposed)
		{
			ExamQuestion examQuestion = _session.Questions[_index];
			if (_session.Select(examQuestion.Id, key))
			{
				PersistLocal();
				_sync?.Kick();
				UpdateSavedText();
				UpdateNavigator();
			}
		}
	}

	private void UpdateSavedText()
	{
		if (_session.Questions.Count != 0)
		{
			ExamQuestion examQuestion = _session.Questions[_index];
			if (_session.GetAnswer(examQuestion.Id) == null)
			{
				SavedText.Text = "Javob belgilanmagan";
				SavedText.Foreground = Gray600Brush;
				SavedBadge.Background = Gray100Brush;
			}
			else if (_session.IsDirty(examQuestion.Id))
			{
				SavedText.Text = "Saqlanmoqda...";
				SavedText.Foreground = YellowInkBrush;
				SavedBadge.Background = YellowSoftBrush;
			}
			else
			{
				SavedText.Text = "Saqlandi";
				SavedText.Foreground = SuccessInkBrush;
				SavedBadge.Background = SuccessSoftBrush;
			}
		}
	}

	private void PersistLocal()
	{
		IReadOnlyList<PendingAnswer> pending = _session.GetPending();
		int index = _index;
		_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
		{
			attempt.Pending = pending.Select((PendingAnswer p) => new LocalAnswer
			{
				QuestionId = p.QuestionId,
				SelectedAnswer = p.SelectedAnswer,
				TimeSpentSec = p.TimeSpentSec
			}).ToList();
			attempt.LastQuestionIndex = index;
		});
	}

	private static IEnumerable<PendingAnswer> ToPending(IEnumerable<LocalAnswer> answers)
	{
		return answers.Select((LocalAnswer a) => new PendingAnswer
		{
			QuestionId = a.QuestionId,
			SelectedAnswer = a.SelectedAnswer,
			TimeSpentSec = a.TimeSpentSec
		});
	}

	private void OnSyncStatus(AnswerSync source, SyncStatus status)
	{
		if (!_disposed && source == _sync)
		{
			switch (status)
			{
			case SyncStatus.Synced:
				SyncBadge.Background = SuccessSoftBrush;
				SyncText.Foreground = SuccessInkBrush;
				SyncText.Text = "Javoblar saqlangan";
				_shell.SetConnection(online: true);
				break;
			case SyncStatus.Saving:
				SyncBadge.Background = YellowSoftBrush;
				SyncText.Foreground = YellowInkBrush;
				SyncText.Text = "Saqlanmoqda...";
				break;
			case SyncStatus.Offline:
				SyncBadge.Background = WarningSoftBrush;
				SyncText.Foreground = WarningInkBrush;
				SyncText.Text = "Aloqa yo'q — javoblar kompyuterda";
				_shell.SetConnection(online: false);
				break;
			}
		}
	}

	private void OnSyncSaved(AnswerSync source)
	{
		if (!_disposed && source == _sync)
		{
			PersistLocal();
			UpdateSavedText();
			UpdateNavigator();
		}
	}

	private void OnSyncStopped(AnswerSync source, SyncStopReason reason)
	{
		if (_disposed || source != _sync)
		{
			return;
		}
		switch (reason)
		{
		case SyncStopReason.Locked:
			if (_phase == Phase.Running)
			{
				RefreshLockFromServerAsync();
			}
			break;
		case SyncStopReason.TimeExpired:
			if (_phase == Phase.Running)
			{
				SubmitAsync("AUTO_TIME", automatic: true);
			}
			break;
		case SyncStopReason.Unauthorized:
			HandleUnauthorized();
			break;
		case SyncStopReason.AttemptClosed:
		{
			Phase phase = _phase;
			if ((uint)(phase - 1) <= 1u)
			{
				FinishAsync("Test yakunlangan", "Bu test serverda allaqachon yakunlangan.");
			}
			break;
		}
		}
	}

	private void OnTick()
	{
		if (_disposed)
		{
			return;
		}
		long elapsedMilliseconds = _stopwatch.ElapsedMilliseconds;
		double num = Math.Clamp((double)(elapsedMilliseconds - _lastTickMs) / 1000.0, 0.0, 5.0);
		_lastTickMs = elapsedMilliseconds;
		if (_phase == Phase.Running && _session.Questions.Count > 0)
		{
			_timeCarrySec += num;
			int num2 = (int)_timeCarrySec;
			if (num2 > 0)
			{
				_timeCarrySec -= num2;
				_session.AddTime(_session.Questions[_index].Id, num2);
			}
		}
		UpdateTimerText();
		TimeSpan remaining = Remaining;
		if (_phase == Phase.Running && remaining <= TimeSpan.Zero)
		{
			SubmitAsync("AUTO_TIME", automatic: true);
		}
		else if (_phase == Phase.Locked && !_lockIsInterruption && !_expiredSubmitTried && remaining <= -ServerGrace)
		{
			_expiredSubmitTried = true;
			SubmitExpiredWhileLockedAsync();
		}
	}

	private void UpdateTimerText()
	{
		TimeSpan timeSpan = Remaining;
		if (timeSpan < TimeSpan.Zero)
		{
			timeSpan = TimeSpan.Zero;
		}
		TimerText.Text = ((timeSpan.TotalHours >= 1.0) ? $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}" : $"{timeSpan.Minutes:00}:{timeSpan.Seconds:00}");
		bool flag = timeSpan <= TimeSpan.FromMinutes(5L);
		TimerText.Foreground = (flag ? DangerInkBrush : BlueInkBrush);
		TimerIcon.Stroke = (flag ? DangerInkBrush : BlueInkBrush);
		TimerBadge.Background = (flag ? DangerSoftBrush : BlueSoftBrush);
		int extraTimeMinutes = _session.ExtraTimeMinutes;
		if (extraTimeMinutes > 0)
		{
			ExtraTimeText.Text = $"+{extraTimeMinutes} daqiqa qo'shimcha vaqt";
			ExtraTimeText.Visibility = Visibility.Visible;
		}
		else
		{
			ExtraTimeText.Visibility = Visibility.Collapsed;
		}
	}

	private async Task HeartbeatAsync()
	{
		if (_disposed || _heartbeatBusy || _phase == Phase.Finished)
		{
			return;
		}
		_heartbeatBusy = true;
		try
		{
			ApiResult<HeartbeatResponse> apiResult = await _services.Api.HeartbeatAsync(_testResultId);
			if (_disposed)
			{
				return;
			}
			if (apiResult.IsUnauthorized)
			{
				HandleUnauthorized();
				return;
			}
			if (apiResult.Ok && apiResult.Value != null)
			{
				HeartbeatResponse value = apiResult.Value;
				_services.Clock.Sync(value.ServerTime);
				_shell.SetConnection(online: true);
				int extraTimeMinutes = _session.ExtraTimeMinutes;
				if (_session.UpdateExtraTime(value.ExtraTimeMinutes) && value.ExtraTimeMinutes > extraTimeMinutes)
				{
					ShowNotice($"Sizga {value.ExtraTimeMinutes - extraTimeMinutes} daqiqa qo'shimcha vaqt berildi.");
				}
				if (value.Proctor != null)
				{
					ApplyProctorSettings(value.Proctor);
				}
				if (value.IsCompleted)
				{
					Phase phase = _phase;
					if ((uint)(phase - 4) > 1u)
					{
						await FinishAsync("Test yakunlangan", "Test serverda yakunlangan. Javoblaringiz saqlangan.");
					}
					return;
				}
				if (value.CheatLocked && _phase == Phase.Running)
				{
					await RefreshLockFromServerAsync();
				}
			}
			else if (apiResult.IsNetworkError)
			{
				_shell.SetConnection(online: false);
			}
			await FlushSignalsAsync();
			if ((double)(_stopwatch.ElapsedMilliseconds - _lastRenewMs) >= RenewInterval.TotalMilliseconds)
			{
				_lastRenewMs = _stopwatch.ElapsedMilliseconds;
				if ((await _services.Api.RenewSessionAsync()).Ok && !_disposed)
				{
					_services.PersistSession(_services.Store.StudentName);
				}
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Heartbeat xatosi");
		}
		finally
		{
			_heartbeatBusy = false;
		}
	}

	private void HandleUnauthorized()
	{
		if (_phase != Phase.Finished && !_disposed)
		{
			_phase = Phase.Finished;
			PersistLocal();
			_services.ForgetSession();
			_shell.ShowLogin("Sessiya tugadi yoki hisobingizga boshqa joydan kirildi. Belgilagan javoblaringiz saqlangan — qayta kirsangiz test davom etadi.");
		}
	}

	private void SetupCamera()
	{
		ProctorSettings proctor = _services.Proctor;
		if (!_session.IsProctored || proctor.IsOff || _cameraMode == CameraMode.NotRequired)
		{
			CameraCard.Visibility = Visibility.Collapsed;
			return;
		}
		CameraCard.Visibility = Visibility.Visible;
		if (_cameraMode != CameraMode.Active)
		{
			CameraImage.Source = null;
			CameraPlaceholderText.Visibility = Visibility.Visible;
			CameraPlaceholderText.Text = ((_cameraMode == CameraMode.Waived) ? "Nazoratchi ruxsati bilan kamerasiz" : "Kamera yo'q");
			CameraDot.Fill = IdleDotBrush;
			CameraStatusText.Text = "Kamerasiz rejim";
			WarningsText.Text = "Bu holat qayd etilgan.";
			WarningsText.Foreground = MutedBrush;
			return;
		}
		CameraProctor proctor2 = new CameraProctor(proctor, _services.Config.CameraIndex);
		proctor2.Preview += OnPreviewFrame;
		proctor2.Incident += delegate(ProctorIncident incident)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnIncident(proctor2, incident);
			});
		};
		proctor2.Recovered += delegate(ProctorCondition condition)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnRecovered(proctor2, condition);
			});
		};
		proctor2.ConditionChanged += delegate(ProctorCondition condition)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnConditionChanged(proctor2, condition);
			});
		};
		proctor2.Faulted += delegate(string message)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnProctorFaulted(proctor2, message);
			});
		};
		proctor2.Paused = _phase != Phase.Running;
		_proctor = proctor2;
		_cameraCondition = ProctorCondition.Ok;
		proctor2.RequestReset(4000);
		proctor2.Start();
		CameraPlaceholderText.Text = "Kamera ishga tushmoqda...";
		CameraPlaceholderText.Visibility = ((_previewBitmap != null) ? Visibility.Collapsed : Visibility.Visible);
		CameraStatusText.Text = IdleCameraText();
		CameraStatusText.Foreground = TextBrush;
		CameraDot.Fill = SuccessBrush;
		UpdateWarningsText();
	}

	private string IdleCameraText()
	{
		if (!_services.Proctor.IsShadow)
		{
			return "Nazorat faol";
		}
		return "Nazorat (sinov rejimi)";
	}

	private void StopCamera()
	{
		CameraProctor proctor = _proctor;
		_proctor = null;
		if (proctor != null)
		{
			proctor.Preview -= OnPreviewFrame;
			Task.Run((Action)proctor.Dispose);
		}
	}

	private void ApplyProctorSettings(ProctorSettings incoming)
	{
		ProctorSettings proctorSettings = incoming.Sanitized();
		ProctorSettings proctor = _services.Proctor;
		if (proctorSettings.SameAs(proctor))
		{
			return;
		}
		_services.Proctor = proctorSettings;
		if (_disposed || _phase == Phase.Finished || !_session.IsProctored)
		{
			return;
		}
		Logger.Info("Kamera nazorati sozlamasi yangilandi: " + proctor.Mode + " -> " + proctorSettings.Mode);
		QueueSignal("PROCTOR_SETTINGS", proctor.Mode + ">" + proctorSettings.Mode);
		if (proctorSettings.IsOff)
		{
			if (_phase == Phase.Warning)
			{
				CancelWarning();
			}
			_proctorRestartPending = false;
			StopCamera();
			CameraImage.Source = null;
			_previewBitmap = null;
			CameraCard.Visibility = Visibility.Collapsed;
			return;
		}
		if (proctorSettings.IsShadow && _phase == Phase.Warning)
		{
			CancelWarning();
		}
		if (_cameraMode == CameraMode.Active)
		{
			Phase phase = _phase;
			if ((uint)(phase - 2) <= 2u)
			{
				_proctorRestartPending = true;
			}
			else
			{
				RestartCamera();
			}
		}
	}

	private void RestartCamera()
	{
		_proctorRestartPending = false;
		StopCamera();
		_previewBitmap = null;
		SetupCamera();
	}

	private void ApplyPendingProctorRestart()
	{
		if (_proctorRestartPending && _phase == Phase.Running && _cameraMode == CameraMode.Active && !_services.Proctor.IsOff)
		{
			RestartCamera();
		}
	}

	private void OnPreviewFrame(PreviewFrame frame)
	{
		if (_disposed || Interlocked.Exchange(ref _previewBusy, 1) == 1)
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			try
			{
				if (!_disposed && _proctor != null)
				{
					if (_previewBitmap == null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
					{
						_previewBitmap = new WriteableBitmap(frame.Width, frame.Height, 96.0, 96.0, PixelFormats.Bgr24, null);
						CameraImage.Source = _previewBitmap;
						CameraPlaceholderText.Visibility = Visibility.Collapsed;
						_services.Store.MarkCameraSeen();
					}
					else if (CameraImage.Source != _previewBitmap)
					{
						CameraImage.Source = _previewBitmap;
						CameraPlaceholderText.Visibility = Visibility.Collapsed;
					}
					_previewBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgr, frame.Stride, 0);
				}
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Kamera kadrini ko'rsatib bo'lmadi");
			}
			finally
			{
				Volatile.Write(ref _previewBusy, 0);
			}
		}, DispatcherPriority.Background);
	}

	private void OnConditionChanged(CameraProctor source, ProctorCondition condition)
	{
		if (_disposed || source != _proctor)
		{
			return;
		}
		if (condition != ProctorCondition.Ok && _cameraCondition == ProctorCondition.Ok)
		{
			_cameraNotOkSinceMs = _stopwatch.ElapsedMilliseconds;
		}
		_cameraCondition = condition;
		if (_phase == Phase.Running)
		{
			if (condition == ProctorCondition.Ok)
			{
				CameraDot.Fill = SuccessBrush;
				CameraStatusText.Text = IdleCameraText();
				CameraStatusText.Foreground = TextBrush;
			}
			else
			{
				CameraDot.Fill = WarningBrush;
				CameraStatusText.Text = condition.ToMessage();
				CameraStatusText.Foreground = WarningInkBrush;
			}
		}
	}

	private void OnProctorFaulted(CameraProctor source, string message)
	{
		if (!_disposed && source == _proctor)
		{
			QueueSignal("PROCTOR_FAULT", message);
			StopCamera();
			_cameraMode = CameraMode.Missing;
			CameraImage.Source = null;
			_previewBitmap = null;
			CameraPlaceholderText.Visibility = Visibility.Visible;
			CameraPlaceholderText.Text = "Kamera ishlamayapti";
			CameraDot.Fill = IdleDotBrush;
			CameraStatusText.Text = "Nazorat ishlamayapti";
			CameraStatusText.Foreground = TextBrush;
			WarningsText.Text = "Bu holat qayd etilgan.";
			WarningsText.Foreground = MutedBrush;
			if (_phase == Phase.Warning)
			{
				CancelWarning();
			}
			FlushSignalsAsync();
		}
	}

	private void UpdateWarningsText()
	{
		ProctorSettings proctor = _services.Proctor;
		if (!proctor.IsEnforce || _proctor == null)
		{
			WarningsText.Text = "";
			return;
		}
		int num = Math.Max(0, proctor.MaxWarnings - _warningsUsed);
		WarningsText.Text = ((num > 0) ? $"Ogohlantirish: {num} ta qoldi" : "Ogohlantirishlar tugadi: keyingi safar blok");
		WarningsText.Foreground = ((num > 0) ? MutedBrush : DangerInkBrush);
	}

	private void OnIncident(CameraProctor source, ProctorIncident incident)
	{
		if (_disposed || source != _proctor)
		{
			return;
		}
		if (_phase != Phase.Running)
		{
			source.RequestReset(3000);
			return;
		}
		ProctorSettings proctor = _services.Proctor;
		string text = incident.Condition.ToViolationType();
		if (proctor.IsShadow)
		{
			QueueSignal("PROCTOR_SHADOW", text);
			UploadSnapshotAsync(text, "SHADOW", incident.Jpeg, incident.Details);
			source.RequestReset(30000);
		}
		else
		{
			StartCountdown(incident, _warningsUsed >= proctor.MaxWarnings);
		}
	}

	private void StartCountdown(ProctorIncident incident, bool final)
	{
		ProctorSettings proctor = _services.Proctor;
		_phase = Phase.Warning;
		_incident = incident;
		_countdownFinal = final;
		_countdownRecovered = false;
		_cameraCondition = incident.Condition;
		_cameraNotOkSinceMs = _stopwatch.ElapsedMilliseconds - 60000;
		_countdownLeft = Math.Max(1, proctor.CountdownSec);
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		WarningTitle.Text = incident.Condition.ToMessage();
		WarningCountdown.Text = _countdownLeft.ToString();
		if (final)
		{
			WarningBadgeText.Text = "QOIDABUZARLIK";
			WarningInstruction.Text = "Ogohlantirishlar tugagan";
			WarningFooter.Text = "Sanoq tugashi bilan test bloklanadi. Testni faqat nazoratchi PIN kod bilan ocha oladi.";
		}
		else
		{
			int num = _warningsUsed + 1;
			WarningBadgeText.Text = $"OGOHLANTIRISH  {num} / {proctor.MaxWarnings}";
			WarningInstruction.Text = incident.Condition.ToInstruction();
			WarningFooter.Text = ((num >= proctor.MaxWarnings) ? "Bu OXIRGI ogohlantirish. Sanoq tugaguncha to'g'rilanmasangiz test bloklanadi. Rasmingiz nazoratchiga yuborildi." : "Sanoq tugaguncha to'g'rilanmasangiz test bloklanadi. Rasmingiz nazoratchiga yuborildi.");
		}
		WarningOverlay.Visibility = Visibility.Visible;
		_renderer?.SetEnabled(enabled: false);
		UpdateOverlayState();
		_countdownTimer.Stop();
		_countdownTimer.Start();
	}

	private void OnCountdownTick()
	{
		if (_phase != Phase.Warning)
		{
			_countdownTimer.Stop();
			return;
		}
		_countdownLeft--;
		if (_countdownLeft > 0)
		{
			WarningCountdown.Text = _countdownLeft.ToString();
			return;
		}
		_countdownTimer.Stop();
		ProctorIncident incident = _incident;
		if (incident == null)
		{
			CancelWarning();
			return;
		}
		bool num = _cameraCondition == ProctorCondition.Ok;
		bool flag = _countdownRecovered && _stopwatch.ElapsedMilliseconds - _cameraNotOkSinceMs < 600;
		bool flag2 = num || flag;
		if (_countdownFinal || !flag2)
		{
			_incident = null;
			WarningOverlay.Visibility = Visibility.Collapsed;
			BlockAsync(incident.Condition, incident.Jpeg, incident.Details);
		}
		else
		{
			CompleteWarning(incident);
		}
	}

	private void OnRecovered(CameraProctor source, ProctorCondition condition)
	{
		if (!_disposed && source == _proctor && _phase == Phase.Warning)
		{
			_countdownRecovered = true;
			if (!_countdownFinal)
			{
				WarningInstruction.Text = "Yaxshi. Shunday davom eting — ekranga qarab o'tiring";
			}
		}
	}

	private void CompleteWarning(ProctorIncident incident)
	{
		CancelWarning();
		_warningsUsed++;
		int used = _warningsUsed;
		_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
		{
			attempt.WarningsUsed = Math.Max(attempt.WarningsUsed, used);
		});
		UpdateWarningsText();
		string text = incident.Condition.ToViolationType();
		QueueSignal("PROCTOR_WARNING", text);
		UploadSnapshotAsync(text, "WARNING", incident.Jpeg, incident.Details);
		FlushSignalsAsync();
		int num = Math.Max(0, _services.Proctor.MaxWarnings - _warningsUsed);
		ShowNotice((num > 0) ? $"Ogohlantirish qayd etildi. Yana {num} ta ogohlantirish qoldi." : "Ogohlantirish qayd etildi. Keyingi qoidabuzarlikda sanoqdan keyin test bloklanadi.");
	}

	private void CancelWarning()
	{
		_countdownTimer.Stop();
		_incident = null;
		_countdownFinal = false;
		_countdownRecovered = false;
		WarningOverlay.Visibility = Visibility.Collapsed;
		if (_phase == Phase.Warning)
		{
			_phase = Phase.Running;
			if (_proctor != null)
			{
				_proctor.RequestReset(4000);
				_proctor.Paused = false;
			}
		}
		_renderer?.SetEnabled(enabled: true);
		UpdateOverlayState();
		ApplyPendingProctorRestart();
	}

	private async Task UploadSnapshotAsync(string type, string phase, byte[]? jpeg, SnapshotDetails details)
	{
		_ = 1;
		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				if (_disposed)
				{
					break;
				}
				ApiResult<SnapshotResponse> apiResult = await _services.Api.UploadSnapshotAsync(_testResultId, type, phase, jpeg, details);
				if (apiResult.Ok && apiResult.Value != null)
				{
					if (phase == "WARNING" && _services.Proctor.IsEnforce && apiResult.Value.WarningCount > _warningsUsed)
					{
						_warningsUsed = apiResult.Value.WarningCount;
						UpdateWarningsText();
					}
					break;
				}
				if (!apiResult.IsTransient)
				{
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)));
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Rasmni yuborib bo'lmadi");
		}
	}

	private static ProctorCondition ParseCondition(string type)
	{
		return type switch
		{
			"LOOKING_AWAY" => ProctorCondition.LookingAway, 
			"MULTIPLE_FACES" => ProctorCondition.MultipleFaces, 
			"NO_FACE" => ProctorCondition.NoFace, 
			"CAMERA_COVERED" => ProctorCondition.CameraCovered, 
			"CAMERA_OFF" => ProctorCondition.CameraOff, 
			_ => ProctorCondition.LookingAway, 
		};
	}

	private async Task BlockAsync(ProctorCondition condition, byte[]? jpeg, SnapshotDetails details)
	{
		bool flag = _disposed;
		if (!flag)
		{
			Phase phase = _phase;
			bool flag2 = (uint)(phase - 3) <= 2u;
			flag = flag2;
		}
		if (flag)
		{
			return;
		}
		string type = condition.ToViolationType();
		_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
		{
			attempt.PendingViolationType = type;
		});
		EnterLock(condition.ToMessage(), isInterruption: false, condition, reported: false);
		try
		{
			if (jpeg != null || condition == ProctorCondition.CameraOff)
			{
				await UploadSnapshotAsync(type, "BLOCK", jpeg, details);
			}
			while (!_disposed && _phase == Phase.Locked)
			{
				ApiResult<CheatResponse> apiResult = await _services.Api.ReportViolationAsync(_testResultId, type, condition.ToMessage());
				if (_disposed)
				{
					break;
				}
				if (apiResult.Ok && apiResult.Value != null)
				{
					_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
					{
						attempt.PendingViolationType = null;
					});
					_shell.SetConnection(online: true);
					if (apiResult.Value.Finalized)
					{
						await FinishAsync("Test yakunlandi", "Qoidabuzarliklar soni ruxsat etilgan chegaradan oshdi. Test avtomatik yakunlandi, belgilangan javoblaringiz qabul qilindi.");
						break;
					}
					_lockReported = true;
					UpdateLockCounters(apiResult.Value.ViolationCount, apiResult.Value.UnlockCount, apiResult.Value.MaxUnlocks);
					UpdateLockControls();
					break;
				}
				if (apiResult.IsUnauthorized)
				{
					HandleUnauthorized();
					break;
				}
				if (apiResult.Status == 404)
				{
					_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
					{
						attempt.PendingViolationType = null;
					});
					await FinishAsync("Test yakunlangan", "Bu test serverda allaqachon yakunlangan.");
					break;
				}
				if (!apiResult.IsTransient)
				{
					Logger.Info($"Blok rad etildi: {apiResult.Status} {apiResult.Error}");
					_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attempt)
					{
						attempt.PendingViolationType = null;
					});
					LeaveLock(resumeCamera: true);
					break;
				}
				_shell.SetConnection(online: false);
				LockStatusText.Text = "Serverga ulanib bo'lmayapti — qayta urinilmoqda...";
				await Task.Delay(TimeSpan.FromSeconds(4L));
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Blokni serverga yuborishda xato");
		}
	}

	private void EnterLock(string reason, bool isInterruption, ProctorCondition condition, bool reported)
	{
		_countdownTimer.Stop();
		_incident = null;
		_phase = Phase.Locked;
		_lockIsInterruption = isInterruption;
		_lockCondition = condition;
		_lockReported = reported;
		_serverUnlocked = false;
		_expiredSubmitTried = false;
		if (_proctor != null)
		{
			_proctor.Paused = true;
		}
		_sync?.Pause();
		WarningOverlay.Visibility = Visibility.Collapsed;
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		BusyOverlay.Visibility = Visibility.Collapsed;
		LockTitle.Text = (isInterruption ? "Test to'xtab qolgan" : "Diqqat! Test bloklandi");
		LockIcon.Stroke = (isInterruption ? WarningBrush : DangerBrush);
		LockReasonText.Text = reason;
		LockInfoText.Text = (isInterruption ? "Kompyuter o'chgani yoki ilova yopilgani sababli test to'xtab qoldi. Nazoratchini chaqiring: PIN kod kiritilgach yo'qotilgan vaqt to'liq qaytariladi va test davom etadi." : "Bu oynani yopish uchun mas'ul xodimni chaqiring. Qoidabuzarlik rasmi nazoratchiga yuborildi.");
		SetLockCounters("");
		LockPinBox.Text = "";
		LockErrorText.Visibility = Visibility.Collapsed;
		LockOverlay.Visibility = Visibility.Visible;
		UpdateLockControls();
		UpdateOverlayState();
	}

	private void UpdateLockControls()
	{
		bool flag = _lockReported && !_unlocking;
		LockPinPanel.IsEnabled = flag;
		UnlockButton.Content = (_unlocking ? "Tekshirilmoqda..." : "PINni tasdiqlash");
		LockStatusText.Text = ((!_lockReported) ? "Blok serverga yuborilmoqda..." : (_unlocking ? "" : "PIN kodni nazoratchi kiritadi."));
		if (flag && LockOverlay.Visibility == Visibility.Visible)
		{
			LockPinBox.Focus();
		}
	}

	private void UpdateLockCounters(int violationCount, int unlockCount, int maxUnlocks)
	{
		if (_lockIsInterruption)
		{
			SetLockCounters("Bu qoidabuzarlik emas — ochish limiti sarflanmaydi.");
			return;
		}
		int num = Math.Max(0, maxUnlocks - unlockCount);
		SetLockCounters((num > 1) ? $"Qoidabuzarlik: {violationCount}-marta. Yana {num} marta ochish mumkin." : ((num == 1) ? $"Qoidabuzarlik: {violationCount}-marta. Bu oxirgi imkoniyat: ochilgandan keyin yana qoida buzilsa, test avtomatik yakunlanadi." : $"Qoidabuzarlik: {violationCount}-marta. Ochish limiti tugagan — keyingi buzilishda test avtomatik yakunlanadi."));
	}

	private void SetLockCounters(string text)
	{
		LockCountersText.Text = text;
		LockReasonPanel.Visibility = ((text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void LeaveLock(bool resumeCamera)
	{
		LockOverlay.Visibility = Visibility.Collapsed;
		_phase = Phase.Running;
		_lockIsInterruption = false;
		_lockReported = true;
		_unlocking = false;
		_serverUnlocked = false;
		_sync?.Resume();
		if (_proctor != null && resumeCamera)
		{
			_proctor.RequestReset(6000);
			_proctor.Paused = false;
		}
		ShowQuestion(_index);
		UpdateOverlayState();
		ApplyPendingProctorRestart();
	}

	private async Task RefreshLockFromServerAsync()
	{
		bool flag = _disposed || _refreshingLock;
		if (!flag)
		{
			Phase phase = _phase;
			bool flag2 = (uint)(phase - 3) <= 2u;
			flag = flag2;
		}
		if (flag)
		{
			return;
		}
		_refreshingLock = true;
		try
		{
			ApiResult<StartResponse> apiResult = await _services.Api.StartTestAsync(_testId);
			flag = _disposed;
			if (!flag)
			{
				Phase phase = _phase;
				bool flag2 = (uint)(phase - 3) <= 2u;
				flag = flag2;
			}
			if (flag)
			{
				return;
			}
			if (apiResult.Ok && apiResult.Value != null)
			{
				_services.Clock.Sync(apiResult.Value.ServerTime);
				if (apiResult.Value.TestResult.CheatLocked)
				{
					if (_phase == Phase.Warning)
					{
						CancelWarning();
					}
					CheatStatus cheatStatus = apiResult.Value.CheatStatus;
					EnterLock(cheatStatus?.Reason ?? apiResult.Value.TestResult.CheatReason ?? "Test bloklangan", cheatStatus?.IsInterruption ?? false, ProctorCondition.Ok, reported: true);
					UpdateLockCounters(apiResult.Value.TestResult.ViolationCount, apiResult.Value.TestResult.UnlockCount, apiResult.Value.TestResult.MaxUnlocks);
				}
				else
				{
					_sync?.Resume();
				}
			}
			else if (apiResult.IsUnauthorized)
			{
				HandleUnauthorized();
			}
			else if (!apiResult.IsTransient)
			{
				await FinishAsync("Test yakunlangan", apiResult.ErrorOr("Test serverda yakunlangan."));
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Blok holatini olishda xato");
		}
		finally
		{
			_refreshingLock = false;
		}
	}

	private void OnLockPinKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			UnlockAsync();
		}
	}

	private void OnUnlockClick(object sender, RoutedEventArgs e)
	{
		UnlockAsync();
	}

	private async Task UnlockAsync()
	{
		if (_phase != Phase.Locked || _unlocking || !_lockReported)
		{
			return;
		}
		string text = LockPinBox.Text.Trim();
		if (text.Length == 0 && !_serverUnlocked)
		{
			ShowLockError("PIN kodni kiriting");
			return;
		}
		_unlocking = true;
		LockErrorText.Visibility = Visibility.Collapsed;
		UpdateLockControls();
		try
		{
			if (_serverUnlocked)
			{
				LockStatusText.Text = "Test ochilmoqda...";
				await ReloadAfterUnlockAsync(0);
				return;
			}
			ApiResult<CheatVerifyResponse> apiResult = await _services.Api.VerifyUnlockPinAsync(_testResultId, text);
			if (!_disposed)
			{
				_shell.SetConnection(!apiResult.IsNetworkError);
				if (apiResult.IsUnauthorized)
				{
					HandleUnauthorized();
				}
				else if (!apiResult.Ok || apiResult.Value == null)
				{
					LockPinBox.Text = "";
					ShowLockError(apiResult.ErrorOr("PIN kodni tekshirib bo'lmadi"));
				}
				else
				{
					_serverUnlocked = true;
					LockStatusText.Text = "Test ochilmoqda...";
					await ReloadAfterUnlockAsync(apiResult.Value.CompensatedMinutes);
				}
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "PIN tekshirishda xato");
			ShowLockError("Kutilmagan xato. Qayta urinib ko'ring.");
		}
		finally
		{
			_unlocking = false;
			if (!_disposed && _phase == Phase.Locked)
			{
				UpdateLockControls();
			}
		}
	}

	private async Task ReloadAfterUnlockAsync(int compensatedMinutes)
	{
		ApiResult<StartResponse> start = null;
		for (int attempt = 0; attempt < 6; attempt++)
		{
			if (_disposed)
			{
				break;
			}
			start = await _services.Api.StartTestAsync(_testId);
			if (!start.IsTransient)
			{
				break;
			}
			LockStatusText.Text = "Serverga ulanib bo'lmayapti — qayta urinilmoqda...";
			await Task.Delay(TimeSpan.FromSeconds(3L));
		}
		if (_disposed || start == null)
		{
			return;
		}
		if (start.IsUnauthorized)
		{
			HandleUnauthorized();
			return;
		}
		if (!start.Ok || start.Value == null)
		{
			if (start.IsTransient)
			{
				ShowLockError("PIN qabul qilindi, lekin serverga ulanib bo'lmadi. Internet tiklangach \"PINni tasdiqlash\" tugmasini qayta bosing.");
				_lockReported = true;
			}
			else
			{
				await FinishAsync("Test yakunlangan", start.ErrorOr("Test serverda yakunlangan."));
			}
			return;
		}
		StartResponse value = start.Value;
		_services.Clock.Sync(value.ServerTime);
		if (value.TestResult.CheatLocked)
		{
			CheatStatus cheatStatus = value.CheatStatus;
			EnterLock(cheatStatus?.Reason ?? value.TestResult.CheatReason ?? "Test bloklangan", cheatStatus?.IsInterruption ?? false, ProctorCondition.Ok, reported: true);
			UpdateLockCounters(value.TestResult.ViolationCount, value.TestResult.UnlockCount, value.TestResult.MaxUnlocks);
			return;
		}
		IReadOnlyList<PendingAnswer> pending = _session.GetPending();
		_session = ExamSession.FromStart(value, pending);
		_index = Math.Clamp(_index, 0, Math.Max(0, _session.Questions.Count - 1));
		if (_navButtons.Count != _session.Questions.Count)
		{
			BuildNavigator();
		}
		AttachSync();
		bool flag = _lockCondition == ProctorCondition.CameraOff;
		if (flag && _proctor != null)
		{
			StopCamera();
			_cameraMode = CameraMode.Waived;
			_services.Store.UpdateAttempt(_testResultId, delegate(AttemptLocalState attemptLocalState)
			{
				attemptLocalState.CameraWaived = true;
			});
			QueueSignal("CAMERA_WAIVED", "after CAMERA_OFF unlock");
			SetupCamera();
		}
		LeaveLock(!flag);
		UpdateTimerText();
		if (compensatedMinutes > 0)
		{
			ShowNotice($"Uzilish uchun {compensatedMinutes} daqiqa qaytarildi. Test davom etmoqda.");
		}
		else
		{
			ShowNotice("Test ochildi. Qoidalarga rioya qiling — ochish soni cheklangan.");
		}
		FlushSignalsAsync();
	}

	private void ShowLockError(string message)
	{
		LockErrorText.Text = message;
		LockErrorText.Visibility = Visibility.Visible;
	}

	private async Task SubmitExpiredWhileLockedAsync()
	{
		_ = 2;
		try
		{
			ApiResult<SubmitResponse> apiResult = await _services.Api.SubmitTestAsync(_testResultId, "AUTO_TIME");
			if (!_disposed)
			{
				if (apiResult.Ok)
				{
					await FinishAsync("Test vaqti tugadi", "Test bloklangan holatda vaqt tugadi. Belgilangan javoblaringiz qabul qilindi.");
				}
				else if (apiResult.IsTransient)
				{
					_expiredSubmitTried = false;
					await Task.Delay(TimeSpan.FromSeconds(5L));
				}
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Vaqti tugagan testni yopishda xato");
		}
	}

	private void OnFinishClick(object sender, RoutedEventArgs e)
	{
		if (_phase == Phase.Running)
		{
			int count = _session.Questions.Count;
			int answeredCount = _session.AnsweredCount;
			int num = Math.Max(0, count - answeredCount);
			ConfirmTotalText.Text = $"Jami savollar: {count} ta";
			ConfirmAnsweredText.Text = $"Javob berilgan: {answeredCount} ta";
			ConfirmUnansweredText.Text = $"Javob berilmagan: {num} ta";
			ConfirmUnansweredText.Foreground = ((num > 0) ? DangerInkBrush : TextBrush);
			ConfirmUnansweredText.FontWeight = ((num > 0) ? FontWeights.SemiBold : FontWeights.Normal);
			if (num > 0)
			{
				ConfirmText.Text = $"Siz hali {num} ta savolga javob bermadingiz.";
				ConfirmAlert.Visibility = Visibility.Visible;
			}
			else
			{
				ConfirmAlert.Visibility = Visibility.Collapsed;
			}
			ConfirmOverlay.Visibility = Visibility.Visible;
			UpdateOverlayState();
		}
	}

	private void OnConfirmCancelClick(object sender, RoutedEventArgs e)
	{
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		UpdateOverlayState();
	}

	private void OnConfirmFinishClick(object sender, RoutedEventArgs e)
	{
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		SubmitAsync("MANUAL", automatic: false);
	}

	private async Task SubmitAsync(string completionType, bool automatic)
	{
		bool flag = _disposed;
		if (!flag)
		{
			Phase phase = _phase;
			bool flag2 = (uint)(phase - 3) <= 2u;
			flag = flag2;
		}
		if (flag)
		{
			return;
		}
		if (_phase == Phase.Warning)
		{
			CancelWarning();
		}
		_phase = Phase.Submitting;
		if (_proctor != null)
		{
			_proctor.Paused = true;
		}
		ConfirmOverlay.Visibility = Visibility.Collapsed;
		ShowBusy(automatic ? "Test vaqti tugadi" : "Test yakunlanmoqda", "Javoblar serverga yuborilmoqda...");
		try
		{
			int attempt = 0;
			while (!_disposed)
			{
				attempt++;
				ApiResult<Empty> apiResult = ((_sync != null) ? (await _sync.FlushAsync(CancellationToken.None)) : ApiResult<Empty>.Success(Empty.Value, 200));
				ApiResult<Empty> apiResult2 = apiResult;
				if (_disposed)
				{
					break;
				}
				if (apiResult2.IsUnauthorized)
				{
					HandleUnauthorized();
					break;
				}
				if (apiResult2.Status == 423)
				{
					ResumeAfterSubmitAttempt();
					await RefreshLockFromServerAsync();
					break;
				}
				if (apiResult2.Ok || !apiResult2.IsTransient)
				{
					ApiResult<SubmitResponse> apiResult3 = await _services.Api.SubmitTestAsync(_testResultId, completionType);
					if (_disposed)
					{
						break;
					}
					if (apiResult3.Ok || (apiResult3.Status == 400 && !apiResult3.IsTransient))
					{
						await FinishAsync(automatic ? "Test vaqti tugadi" : "Test topshirildi", "Javoblaringiz qabul qilindi.");
						break;
					}
					if (apiResult3.IsUnauthorized)
					{
						HandleUnauthorized();
						break;
					}
					if (apiResult3.Status == 423)
					{
						ResumeAfterSubmitAttempt();
						await RefreshLockFromServerAsync();
						break;
					}
					if (apiResult3.Status == 404)
					{
						await FinishAsync("Test yakunlangan", "Bu test serverda allaqachon yakunlangan.");
						break;
					}
				}
				_shell.SetConnection(online: false);
				if (!automatic && attempt >= 2 && Remaining > TimeSpan.FromSeconds(30L))
				{
					ResumeAfterSubmitAttempt();
					ShowQuestion(_index);
					ShowNotice("Serverga ulanib bo'lmadi. Javoblaringiz kompyuterda saqlangan — internet tiklangach qayta yakunlang.");
					break;
				}
				BusyDetail.Text = "Internet aloqasi yo'q. Javoblar kompyuterda saqlangan — aloqa tiklanishi bilan yuboriladi. Kompyuterni o'chirmang.";
				await Task.Delay(TimeSpan.FromSeconds(5L));
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Testni topshirishda xato");
			if (!_disposed && _phase == Phase.Submitting)
			{
				ResumeAfterSubmitAttempt();
				ShowQuestion(_index);
				ShowNotice("Kutilmagan xato. Qayta urinib ko'ring.");
			}
		}
	}

	private void ResumeAfterSubmitAttempt()
	{
		_phase = Phase.Running;
		if (_proctor != null)
		{
			_proctor.RequestReset(4000);
			_proctor.Paused = false;
		}
		HideBusy();
		ApplyPendingProctorRestart();
	}

	private async Task FinishAsync(string title, string message)
	{
		if (!_disposed && _phase != Phase.Finished)
		{
			_phase = Phase.Finished;
			_tickTimer.Stop();
			_heartbeatTimer.Stop();
			_countdownTimer.Stop();
			StopCamera();
			_sync?.Dispose();
			_sync = null;
			_services.Store.RemoveAttempt(_testResultId);
			LockOverlay.Visibility = Visibility.Collapsed;
			WarningOverlay.Visibility = Visibility.Collapsed;
			ShowBusy(title, "Natija tayyorlanmoqda...");
			await Task.Yield();
			if (!_disposed)
			{
				_shell.ShowFinished(title, message, _testResultId);
			}
		}
	}

	private void ShowBusy(string text, string detail)
	{
		BusyText.Text = text;
		BusyDetail.Text = detail;
		BusyOverlay.Visibility = Visibility.Visible;
		UpdateOverlayState();
	}

	private void HideBusy()
	{
		BusyOverlay.Visibility = Visibility.Collapsed;
		UpdateOverlayState();
	}

	private void ShowNotice(string message)
	{
		NoticeText.Text = message;
		NoticeBar.Visibility = Visibility.Visible;
		_noticeTimer.Stop();
		_noticeTimer.Start();
	}

	private void UpdateOverlayState()
	{
		bool flag = LockOverlay.Visibility == Visibility.Visible || WarningOverlay.Visibility == Visibility.Visible || ConfirmOverlay.Visibility == Visibility.Visible || BusyOverlay.Visibility == Visibility.Visible;
		bool flag2 = !_rendererFailed && !flag;
		if (flag2)
		{
			Phase phase = _phase;
			bool flag3 = (uint)phase <= 1u;
			flag2 = flag3;
		}
		bool flag4 = flag2;
		QuestionView.Visibility = ((!flag4) ? Visibility.Hidden : Visibility.Visible);
		RendererStatusText.Visibility = (_rendererReady ? Visibility.Collapsed : Visibility.Visible);
		FinishButton.IsEnabled = _phase == Phase.Running;
	}

	private void OnShellFocusLost()
	{
		QueueSignal("KIOSK_FOCUS_LOST");
	}

	private void QueueSignal(string kind, string? detail = null)
	{
		if (!_disposed)
		{
			object obj;
			if (_signals.Count <= 0)
			{
				obj = null;
			}
			else
			{
				List<ClientSignal> signals = _signals;
				obj = signals[signals.Count - 1];
			}
			ClientSignal clientSignal = (ClientSignal)obj;
			if (clientSignal != null && clientSignal.Kind == kind && clientSignal.Detail == detail)
			{
				clientSignal.Count = Math.Min(clientSignal.Count + 1, 10000);
			}
			else if (_signals.Count < 50)
			{
				_signals.Add(new ClientSignal
				{
					Kind = kind,
					At = _services.Clock.Now.ToUniversalTime(),
					Detail = ((detail == null) ? null : ((detail.Length <= 120) ? detail : detail.Substring(0, 120)))
				});
			}
		}
	}

	private async Task FlushSignalsAsync()
	{
		if (_disposed || _signals.Count == 0)
		{
			return;
		}
		List<ClientSignal> batch = _signals.ToList();
		_signals.Clear();
		try
		{
			ApiResult<Empty> apiResult = await _services.Api.SendSignalsAsync(_testResultId, batch);
			if (!apiResult.Ok && apiResult.IsTransient && !_disposed && _signals.Count + batch.Count <= 50)
			{
				_signals.InsertRange(0, batch);
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Signallarni yuborib bo'lmadi");
		}
	}

	private static int MonitorCount()
	{
		if (!(SystemParameters.VirtualScreenWidth > SystemParameters.PrimaryScreenWidth + 1.0) && !(SystemParameters.VirtualScreenHeight > SystemParameters.PrimaryScreenHeight + 1.0))
		{
			return 1;
		}
		return 2;
	}

	private static Brush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		Phase phase = _phase;
		if ((uint)(phase - 1) <= 3u)
		{
			try
			{
				PersistLocal();
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Holatni saqlashda xato");
			}
		}
		_disposed = true;
		_shell.FocusLost -= OnShellFocusLost;
		_tickTimer.Stop();
		_heartbeatTimer.Stop();
		_countdownTimer.Stop();
		_noticeTimer.Stop();
		StopCamera();
		_sync?.Dispose();
		_sync = null;
		try
		{
			QuestionView.Dispose();
		}
		catch (Exception exception2)
		{
			Logger.Error(exception2, "Savol chizuvchini yopishda xato");
		}
	}
}
