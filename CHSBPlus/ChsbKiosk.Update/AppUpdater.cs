using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Update;

namespace ChsbKiosk.Update;

public sealed class AppUpdater
{
	private const int MaxDownloadAttempts = 4;

	private const int MaxInstallAttemptsPerVersion = 3;

	private readonly AppServices _services;

	private readonly string _directory;

	private static string Arch
	{
		get
		{
			if (!Environment.Is64BitProcess)
			{
				return "x86";
			}
			return "x64";
		}
	}

	public AppUpdater(AppServices services)
	{
		_services = services;
		_directory = Path.Combine(services.Config.DataRoot, "updates");
	}

	public async Task<UpdateOutcome> RunAsync(UpdateInfo? info, bool mandatory, Action<string, string?> status)
	{
		_ = 2;
		try
		{
			if (info == null || !UpdateVerifier.IsNewer(info.Version, "2.1.1"))
			{
				Cleanup();
				return UpdateOutcome.NotNeeded;
			}
			if (!_services.Config.AutoUpdate && !mandatory)
			{
				Logger.Info("Yangi versiya bor (" + info.Version + "), lekin avtomatik yangilanish sozlamada o'chirilgan.");
				return UpdateOutcome.Skipped;
			}
			UpdatePackage package = UpdateVerifier.Select(info, Arch);
			if (package == null)
			{
				Logger.Info($"Yangi versiya {info.Version} uchun {Arch} paketi topilmadi.");
				return UpdateOutcome.Skipped;
			}
			if (!UpdateVerifier.VerifySignature(info.Version, package))
			{
				Logger.Info("Yangilanish rad etildi: " + info.Version + " manifest imzosi noto'g'ri.");
				return UpdateOutcome.Failed;
			}
			int attempts = ReadAttempts(info.Version);
			if (attempts >= 3 && !mandatory)
			{
				Logger.Info($"Yangilanish {info.Version} {attempts} marta urinildi va o'rnatilmadi — avtomatik urinish to'xtatildi.");
				return UpdateOutcome.Skipped;
			}
			Directory.CreateDirectory(_directory);
			string target = Path.Combine(_directory, package.File);
			bool flag = File.Exists(target);
			if (flag)
			{
				flag = await UpdateVerifier.VerifyFileAsync(target, info.Version, package);
			}
			if (!flag)
			{
				TryDelete(target);
				if (!(await DownloadAsync(info, package, target, status)))
				{
					return UpdateOutcome.Failed;
				}
				status("Yangilanish tekshirilmoqda (" + info.Version + ")...", "Fayl imzosi va butunligi tekshirilmoqda.");
				if (!(await UpdateVerifier.VerifyFileAsync(target, info.Version, package)))
				{
					Logger.Info("Yangilanish rad etildi: " + package.File + " tekshiruvdan o'tmadi (hajm, SHA-256 yoki imzo).");
					TryDelete(target);
					return UpdateOutcome.Failed;
				}
			}
			WriteAttempts(info.Version, attempts + 1);
			status("Yangilanish o'rnatilmoqda (" + info.Version + ")...", "Windows ruxsat so'rasa — \"Да / Yes\" ni bosing. Dastur o'zi qayta ochiladi.");
			try
			{
				using Process process = Process.Start(new ProcessStartInfo(target, "/update")
				{
					UseShellExecute = true,
					Verb = "runas",
					WorkingDirectory = _directory
				});
				if (process == null)
				{
					return UpdateOutcome.Failed;
				}
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				Logger.Info("Yangilanish o'rnatilmadi: administrator ruxsati berilmadi.");
				return UpdateOutcome.Declined;
			}
			Logger.Info($"Yangilanish o'rnatgichi ishga tushdi: {"2.1.1"} -> {info.Version} ({package.File}).");
			return UpdateOutcome.InstallerStarted;
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Yangilanishda xato");
			return UpdateOutcome.Failed;
		}
	}

	private async Task<bool> DownloadAsync(UpdateInfo info, UpdatePackage package, string target, Action<string, string?> status)
	{
		long totalMb = Math.Max(1L, package.Size / 1048576);
		string title = "Yangilanish yuklanmoqda (" + info.Version + ")...";
		long lastShownMb = -1L;
		Progress<long> progress = new Progress<long>(delegate(long bytes)
		{
			long num = bytes / 1048576;
			if (num != lastShownMb)
			{
				lastShownMb = num;
				status(title, $"{num} / {totalMb} MB  ·  Kompyuterni o'chirmang.");
			}
		});
		for (int attempt = 1; attempt <= 4; attempt++)
		{
			status(title, (attempt == 1) ? $"0 / {totalMb} MB" : $"Qayta urinilmoqda ({attempt}/{4})...");
			ApiResult<Empty> apiResult = await _services.Api.DownloadAsync(package.File, target, package.Size, progress);
			if (apiResult.Ok)
			{
				return true;
			}
			Logger.Info($"Yangilanishni yuklab bo'lmadi (urinish {attempt}): {apiResult.Status} {apiResult.Error}");
			if (!apiResult.IsTransient)
			{
				return false;
			}
			await Task.Delay(TimeSpan.FromSeconds((apiResult.Status == 503) ? 20 : 6));
		}
		return false;
	}

	private string AttemptsFile(string version)
	{
		return Path.Combine(_directory, "attempt-" + version + ".txt");
	}

	private int ReadAttempts(string version)
	{
		try
		{
			string path = AttemptsFile(version);
			int result;
			return (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out result)) ? Math.Clamp(result, 0, 1000) : 0;
		}
		catch (Exception)
		{
			return 0;
		}
	}

	private void WriteAttempts(string version, int count)
	{
		try
		{
			Directory.CreateDirectory(_directory);
			File.WriteAllText(AttemptsFile(version), count.ToString());
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Yangilanish urinishlari hisobini yozib bo'lmadi");
		}
	}

	private void Cleanup()
	{
		try
		{
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, recursive: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception)
		{
		}
	}
}
