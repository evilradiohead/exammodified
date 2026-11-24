using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ChsbKiosk;

public partial class App : Application
{
	private Mutex? _singleInstance;

	private KeyboardBlocker? _keyboardBlocker;

	private MouseBlocker? _mouseBlocker;

	private ProcessGuard? _processGuard;

	private AppServices? _services;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		base.DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		WindowsPolicyRestorer.Restore();
		_singleInstance = new Mutex(initiallyOwned: true, "Local\\CHSBPlusExamBrowser", out var createdNew);
		if (!createdNew)
		{
			Shutdown();
			return;
		}
		EnsureWebView2Runtime();
		KioskConfig kioskConfig = KioskConfig.Load();
		_services = AppServices.Create(kioskConfig);
		MainWindow mainWindow = (MainWindow)(base.MainWindow = new MainWindow(_services));
#if !PRACTICE
		_keyboardBlocker = new KeyboardBlocker(mainWindow, base.Dispatcher, kioskConfig.AllowEssentialEditingKeys);
		_mouseBlocker = new MouseBlocker();
		_keyboardBlocker.Install();
		_mouseBlocker.Install();
#endif
		mainWindow.Show();
#if !PRACTICE
		_processGuard = new ProcessGuard(kioskConfig);
		_processGuard.StartAsync();
#endif
	}

	// WebView2 runtime o'rnatilmagan bo'lsa, yoniga qo'yilgan bootstrapper'ni jim ishga tushiradi.
	// Xavfsiz: har qanday xato yutiladi — eng yomoni, hozirgidek "runtime yo'q" xabari ko'rinadi.
	private static void EnsureWebView2Runtime()
	{
		try
		{
			string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
			if (!string.IsNullOrEmpty(version))
			{
				return;
			}
		}
		catch
		{
			// o'rnatilmagan — pastda o'rnatishga harakat qilamiz
		}
		try
		{
			string setup = Path.Combine(AppContext.BaseDirectory, "MicrosoftEdgeWebView2Setup.exe");
			if (!File.Exists(setup))
			{
				return;
			}
			using Process? process = Process.Start(new ProcessStartInfo(setup, "/silent /install")
			{
				UseShellExecute = true
			});
			process?.WaitForExit(180000);
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "WebView2 runtime'ni o'rnatib bo'lmadi");
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_processGuard?.Dispose();
		_keyboardBlocker?.Dispose();
		_mouseBlocker?.Dispose();
		_services?.Dispose();
		WindowsPolicyRestorer.Restore();
		try
		{
			_singleInstance?.ReleaseMutex();
		}
		catch (ApplicationException)
		{
		}
		_singleInstance?.Dispose();
		base.OnExit(e);
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception exception)
		{
			Logger.Error(exception, "Unhandled exception");
		}
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		Logger.Error(e.Exception, "Dispatcher exception");
		e.Handled = true;
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		Logger.Error(e.Exception, "Unobserved task exception");
		e.SetObserved();
	}
}
