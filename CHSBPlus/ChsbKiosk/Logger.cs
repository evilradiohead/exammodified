using System;
using System.IO;

namespace ChsbKiosk;

public static class Logger
{
	private static readonly object Sync = new object();

	public static void Info(string message)
	{
		Write("INFO", message);
	}

	public static void Error(Exception exception, string message)
	{
		Write("ERROR", $"{message}: {exception}");
	}

	private static void Write(string level, string message)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChsbKiosk", "logs");
			Directory.CreateDirectory(text);
			string contents = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
			lock (Sync)
			{
				File.AppendAllText(Path.Combine(text, "kiosk.log"), contents);
			}
		}
		catch
		{
		}
	}
}
