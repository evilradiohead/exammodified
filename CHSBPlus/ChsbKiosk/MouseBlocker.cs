using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChsbKiosk;

public sealed class MouseBlocker : IDisposable
{
	private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

	private const int WhMouseLl = 14;

	private const int WmRButtonDown = 516;

	private const int WmRButtonUp = 517;

	private const int WmRButtonDblClk = 518;

	private const int WmMButtonDown = 519;

	private const int WmMButtonUp = 520;

	private const int WmMButtonDblClk = 521;

	private const int WmXButtonDown = 523;

	private const int WmXButtonUp = 524;

	private const int WmXButtonDblClk = 525;

	private readonly LowLevelMouseProc _proc;

	private nint _hookId;

	public MouseBlocker()
	{
		_proc = HookCallback;
	}

	public void Install()
	{
		if (_hookId != IntPtr.Zero)
		{
			return;
		}
		using Process process = Process.GetCurrentProcess();
		ProcessModule mainModule = process.MainModule;
		try
		{
			_hookId = SetWindowsHookEx(14, _proc, GetModuleHandle(mainModule?.ModuleName), 0u);
			if (_hookId == IntPtr.Zero)
			{
				Logger.Info("Mouse hook was not installed.");
			}
		}
		finally
		{
			((IDisposable)mainModule)?.Dispose();
		}
	}

	public void Dispose()
	{
		if (_hookId != IntPtr.Zero)
		{
			UnhookWindowsHookEx(_hookId);
			_hookId = IntPtr.Zero;
		}
	}

	private nint HookCallback(int nCode, nint wParam, nint lParam)
	{
		if (nCode < 0)
		{
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}
		int num = ((IntPtr)wParam).ToInt32();
		if (((uint)(num - 516) > 5u && (uint)(num - 523) > 2u) || 1 == 0)
		{
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}
		return new IntPtr(1);
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint GetModuleHandle(string? lpModuleName);
}
