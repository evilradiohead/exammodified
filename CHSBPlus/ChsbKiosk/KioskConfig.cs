using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk;

public sealed class KioskConfig
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	public string HomeUrl { get; set; } = "https://chsb.piima.uz/";

	public string CameraPolicy { get; set; } = "auto";

	public bool AllowInsecureHttp { get; set; }

	public int CameraIndex { get; set; }

	public bool AutoUpdate { get; set; } = true;

	public string ExitPassword { get; set; } = "";

#if PRACTICE
	// Amaliy (practice) qurilishi uchun AI yordam sozlamalari. Rasmiy imtihon qurilishida bu maydonlar yo'q.
	// aiApiKey — Google Gemini API kaliti (aistudio.google.com'dan BEPUL olinadi; config.json ichida).
	public string AiApiKey { get; set; } = "";

	// aiModel — bepul "flash" modeli bu "to'g'ri variantni tanlash" vazifasi uchun yetarli.
	public string AiModel { get; set; } = "gemini-2.0-flash";
#endif

	public string[] AllowedHosts { get; set; } = new string[1] { "chsb.piima.uz" };

	public string[] AllowedSchemesWithoutHost { get; set; } = new string[3] { "about", "data", "blob" };

	public bool RestoreAfterUnexpectedShutdown { get; set; } = true;

	public bool AllowEssentialEditingKeys { get; set; } = true;

	public bool CloseWindowedAppsOnStartup { get; set; } = true;

	public bool KeepProcessGuardRunning { get; set; } = true;

	public int ProcessGuardIntervalMs { get; set; } = 1500;

	public string[] AdditionalAllowedProcessNames { get; set; } = Array.Empty<string>();

	public string[] BlockedProcessNames { get; set; } = new string[29]
	{
		"1password", "anydesk", "brave", "calculator", "calculatorapp", "calc", "chrome", "cmd", "discord", "firefox",
		"iexplore", "msedge", "notepad", "obs64", "opera", "powershell", "powerpnt", "pwsh", "regedit", "screenclip",
		"snippingtool", "snipandsketch", "taskmgr", "teams", "telegram", "teamviewer", "vivaldi", "winword", "wt"
	};

	public string[] BlockedWindowTitleWords { get; set; } = new string[12]
	{
		"calculator", "chatgpt", "chrome", "copilot", "edge", "firefox", "gemini", "notepad", "opera", "telegram",
		"terminal", "word"
	};

	public string DataRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CHSB Plus");

	public string WebViewUserDataFolder => Path.Combine(DataRoot, "WebView2");

	public string StateFile => Path.Combine(DataRoot, "state.bin");

	public string ModelFile => Path.Combine(DataRoot, "models", "yunet.onnx");

	public Uri ApiBaseUri
	{
		get
		{
			if (Uri.TryCreate(HomeUrl, UriKind.Absolute, out Uri result) && ApiClient.IsAllowedBase(result, AllowInsecureHttp))
			{
				return result;
			}
			return new Uri("https://chsb.piima.uz/");
		}
	}

	public bool CameraNeverRequired => string.Equals(CameraPolicy, "never", StringComparison.OrdinalIgnoreCase);

	public bool CameraAlwaysRequired => string.Equals(CameraPolicy, "always", StringComparison.OrdinalIgnoreCase);

	public string LogFile => Path.Combine(DataRoot, "logs", "kiosk.log");

	public bool RequiresExitPassword => !string.IsNullOrWhiteSpace(ExitPassword);

	public bool VerifyExitPassword(string password)
	{
		return string.Equals(password, ExitPassword, StringComparison.Ordinal);
	}

	public static KioskConfig Load()
	{
		KioskConfig kioskConfig = new KioskConfig();
		foreach (string configSearchPath in GetConfigSearchPaths())
		{
			if (!File.Exists(configSearchPath))
			{
				continue;
			}
			try
			{
				KioskConfig kioskConfig2 = JsonSerializer.Deserialize<KioskConfig>(File.ReadAllText(configSearchPath), JsonOptions);
				if (kioskConfig2 != null)
				{
					kioskConfig = kioskConfig2;
					break;
				}
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Could not load config: " + configSearchPath);
			}
		}
		Directory.CreateDirectory(kioskConfig.DataRoot);
		Directory.CreateDirectory(Path.GetDirectoryName(kioskConfig.LogFile));
		return kioskConfig;
	}

	public bool IsAllowedUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return false;
		}
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(uri.Host))
		{
			return AllowedSchemesWithoutHost.Any((string s) => string.Equals(s, uri.Scheme, StringComparison.OrdinalIgnoreCase));
		}
		bool num = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
		bool flag = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && (AllowInsecureHttp || uri.IsLoopback);
		if (!num && !flag)
		{
			return false;
		}
		if (!string.Equals(ApiBaseUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
		{
			return AllowedHosts.Any((string host) => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase));
		}
		return true;
	}

	public IReadOnlySet<string> AllowedProcessNames()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"chsbkiosk", "chsbplus", "chsb +", "applicationframehost", "ctfmon", "dwm", "explorer", "fontdrvhost", "msedgewebview2", "runtimebroker",
			"searchhost", "shellexperiencehost", "sihost", "startmenuexperiencehost", "textinputhost"
		};
		string[] additionalAllowedProcessNames = AdditionalAllowedProcessNames;
		foreach (string processName in additionalAllowedProcessNames)
		{
			hashSet.Add(NormalizeProcessName(processName));
		}
		return hashSet;
	}

	public IReadOnlySet<string> BlockedProcesses()
	{
		return (from name in BlockedProcessNames.Select(NormalizeProcessName)
			where !string.IsNullOrWhiteSpace(name)
			select name).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public static string NormalizeProcessName(string processName)
	{
		if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return processName;
		}
		return processName.Substring(0, processName.Length - 4);
	}

	private static IEnumerable<string> GetConfigSearchPaths()
	{
		yield return Path.Combine(AppContext.BaseDirectory, "config.json");
		yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CHSB Plus", "config.json");
		yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CHSB Plus", "config.json");
	}
}
