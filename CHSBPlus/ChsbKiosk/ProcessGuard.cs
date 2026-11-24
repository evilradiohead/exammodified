using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChsbKiosk;

public sealed class ProcessGuard : IDisposable
{
	private readonly KioskConfig _config;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly int _currentProcessId = Environment.ProcessId;

	private readonly Dictionary<int, DateTime> _lastCloseAttempts = new Dictionary<int, DateTime>();

	public ProcessGuard(KioskConfig config)
	{
		_config = config;
	}

	public Task StartAsync()
	{
		if (!_config.CloseWindowedAppsOnStartup && !_config.KeepProcessGuardRunning)
		{
			return Task.CompletedTask;
		}
		Task.Run(() => GuardLoopAsync(_cts.Token));
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	private async Task GuardLoopAsync(CancellationToken cancellationToken)
	{
		bool closeWindowedApps = _config.CloseWindowedAppsOnStartup;
		do
		{
			Sweep(closeWindowedApps);
			closeWindowedApps = false;
			if (_config.KeepProcessGuardRunning)
			{
				try
				{
					await Task.Delay(Math.Max(750, _config.ProcessGuardIntervalMs), cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				continue;
			}
			break;
		}
		while (!cancellationToken.IsCancellationRequested);
	}

	private void Sweep(bool closeWindowedApps)
	{
		IReadOnlySet<string> readOnlySet = _config.AllowedProcessNames();
		IReadOnlySet<string> readOnlySet2 = _config.BlockedProcesses();
		DateTime now = DateTime.UtcNow;
		int[] array = (from pair in _lastCloseAttempts
			where now - pair.Value > TimeSpan.FromMinutes(1L)
			select pair.Key).ToArray();
		foreach (int key in array)
		{
			_lastCloseAttempts.Remove(key);
		}
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			using (process)
			{
				try
				{
					if (process.Id == _currentProcessId || process.HasExited)
					{
						continue;
					}
					string item = KioskConfig.NormalizeProcessName(process.ProcessName);
					if (!readOnlySet.Contains(item))
					{
						bool flag = readOnlySet2.Contains(item) || HasBlockedTitle(process);
						if (!flag && closeWindowedApps)
						{
							flag = process.MainWindowHandle != IntPtr.Zero;
						}
						if (flag && (!_lastCloseAttempts.TryGetValue(process.Id, out var value) || !(now - value < TimeSpan.FromSeconds(10L))))
						{
							_lastCloseAttempts[process.Id] = now;
							CloseProcess(process);
						}
					}
				}
				catch
				{
				}
			}
		}
	}

	private bool HasBlockedTitle(Process process)
	{
		string title = process.MainWindowTitle;
		if (string.IsNullOrWhiteSpace(title))
		{
			return false;
		}
		return _config.BlockedWindowTitleWords.Any((string word) => title.Contains(word, StringComparison.OrdinalIgnoreCase));
	}

	private static void CloseProcess(Process process)
	{
		try
		{
			Logger.Info($"Closing process: {process.ProcessName} ({process.Id})");
			if (process.MainWindowHandle != IntPtr.Zero)
			{
				process.CloseMainWindow();
				if (process.WaitForExit(700))
				{
					return;
				}
			}
			process.Kill(entireProcessTree: true);
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Could not close process: " + process.ProcessName);
		}
	}
}
