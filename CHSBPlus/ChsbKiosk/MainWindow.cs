using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Update;
using ChsbKiosk.Views;

namespace ChsbKiosk;

public partial class MainWindow : Window, IKioskCommandTarget, IShell, IComponentConnector
{
	private const int WmSysCommand = 274;

	private const int ScClose = 61536;

	private const int ScMinimize = 61472;

	private const int ScMaximize = 61488;

	private const int ScMove = 61456;

	private const int ScSize = 61440;

	private static readonly nint HwndTopmost = new IntPtr(-1);

	private const uint SwpNoSize = 1u;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoOwnerZOrder = 512u;

	private const uint SwpShowWindow = 64u;

	private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));

	private static readonly Brush OfflineBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));

	private static readonly Brush OfflineTextBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));

	private static readonly Brush OnlineTextBrush = new SolidColorBrush(Color.FromRgb(92, 111, 138));

	private bool _exitRequested;

	private bool _focusReclaimPending;

	private bool _dialogOpen;

	public AppServices Services { get; }

	public event Action? FocusLost;

	public MainWindow(AppServices services)
	{
		Services = services;
		InitializeComponent();
		base.Title = "Ixtisoslashtirilgan ta'lim muassasalari agentligi test platformasi";
		SplashOrganization.Text = "Ixtisoslashtirilgan ta'lim muassasalari agentligi";
		SplashPlatform.Text = "Test platformasi";
		VersionText.Text = "Ixtisoslashtirilgan ta'lim muassasalari agentligi test platformasi  ·  v2.1.1";
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
#if PRACTICE
		base.WindowState = WindowState.Maximized;
#else
		EnterKioskMode();
#endif
		try
		{
			await StartupAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Ishga tushishda xato");
			ShowSplash("Ilovani ishga tushirib bo'lmadi", "Nazoratchini chaqiring. Tafsilot jurnal faylida.", busy: false);
		}
	}

	private async Task StartupAsync()
	{
		ShowSplash("Serverga ulanmoqda...", null, busy: true);
		BootstrapResponse bootstrap;
		while (true)
		{
			ApiResult<BootstrapResponse> apiResult = await Services.Api.BootstrapAsync();
			if (apiResult.Ok && apiResult.Value != null)
			{
				bootstrap = apiResult.Value;
				SetConnection(online: true);
				Services.Clock.Sync(bootstrap.ServerTime);
				Services.Proctor = bootstrap.Proctor.Sanitized();
				Services.HeartbeatSec = Math.Clamp(bootstrap.HeartbeatSec, 30, 600);
				bool mandatoryUpdate = !bootstrap.Supported;
				bool flag = !string.IsNullOrEmpty(Services.Store.AuthToken);
				if (!mandatoryUpdate && flag)
				{
					break;
				}
				UpdateOutcome updateOutcome = await new AppUpdater(Services).RunAsync(bootstrap.Update, mandatoryUpdate, delegate(string text, string? detail)
				{
					ShowSplash(text, detail, busy: true);
				});
				if (_exitRequested)
				{
					return;
				}
				switch (updateOutcome)
				{
				case UpdateOutcome.InstallerStarted:
					ShowSplash("Yangilanish o'rnatilmoqda...", "Dastur bir necha soniyadan keyin o'zi qayta ochiladi.", busy: true);
					await Task.Delay(TimeSpan.FromSeconds(1.5));
					ExitNow();
					return;
				case UpdateOutcome.Declined:
					if (!mandatoryUpdate)
					{
						ShowSplash("Yangilanish o'rnatilmadi", "Administrator ruxsati berilmadi. Dastur hozirgi versiyada davom etadi; keyingi safar yana so'raladi.", busy: false);
						await Task.Delay(TimeSpan.FromSeconds(5L));
					}
					break;
				}
				break;
			}
			SetConnection(online: false);
			if (apiResult.IsNetworkError)
			{
				ShowSplash("Internet aloqasi yo'q", "Ulanish tiklanishi bilan ilova o'zi davom etadi.", busy: true);
			}
			else
			{
				ShowSplash("Server javob bermayapti", apiResult.ErrorOr("Noma'lum xato") + " — qayta urinilmoqda.", busy: true);
			}
			await Task.Delay(TimeSpan.FromSeconds(4L));
			if (_exitRequested)
			{
				return;
			}
		}
		if (!bootstrap.Supported)
		{
			ShowSplash("Dasturni yangilash kerak", $"O'rnatilgan versiya: {"2.1.1"}. Kerakli: {bootstrap.MinVersion} yoki yangiroq. " + "Avtomatik yangilab bo'lmadi — administrator ruxsati bilan qayta urinib ko'ring (dasturni yopib oching) yoki CHSB saytidagi \"Test dasturi\" bo'limidan yangi o'rnatgichni yuklab o'rnating.", busy: false);
			return;
		}
		string authToken = Services.Store.AuthToken;
		if (!string.IsNullOrEmpty(authToken))
		{
			ShowSplash("Sessiya tiklanmoqda...", Services.Store.StudentName, busy: true);
			Services.Api.ImportAuthToken(authToken);
			for (int attempt = 0; attempt < 5; attempt++)
			{
				ApiResult<TestsResponse> apiResult2 = await Services.Api.GetTestsAsync();
				if (apiResult2.Ok && apiResult2.Value != null)
				{
					HideSplash();
					ShowTests(apiResult2.Value);
					return;
				}
				if (!apiResult2.IsTransient)
				{
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(3L));
			}
			Services.ForgetSession();
		}
		HideSplash();
		ShowLogin();
	}

	private void ShowSplash(string text, string? detail, bool busy)
	{
		SplashText.Text = text;
		SplashDetail.Text = detail ?? "";
		SplashProgress.Visibility = ((!busy) ? Visibility.Hidden : Visibility.Visible);
		Splash.Visibility = Visibility.Visible;
	}

	private void HideSplash()
	{
		Splash.Visibility = Visibility.Collapsed;
	}

	public void ShowLogin(string? message = null)
	{
		SetView(new LoginView(this, message));
	}

	public void ShowTests(TestsResponse? preloaded = null)
	{
		SetView(new TestsView(this, preloaded));
	}

	public void ShowExam(StartResponse start, CameraMode camera)
	{
		SetView(new ExamView(this, start, camera));
	}

	public void ShowSurvey(string surveyId, string? testResultId)
	{
		SetView(new SurveyView(this, surveyId, testResultId));
	}

	public void ShowFinished(string title, string message, string? testResultId = null, bool offerSurvey = true)
	{
		SetView(new FinishedView(this, title, message, testResultId, offerSurvey));
	}

	private void SetView(UIElement view)
	{
		object content = ViewHost.Content;
		ViewHost.Content = view;
		if (content is IDisposable disposable)
		{
			try
			{
				disposable.Dispose();
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Ekranni yopishda xato");
			}
		}
	}

	public void SetConnection(bool online)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SetConnection(online);
			});
		}
		else
		{
			ConnectionDot.Fill = (online ? OnlineBrush : OfflineBrush);
			ConnectionText.Text = (online ? "Serverga ulangan" : "Aloqa yo'q — qayta ulanmoqda");
			ConnectionText.Foreground = (online ? OnlineTextBrush : OfflineTextBrush);
		}
	}

	public bool ConfirmSupervisor(string title, string confirmText)
	{
		if (!Services.Config.RequiresExitPassword)
		{
			return true;
		}
		_dialogOpen = true;
		try
		{
			return ExitPasswordDialog.TryConfirm(this, Services.Config, title, confirmText);
		}
		finally
		{
			_dialogOpen = false;
			ReclaimFocus();
		}
	}

	public void Reload()
	{
		if (ViewHost.Content is IRefreshableView refreshableView)
		{
			refreshableView.Refresh();
		}
	}

	public void RequestExit()
	{
		if (_dialogOpen || _exitRequested)
		{
			return;
		}
		string text = (ViewHost.Content as IExitGuard)?.ExitWarning;
		if (Services.Config.RequiresExitPassword)
		{
			string title = ((text == null) ? "Chiqish paroli" : (text + "\n\nChiqish uchun nazoratchi parolini kiriting"));
			if (!ConfirmSupervisor(title, "Chiqish"))
			{
				return;
			}
		}
		else
		{
			_dialogOpen = true;
			try
			{
				if (MessageBox.Show(this, (text == null) ? "Ilovadan chiqilsinmi?" : (text + "\n\nBaribir chiqilsinmi?"), "Test platformasi", MessageBoxButton.YesNo, (text == null) ? MessageBoxImage.Question : MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
				{
					return;
				}
			}
			finally
			{
				_dialogOpen = false;
			}
		}
		ExitNow();
	}

	private void ExitNow()
	{
		_exitRequested = true;
		SetView(new Grid());
		Application.Current.Shutdown();
	}

	private void EnterKioskMode()
	{
		base.WindowState = WindowState.Normal;
		base.Left = SystemParameters.VirtualScreenLeft;
		base.Top = SystemParameters.VirtualScreenTop;
		base.Width = SystemParameters.VirtualScreenWidth;
		base.Height = SystemParameters.VirtualScreenHeight;
		Root.Margin = new Thickness(0.0 - SystemParameters.VirtualScreenLeft, 0.0 - SystemParameters.VirtualScreenTop, 0.0, 0.0);
		Root.Width = SystemParameters.PrimaryScreenWidth;
		Root.Height = SystemParameters.PrimaryScreenHeight;
		base.Topmost = true;
		Activate();
	}

	private void Window_Deactivated(object sender, EventArgs e)
	{
		if (_exitRequested || _dialogOpen)
		{
			return;
		}
#if PRACTICE
		// Amaliy rejim: fokus yo'qolishini FAQAT serverga xabar qilamiz (monitoring),
		// lekin oynani majburan qaytarmaymiz — talaba erkin ishlay oladi.
		this.FocusLost?.Invoke();
#else
		if (!_focusReclaimPending)
		{
			this.FocusLost?.Invoke();
			_focusReclaimPending = true;
			base.Dispatcher.BeginInvoke(new Action(ReclaimFocus), DispatcherPriority.ApplicationIdle);
		}
#endif
	}

	private void ReclaimFocus()
	{
		_focusReclaimPending = false;
		if (!_exitRequested && !_dialogOpen)
		{
			if (base.WindowState == WindowState.Minimized)
			{
				base.WindowState = WindowState.Normal;
			}
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, 579u);
			}
			base.Topmost = true;
			Activate();
		}
	}

	private void Window_SourceInitialized(object sender, EventArgs e)
	{
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
#if !PRACTICE
		if (msg == 274)
		{
			bool flag;
			switch (((IntPtr)wParam).ToInt32() & 0xFFF0)
			{
			case 61440:
			case 61456:
			case 61472:
			case 61488:
			case 61536:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				handled = true;
				return IntPtr.Zero;
			}
		}
#endif
		return IntPtr.Zero;
	}

	protected override void OnClosing(CancelEventArgs e)
	{
#if !PRACTICE
		if (!_exitRequested)
		{
			e.Cancel = true;
		}
		else
		{
			base.OnClosing(e);
		}
#else
		base.OnClosing(e);
#endif
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
