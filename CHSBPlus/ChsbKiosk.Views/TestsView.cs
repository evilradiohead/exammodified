using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Proctor;
using ChsbKiosk.Proctor;

namespace ChsbKiosk.Views;

public partial class TestsView : UserControl, IRefreshableView, IDisposable, IComponentConnector, IStyleConnector
{
	private enum CameraCheck
	{
		NotNeeded,
		Checking,
		Available,
		MissingAllowed,
		MissingBlocked,
		Waived
	}

	public sealed class TestCard
	{
		public KioskTest Test { get; }

		public string Title { get; }

		public string Description { get; }

		public string DurationText { get; }

		public string QuestionsText { get; }

		public IReadOnlyList<string> SubjectList { get; }

		public Visibility SubjectsVisibility
		{
			get
			{
				if (SubjectList.Count <= 0)
				{
					return Visibility.Collapsed;
				}
				return Visibility.Visible;
			}
		}

		public string? ActiveToText { get; }

		public Visibility ActiveToVisibility
		{
			get
			{
				if (!string.IsNullOrEmpty(ActiveToText))
				{
					return Visibility.Visible;
				}
				return Visibility.Collapsed;
			}
		}

		public string BadgeText { get; }

		public Brush BadgeBackground { get; }

		public Brush BadgeForeground { get; }

		public Visibility BadgeVisibility
		{
			get
			{
				if (!string.IsNullOrEmpty(BadgeText))
				{
					return Visibility.Visible;
				}
				return Visibility.Collapsed;
			}
		}

		public string? Status { get; }

		public Brush StatusBrush { get; private set; } = MutedBrush;

		public Brush StatusBackground { get; private set; } = Brushes.Transparent;

		public Brush StatusBorder { get; private set; } = Brushes.Transparent;

		public Visibility StatusVisibility
		{
			get
			{
				if (!string.IsNullOrEmpty(Status))
				{
					return Visibility.Visible;
				}
				return Visibility.Collapsed;
			}
		}

		public string ButtonText { get; }

		public bool CanStart { get; }

		public TestCard(KioskTest test)
		{
			Test = test;
			Title = test.DisplayTitle;
			List<string> list = new List<string>();
			if (!string.IsNullOrWhiteSpace(test.Title) && !string.IsNullOrWhiteSpace(test.Variant))
			{
				list.Add("Variant " + test.Variant);
			}
			if (!string.IsNullOrWhiteSpace(test.Grade))
			{
				list.Add(test.Grade);
			}
			Description = ((list.Count > 0) ? string.Join("  ·  ", list) : "Chorak sinovi");
			DurationText = $"{test.Duration} daqiqa";
			QuestionsText = $"{test.QuestionCount} ta savol";
			SubjectList = test.Subjects.Select((TestSubjectInfo s) => $"{s.Name} ({s.QuestionCount})").ToList();
			DateTimeOffset? activeTo = test.ActiveTo;
			if (activeTo.HasValue)
			{
				DateTimeOffset valueOrDefault = activeTo.GetValueOrDefault();
				ActiveToText = $"{valueOrDefault.ToLocalTime():dd.MM.yyyy HH:mm} gacha ochiq";
			}
			if (test.IsBlocked)
			{
				Status = test.BlockedReason ?? "Bu testni hozir boshlab bo'lmaydi";
				SetStatusColors(DangerInkBrush, DangerSoftBrush, DangerBorderBrush);
				BadgeText = "Yopiq";
				BadgeBackground = DangerSoftBrush;
				BadgeForeground = DangerInkBrush;
			}
			else if (test.CheatLocked)
			{
				Status = "Test bloklangan — ochish uchun nazoratchi PIN kodi kerak";
				SetStatusColors(DangerInkBrush, DangerSoftBrush, DangerBorderBrush);
				BadgeText = "Bloklangan";
				BadgeBackground = DangerSoftBrush;
				BadgeForeground = DangerInkBrush;
			}
			else if (test.InProgress)
			{
				Status = "Test boshlangan — vaqt ketmoqda";
				SetStatusColors(WarningInkBrush, WarningSoftBrush, WarningBorderBrush);
				BadgeText = "Davom etmoqda";
				BadgeBackground = WarningSoftBrush;
				BadgeForeground = WarningInkBrush;
			}
			else if (test.IsDemo)
			{
				BadgeText = "Sinov testi";
				BadgeBackground = WarningSoftBrush;
				BadgeForeground = WarningInkBrush;
			}
			else
			{
				BadgeText = "Faol";
				BadgeBackground = SuccessSoftBrush;
				BadgeForeground = SuccessInkBrush;
			}
			ButtonText = (test.InProgress ? "Davom ettirish" : "Testni boshlash");
			CanStart = !test.IsBlocked;
		}

		private void SetStatusColors(Brush text, Brush background, Brush border)
		{
			StatusBrush = text;
			StatusBackground = background;
			StatusBorder = border;
		}
	}

	private static readonly Brush DangerInkBrush = Frozen(185, 28, 28);

	private static readonly Brush DangerSoftBrush = Frozen(254, 242, 242);

	private static readonly Brush DangerBorderBrush = Frozen(254, 202, 202);

	private static readonly Brush WarningInkBrush = Frozen(120, 53, 15);

	private static readonly Brush WarningSoftBrush = Frozen(254, 243, 199);

	private static readonly Brush WarningBorderBrush = Frozen(252, 211, 77);

	private static readonly Brush SuccessInkBrush = Frozen(22, 101, 52);

	private static readonly Brush SuccessSoftBrush = Frozen(220, 252, 231);

	private static readonly Brush MutedBrush = Frozen(92, 111, 138);

	private static readonly Brush TextBrush = Frozen(22, 33, 60);

	private readonly IShell _shell;

	private readonly DispatcherTimer _autoRefresh;

	private bool _loading;

	private bool _starting;

	private bool _disposed;

	private bool _autoResumeOffered;

	private PendingSurveyInfo? _survey;

	private KioskTest? _selected;

	private bool _pinRequired;

	private CameraCheck _camera;

	private int _cameraProbeId;

	private static Brush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	public TestsView(IShell shell, TestsResponse? preloaded)
	{
		_shell = shell;
		InitializeComponent();
		OrganizationText.Text = "Ixtisoslashtirilgan ta'lim muassasalari agentligi";
		_autoRefresh = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(30L)
		};
		_autoRefresh.Tick += delegate
		{
			if (StartOverlay.Visibility != Visibility.Visible)
			{
				LoadAsync(silent: true);
			}
		};
		_autoRefresh.Start();
		if (preloaded != null)
		{
			Apply(preloaded);
			return;
		}
		StudentNameText.Text = _shell.Services.Store.StudentName ?? "Yuklanmoqda...";
		LoadAsync(silent: false);
	}

	public void Refresh()
	{
		if (StartOverlay.Visibility != Visibility.Visible)
		{
			LoadAsync(silent: false);
		}
	}

	public void Dispose()
	{
		_disposed = true;
		_autoRefresh.Stop();
		_cameraProbeId++;
	}

	private async Task LoadAsync(bool silent)
	{
		if (_loading || _disposed)
		{
			return;
		}
		_loading = true;
		RefreshButton.IsEnabled = false;
		try
		{
			ApiResult<TestsResponse> apiResult = await _shell.Services.Api.GetTestsAsync();
			if (_disposed)
			{
				return;
			}
			_shell.SetConnection(!apiResult.IsNetworkError);
			if (apiResult.IsUnauthorized)
			{
				_shell.Services.ForgetSession();
				_shell.ShowLogin("Sessiya tugagan yoki hisobingizga boshqa kompyuterdan kirilgan. Qayta kiring.");
			}
			else if (!apiResult.Ok || apiResult.Value == null)
			{
				if (!silent || TestsList.Items.Count == 0)
				{
					ShowMessage(apiResult.ErrorOr("Testlar ro'yxatini yuklab bo'lmadi"));
				}
			}
			else
			{
				MessagePanel.Visibility = Visibility.Collapsed;
				Apply(apiResult.Value);
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Testlar ro'yxati xatosi");
		}
		finally
		{
			_loading = false;
			RefreshButton.IsEnabled = true;
		}
	}

	private void Apply(TestsResponse response)
	{
		_shell.Services.Clock.Sync(response.ServerTime);
		if (response.Proctor != null)
		{
			_shell.Services.Proctor = response.Proctor.Sanitized();
		}
		StudentInfo student = response.Student;
		StudentNameText.Text = (string.IsNullOrWhiteSpace(student.FullName) ? student.Username : student.FullName);
		StudentMetaText.Text = string.Join("  ·  ", new string[3] { student.School, student.Grade, student.Username }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
		_shell.Services.PersistSession(StudentNameText.Text);
		_shell.Services.StudentLogin = student.Username;
		List<TestCard> list = response.Tests.Select((KioskTest test) => new TestCard(test)).ToList();
		TestsList.ItemsSource = list;
		EmptyPanel.Visibility = ((list.Count != 0 || response.PendingSurvey != null) ? Visibility.Collapsed : Visibility.Visible);
		_survey = response.PendingSurvey;
		if (_survey != null)
		{
			SurveyText.Text = $"{_survey.Title} — {_survey.QuestionsCount} ta savol. Fikringiz biz uchun muhim.";
			SurveyPanel.Visibility = Visibility.Visible;
		}
		else
		{
			SurveyPanel.Visibility = Visibility.Collapsed;
		}
		KioskTest kioskTest = response.Tests.FirstOrDefault((KioskTest t) => t.InProgress);
		LogoutButton.IsEnabled = kioskTest == null;
		LogoutButton.ToolTip = ((kioskTest == null) ? null : "Avval boshlangan testni yakunlang");
		UpdatedText.Text = $"Yangilandi: {_shell.Services.Clock.Now.ToLocalTime():HH:mm:ss}";
		if (kioskTest != null && !_autoResumeOffered && StartOverlay.Visibility != Visibility.Visible)
		{
			_autoResumeOffered = true;
			OpenStartDialog(kioskTest);
		}
	}

	private void ShowMessage(string message)
	{
		MessageText.Text = message;
		MessagePanel.Visibility = Visibility.Visible;
	}

	private void OnRefreshClick(object sender, RoutedEventArgs e)
	{
		Refresh();
	}

	private async void OnLogoutClick(object sender, RoutedEventArgs e)
	{
		LogoutButton.IsEnabled = false;
		try
		{
			await _shell.Services.Api.LogoutAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Logout xatosi");
		}
		_shell.Services.ForgetSession();
		_shell.ShowLogin();
	}

	private void OnSurveyClick(object sender, RoutedEventArgs e)
	{
		if (_survey != null)
		{
			_shell.ShowSurvey(_survey.Id, _survey.TestResultId);
		}
	}

	private void OnStartClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is TestCard { CanStart: not false } testCard)
		{
			OpenStartDialog(testCard.Test);
		}
	}

	private void OpenStartDialog(KioskTest test)
	{
		_selected = test;
		_pinRequired = test.RequiresPin;
		_starting = false;
		StartTitle.Text = (test.InProgress ? (test.DisplayTitle + " — davom ettirish") : test.DisplayTitle);
		StartMeta.Text = $"{test.QuestionCount} ta savol  ·  {test.Duration} daqiqa" + (test.InProgress ? "  ·  test vaqti to'xtamagan" : "");
		ProctorSettings proctor = _shell.Services.Proctor;
		bool flag = string.Equals(test.TestType, "CHSB", StringComparison.Ordinal) && !proctor.IsOff && !_shell.Services.Config.CameraNeverRequired;
		RulesText.Text = BuildRules(flag, proctor.MaxWarnings, proctor.CountdownSec);
		PinBox.Text = "";
		PinPanel.Visibility = ((!_pinRequired) ? Visibility.Collapsed : Visibility.Visible);
		ConsentCheck.IsChecked = false;
		StartErrorPanel.Visibility = Visibility.Collapsed;
		StartConfirmButton.Content = (test.InProgress ? "Testni davom ettirish" : "Testni boshlash");
		StartOverlay.Visibility = Visibility.Visible;
		if (flag)
		{
			CameraPanel.Visibility = Visibility.Visible;
			StartCameraProbe();
		}
		else
		{
			CameraPanel.Visibility = Visibility.Collapsed;
			_camera = CameraCheck.NotNeeded;
		}
		UpdateStartButton();
		if (_pinRequired)
		{
			PinBox.Focus();
		}
	}

	private static string BuildRules(bool camera, int maxWarnings, int countdownSec)
	{
		List<string> list = new List<string>();
		if (camera)
		{
			list.Add("Test davomida kamera sizni kuzatib turadi va o'zingizni ekran burchagida ko'rasiz. Video saqlanmaydi — faqat qoidabuzarlik paytidagi rasm nazoratchiga yuboriladi.");
			list.Add("O'ngga yoki chapga qarash, boshqa o'quvchidan ko'chirish testdan chetlatilishingizga olib keladi.");
			list.Add("Kamerani to'sish, o'chirish yoki kamera oldidan ketish taqiqlanadi. Kadrda faqat o'zingiz bo'lishingiz kerak.");
			list.Add((maxWarnings > 0) ? $"Qoidabuzarlik aniqlansa ekran qizarib, {countdownSec} soniyalik sanoq chiqadi va rasmingiz olinadi. Sanoq tugaguncha to'g'rilansangiz — bu ogohlantirish bo'lib qayd etiladi. Ogohlantirish faqat {maxWarnings} marta kechiriladi: keyingi safar sanoq tugashi bilan test bloklanadi." : $"Qoidabuzarlik aniqlansa ekran qizarib, {countdownSec} soniyalik sanoq chiqadi va sanoq tugashi bilan test bloklanadi.");
		}
		list.Add("Telefon, daftar, kitob va boshqa manbalardan foydalanish taqiqlanadi.");
		list.Add("Bloklangan testni faqat nazoratchi PIN kod bilan ocha oladi. Ochish soni cheklangan — limit tugasa test avtomatik yakunlanadi.");
		list.Add("Test vaqti serverda hisoblanadi va to'xtamaydi. Javoblar har belgilaganingizda avtomatik saqlanadi.");
		return string.Join(Environment.NewLine, list.Select((string rule) => "•  " + rule));
	}

	private void StartCameraProbe()
	{
		int probeId = ++_cameraProbeId;
		SetCameraState(CameraCheck.Checking);
		int index = _shell.Services.Config.CameraIndex;
		Task.Run(() => CameraProctor.Probe(index, TimeSpan.FromSeconds(4L))).ContinueWith(delegate(Task<bool> task)
		{
			if (!_disposed && probeId == _cameraProbeId)
			{
				if (task.Status == TaskStatus.RanToCompletion && task.Result)
				{
					_shell.Services.Store.MarkCameraSeen();
					SetCameraState(CameraCheck.Available);
				}
				else
				{
					bool flag = _shell.Services.Config.CameraAlwaysRequired || _shell.Services.Store.CameraSeen;
					SetCameraState(flag ? CameraCheck.MissingBlocked : CameraCheck.MissingAllowed);
				}
			}
		}, TaskScheduler.FromCurrentSynchronizationContext());
	}

	private void SetCameraState(CameraCheck state)
	{
		_camera = state;
		CameraRetryButton.Visibility = Visibility.Collapsed;
		CameraWaiveButton.Visibility = Visibility.Collapsed;
		CameraStatusText.Foreground = TextBrush;
		switch (state)
		{
		case CameraCheck.Checking:
			CameraStatusText.Text = "Kamera tekshirilmoqda...";
			break;
		case CameraCheck.Available:
			CameraStatusText.Text = "Kamera ishlayapti. Test davomida nazorat faol bo'ladi.";
			CameraStatusText.Foreground = SuccessInkBrush;
			break;
		case CameraCheck.MissingAllowed:
			CameraStatusText.Text = "Bu kompyuterda kamera topilmadi. Test kamerasiz o'tadi (bu holat qayd etiladi).";
			CameraRetryButton.Visibility = Visibility.Visible;
			break;
		case CameraCheck.MissingBlocked:
			CameraStatusText.Text = "Kamera topilmadi yoki ishlamayapti. Kamerani ulang yoki nazoratchini chaqiring.";
			CameraStatusText.Foreground = DangerInkBrush;
			CameraRetryButton.Visibility = Visibility.Visible;
			if (!_shell.Services.Config.CameraAlwaysRequired)
			{
				CameraWaiveButton.Visibility = Visibility.Visible;
			}
			break;
		case CameraCheck.Waived:
			CameraStatusText.Text = "Nazoratchi ruxsati bilan test kamerasiz o'tadi (bu holat qayd etiladi).";
			break;
		}
		UpdateStartButton();
	}

	private void OnCameraRetryClick(object sender, RoutedEventArgs e)
	{
		StartCameraProbe();
	}

	private void OnCameraWaiveClick(object sender, RoutedEventArgs e)
	{
		if (_shell.ConfirmSupervisor("Kamerasiz boshlash uchun nazoratchi parolini kiriting", "Ruxsat berish"))
		{
			_cameraProbeId++;
			SetCameraState(CameraCheck.Waived);
		}
	}

	private void OnConsentChanged(object sender, RoutedEventArgs e)
	{
		UpdateStartButton();
	}

	private void OnStartInputChanged(object sender, TextChangedEventArgs e)
	{
		UpdateStartButton();
	}

	private void OnPinKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return && StartConfirmButton.IsEnabled)
		{
			e.Handled = true;
			StartAsync();
		}
	}

	private void UpdateStartButton()
	{
		if (StartConfirmButton != null && StartCancelButton != null && ConsentCheck != null && PinBox != null)
		{
			bool flag;
			switch (_camera)
			{
			case CameraCheck.NotNeeded:
			case CameraCheck.Available:
			case CameraCheck.MissingAllowed:
			case CameraCheck.Waived:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			bool flag2 = flag;
			bool flag3 = !_pinRequired || PinBox.Text.Trim().Length > 0;
			StartConfirmButton.IsEnabled = !_starting && flag2 && flag3 && ConsentCheck.IsChecked == true;
			StartCancelButton.IsEnabled = !_starting;
		}
	}

	private void OnStartCancelClick(object sender, RoutedEventArgs e)
	{
		_cameraProbeId++;
		_selected = null;
		StartOverlay.Visibility = Visibility.Collapsed;
	}

	private void OnStartConfirmClick(object sender, RoutedEventArgs e)
	{
		StartAsync();
	}

	private async Task StartAsync()
	{
		KioskTest test = _selected;
		if (test == null || _starting)
		{
			return;
		}
		_starting = true;
		StartErrorPanel.Visibility = Visibility.Collapsed;
		object originalText = StartConfirmButton.Content;
		StartConfirmButton.Content = "Yuklanmoqda...";
		UpdateStartButton();
		try
		{
			ApiClient api = _shell.Services.Api;
			if (_pinRequired)
			{
				ApiResult<Empty> apiResult = await api.VerifyTestPinAsync(test.Id, PinBox.Text.Trim());
				_shell.SetConnection(!apiResult.IsNetworkError);
				if (!apiResult.Ok)
				{
					ShowStartError(apiResult.ErrorOr("PIN kodni tekshirib bo'lmadi"));
					return;
				}
			}
			ApiResult<StartResponse> apiResult2 = await api.StartTestAsync(test.Id);
			_shell.SetConnection(!apiResult2.IsNetworkError);
			if (!apiResult2.Ok || apiResult2.Value == null)
			{
				if (apiResult2.Code == "PIN_REQUIRED")
				{
					_pinRequired = true;
					PinPanel.Visibility = Visibility.Visible;
					PinBox.Focus();
					ShowStartError("Test PIN kodini kiriting");
				}
				else if (apiResult2.IsUnauthorized)
				{
					_shell.Services.ForgetSession();
					_shell.ShowLogin("Sessiya tugagan. Qayta kiring.");
				}
				else
				{
					ShowStartError(apiResult2.ErrorOr("Testni boshlab bo'lmadi"));
				}
			}
			else if (apiResult2.Value.Test.Questions.Count == 0)
			{
				ShowStartError("Testda savollar topilmadi. Nazoratchiga murojaat qiling.");
			}
			else
			{
				CameraMode camera = _camera switch
				{
					CameraCheck.Available => CameraMode.Active, 
					CameraCheck.MissingAllowed => CameraMode.Missing, 
					CameraCheck.Waived => CameraMode.Waived, 
					_ => CameraMode.NotRequired, 
				};
				_cameraProbeId++;
				_shell.ShowExam(apiResult2.Value, camera);
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Testni boshlashda xato");
			ShowStartError("Kutilmagan xato. Qayta urinib ko'ring.");
		}
		finally
		{
			_starting = false;
			if (!_disposed)
			{
				StartConfirmButton.Content = originalText;
				UpdateStartButton();
			}
		}
	}

	private void ShowStartError(string message)
	{
		StartErrorText.Text = message;
		StartErrorPanel.Visibility = Visibility.Visible;
	}
}
