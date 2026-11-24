using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ChsbKiosk;

public sealed class KeyboardBlocker : IDisposable
{
	private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	private readonly struct KbdLlHookStruct
	{
		public readonly int VkCode;

		public readonly int ScanCode;

		public readonly int Flags;

		public readonly int Time;

		public readonly nint DwExtraInfo;
	}

	private const int WhKeyboardLl = 13;

	private const int WmKeyDown = 256;

	private const int WmKeyUp = 257;

	private const int WmSysKeyDown = 260;

	private const int WmSysKeyUp = 261;

	private const int VkBack = 8;

	private const int VkTab = 9;

	private const int VkReturn = 13;

	private const int VkShift = 16;

	private const int VkControl = 17;

	private const int VkMenu = 18;

	private const int VkCapital = 20;

	private const int VkSpace = 32;

	private const int VkLeft = 37;

	private const int VkUp = 38;

	private const int VkRight = 39;

	private const int VkDown = 40;

	private const int VkDelete = 46;

	private const int Vk0 = 48;

	private const int Vk9 = 57;

	private const int VkA = 65;

	private const int VkQ = 81;

	private const int VkZ = 90;

	private const int VkLWin = 91;

	private const int VkRWin = 92;

	private const int VkNumpad0 = 96;

	private const int VkNumpad9 = 105;

	private const int VkMultiply = 106;

	private const int VkDivide = 111;

	private const int VkF5 = 116;

	private const int VkLShift = 160;

	private const int VkRShift = 161;

	private const int VkLControl = 162;

	private const int VkRControl = 163;

	private const int VkLMenu = 164;

	private const int VkRMenu = 165;

	private const int VkOem1 = 186;

	private const int VkOem8 = 223;

	private const int VkOem102 = 226;

	private const int VkPacket = 231;

	private readonly IKioskCommandTarget _target;

	private readonly Dispatcher _dispatcher;

	private readonly bool _allowEssentialEditingKeys;

	private readonly LowLevelKeyboardProc _proc;

	private nint _hookId;

	public KeyboardBlocker(IKioskCommandTarget target, Dispatcher dispatcher, bool allowEssentialEditingKeys)
	{
		_target = target;
		_dispatcher = dispatcher;
		_allowEssentialEditingKeys = allowEssentialEditingKeys;
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
			_hookId = SetWindowsHookEx(13, _proc, GetModuleHandle(mainModule?.ModuleName), 0u);
			if (_hookId == IntPtr.Zero)
			{
				Logger.Info("Keyboard hook was not installed.");
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
		bool flag = ((num == 256 || num == 260) ? true : false);
		bool flag2 = flag;
		flag = ((num == 257 || num == 261) ? true : false);
		bool flag3 = flag;
		if (!flag2 && !flag3)
		{
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}
		if (!ShouldBlock(Marshal.PtrToStructure<KbdLlHookStruct>(lParam).VkCode, flag2))
		{
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}
		return new IntPtr(1);
	}

	private bool ShouldBlock(int vk, bool isKeyDown)
	{
		bool flag = IsAnyDown(17, 162, 163);
		bool flag2 = IsAnyDown(18, 164, 165);
		bool flag3 = IsAnyDown(16, 160, 161);
		bool flag4 = IsAnyDown(91, 92);
		if (!flag4)
		{
			bool flag5 = (uint)(vk - 91) <= 1u;
			flag4 = flag5;
		}
		bool flag6 = flag4;
		if ((uint)(vk - 91) <= 1u)
		{
			return true;
		}
		if (IsModifier(vk))
		{
			return false;
		}
		if (isKeyDown)
		{
			if (flag && !flag2 && !flag3 && vk == 81)
			{
				_dispatcher.BeginInvoke(new Action(_target.RequestExit));
				return true;
			}
			if (!flag && !flag2 && !flag3 && vk == 116)
			{
				_dispatcher.BeginInvoke(new Action(_target.Reload));
				return true;
			}
			if (flag && !flag2 && vk == 116)
			{
				_dispatcher.BeginInvoke(new Action(_target.Reload));
				return true;
			}
		}
		if (flag6 || flag || flag2)
		{
			return true;
		}
		if (IsTextInputKey(vk))
		{
			return false;
		}
		if (vk == 20)
		{
			return false;
		}
		if (_allowEssentialEditingKeys && IsEssentialEditingKey(vk))
		{
			return false;
		}
		return true;
	}

	private static bool IsAnyDown(params int[] keys)
	{
		for (int i = 0; i < keys.Length; i++)
		{
			if ((GetAsyncKeyState(keys[i]) & 0x8000) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsModifier(int vk)
	{
		if ((uint)(vk - 16) <= 2u || (uint)(vk - 160) <= 5u)
		{
			return true;
		}
		return false;
	}

	private static bool IsTextInputKey(int vk)
	{
		bool flag = (vk >= 65 && vk <= 90) || (vk >= 48 && vk <= 57) || (vk >= 96 && vk <= 105) || (vk >= 106 && vk <= 111) || (vk >= 186 && vk <= 223);
		if (!flag)
		{
			bool flag2 = ((vk == 32 || vk == 226 || vk == 231) ? true : false);
			flag = flag2;
		}
		return flag;
	}

	private static bool IsEssentialEditingKey(int vk)
	{
		switch (vk)
		{
		case 8:
		case 9:
		case 13:
		case 37:
		case 38:
		case 39:
		case 40:
		case 46:
			return true;
		default:
			return false;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern nint GetModuleHandle(string? lpModuleName);
}
