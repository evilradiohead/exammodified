using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using ChsbKiosk.Core.Api;
using Microsoft.Win32;

namespace ChsbKiosk.Security;

public static class DeviceIdentity
{
	public static DeviceInfo Create(KioskConfig config)
	{
		string text = ReadMachineGuid() ?? ReadOrCreateLocalId(config);
		InlineArray6<object> buffer = default(InlineArray6<object>);
		buffer[0] = Environment.MachineName;
		buffer[1] = Environment.ProcessorCount;
		buffer[2] = Environment.OSVersion.Version;
		buffer[3] = (int)SystemParameters.PrimaryScreenWidth;
		buffer[4] = (int)SystemParameters.PrimaryScreenHeight;
		buffer[5] = Environment.Is64BitOperatingSystem;
		string text2 = string.Join("|", (ReadOnlySpan<object?>)buffer);
		return new DeviceInfo
		{
			DeviceId = Hash("chsb-plus/device/" + text),
			Fingerprint = Hash("chsb-plus/fp/" + text2),
			OsDescription = $"Windows {Environment.OSVersion.Version}"
		};
	}

	private static string? ReadMachineGuid()
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using RegistryKey registryKey2 = registryKey.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
			string text = registryKey2?.GetValue("MachineGuid") as string;
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "MachineGuid o'qilmadi");
			return null;
		}
	}

	private static string ReadOrCreateLocalId(KioskConfig config)
	{
		string path = Path.Combine(config.DataRoot, "device.id");
		try
		{
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path).Trim();
				if (text.Length >= 16)
				{
					return text;
				}
			}
			string text2 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(config.DataRoot);
			File.WriteAllText(path, text2);
			return text2;
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Qurilma identifikatori saqlanmadi");
			return Environment.MachineName + "/" + Environment.UserName;
		}
	}

	private static string Hash(string value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 16).ToLowerInvariant();
	}
}
