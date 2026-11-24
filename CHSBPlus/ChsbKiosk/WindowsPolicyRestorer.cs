using Microsoft.Win32;

namespace ChsbKiosk;

internal static class WindowsPolicyRestorer
{
	public static void Restore()
	{
		string[] array = new string[11]
		{
			"NoRun", "NoClose", "NoLogoff", "StartMenuLogOff", "NoControlPanel", "NoWinKeys", "NoViewContextMenu", "NoTrayContextMenu", "NoSetTaskbar", "NoFind",
			"NoRecentDocsHistory"
		};
		foreach (string name in array)
		{
			TrySetDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer", name, 0);
		}
		array = new string[5] { "DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword", "DisableRegistryTools", "HideFastUserSwitching" };
		foreach (string name2 in array)
		{
			TrySetDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", name2, 0);
		}
		TrySetDword(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\System", "DisableCMD", 0);
		TryDeleteValue(Registry.CurrentUser, "Software\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon", "Shell");
		TryDeleteValue(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", "CHSB +");
		TryDeleteValue(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", "CHSBPlus");
		TryDeleteValue(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Run", "ChsbKiosk");
	}

	private static void TrySetDword(RegistryKey root, string subKey, string name, int value)
	{
		try
		{
			using RegistryKey registryKey = root.CreateSubKey(subKey);
			registryKey?.SetValue(name, value, RegistryValueKind.DWord);
		}
		catch
		{
		}
	}

	private static void TryDeleteValue(RegistryKey root, string subKey, string name)
	{
		try
		{
			using RegistryKey registryKey = root.OpenSubKey(subKey, writable: true);
			registryKey?.DeleteValue(name, throwOnMissingValue: false);
		}
		catch
		{
		}
	}
}
