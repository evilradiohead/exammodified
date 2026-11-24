using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ChsbKiosk.Core.Exam;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChsbKiosk.Rendering;

public sealed class QuestionRenderer
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly WebView2 _view;

	private readonly KioskConfig _config;

	private readonly string _imageOrigin;

	private readonly ChsbKiosk.Core.Api.ApiClient? _api;

	private bool _ready;

	private bool _loadingTemplate;

	private string? _pendingMessage;

	private string? _lastShowMessage;

	private ExamQuestion? _lastQuestion;

#if PRACTICE
	private ChsbKiosk.Assist.AnswerAssistant? _assistant;

	private bool _assistBusy;
#endif

	private static readonly Lazy<string> FontFaces = new Lazy<string>(BuildFontFaces);

	public event Action<string>? OptionSelected;

	public QuestionRenderer(WebView2 view, KioskConfig config, ChsbKiosk.Core.Api.ApiClient? api = null)
	{
		_view = view;
		_config = config;
		_api = api;
		_imageOrigin = config.ApiBaseUri.GetLeftPart(UriPartial.Authority);
	}

	public async Task InitializeAsync()
	{
		CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, _config.WebViewUserDataFolder);
		await _view.EnsureCoreWebView2Async(environment);
		CoreWebView2 coreWebView = _view.CoreWebView2;
		CoreWebView2Settings settings = coreWebView.Settings;
		settings.AreBrowserAcceleratorKeysEnabled = false;
		settings.AreDefaultContextMenusEnabled = false;
		settings.AreDevToolsEnabled = false;
		settings.AreHostObjectsAllowed = false;
		settings.AreDefaultScriptDialogsEnabled = false;
		settings.IsGeneralAutofillEnabled = false;
		settings.IsPasswordAutosaveEnabled = false;
		settings.IsStatusBarEnabled = false;
		settings.IsZoomControlEnabled = false;
		settings.IsPinchZoomEnabled = false;
		settings.IsSwipeNavigationEnabled = false;
		settings.IsBuiltInErrorPageEnabled = false;
		settings.IsWebMessageEnabled = true;
		coreWebView.NavigationStarting += OnNavigationStarting;
		coreWebView.NewWindowRequested += delegate(object? _, CoreWebView2NewWindowRequestedEventArgs e)
		{
			e.Handled = true;
		};
		coreWebView.PermissionRequested += delegate(object? _, CoreWebView2PermissionRequestedEventArgs e)
		{
			e.State = CoreWebView2PermissionState.Deny;
		};
		coreWebView.DownloadStarting += delegate(object? _, CoreWebView2DownloadStartingEventArgs e)
		{
			e.Cancel = true;
		};
		coreWebView.WebMessageReceived += OnWebMessageReceived;
		coreWebView.ProcessFailed += OnProcessFailed;
		coreWebView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
		coreWebView.WebResourceRequested += OnWebResourceRequested;
		LoadTemplate();
	}

	public void Show(ExamQuestion question, string? selected, bool enabled)
	{
		_lastQuestion = question;
		Post(_lastShowMessage = JsonSerializer.Serialize(new
		{
			type = "show",
			text = question.TextHtml,
			options = question.OptionsHtml,
			selected = selected,
			enabled = enabled
		}, Json));
	}

	public void SetEnabled(bool enabled)
	{
		Post(JsonSerializer.Serialize(new
		{
			type = "enabled",
			value = enabled
		}, Json));
	}

#if PRACTICE
	// To'g'ri variantni (A/B/C/D) savol sahifasida qalin qilib belgilaydi. Faqat amaliy qurilishda.
	public void Reveal(string key)
	{
		Post(JsonSerializer.Serialize(new
		{
			type = "reveal",
			key = key
		}, Json));
	}

	private async void OnAssistRequested()
	{
		if (_assistBusy)
		{
			return;
		}
		ExamQuestion? question = _lastQuestion;
		if (question == null)
		{
			return;
		}
		string apiKey = _config.AiApiKey;
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			Logger.Info("AI yordam: API kalit sozlanmagan (config.json: aiApiKey)");
			return;
		}
		_assistBusy = true;
		try
		{
			if (_assistant == null)
			{
				ChsbKiosk.Assist.AnswerAssistant.ImageFetcher? fetcher = null;
				if (_api != null)
				{
					fetcher = (url, ct) => _api!.FetchImageAsync(url, ct);
				}
				_assistant = new ChsbKiosk.Assist.AnswerAssistant(apiKey, _config.AiModel, fetcher);
			}
			string? key = await _assistant.PredictAsync(question).ConfigureAwait(true);
			if (key != null && ReferenceEquals(_lastQuestion, question))
			{
				Reveal(key);
			}
		}
		finally
		{
			_assistBusy = false;
		}
	}
#endif

	private void Post(string json)
	{
		if (!_ready)
		{
			_pendingMessage = json;
			return;
		}
		try
		{
			_view.CoreWebView2.PostWebMessageAsJson(json);
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Savolni ko'rsatib bo'lmadi");
		}
	}

	private void LoadTemplate()
	{
		_ready = false;
		_loadingTemplate = true;
		string newValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		string htmlContent = ReadTemplate().Replace("/*{{FONT_FACES}}*/", FontFaces.Value, StringComparison.Ordinal).Replace("{{NONCE}}", newValue, StringComparison.Ordinal).Replace("{{IMG_ORIGIN}}", _imageOrigin, StringComparison.Ordinal);
		_view.CoreWebView2.NavigateToString(htmlContent);
	}

	private static string BuildFontFaces()
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			(string, int)[] array = new(string, int)[3]
			{
				("font-regular.ttf", 400),
				("font-semibold.ttf", 600),
				("font-bold.ttf", 700)
			};
			for (int i = 0; i < array.Length; i++)
			{
				var (name, value) = array[i];
				using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
				if (stream != null)
				{
					using MemoryStream memoryStream = new MemoryStream();
					stream.CopyTo(memoryStream);
					stringBuilder.Append("@font-face{font-family:\"Onest\";font-style:normal;font-weight:").Append(value).Append(";src:url(data:font/ttf;base64,")
						.Append(Convert.ToBase64String(memoryStream.ToArray()))
						.Append(") format(\"truetype\");}");
				}
			}
			return stringBuilder.ToString();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Shriftni savol sahifasiga qo'shib bo'lmadi");
			return "";
		}
	}

	private static string ReadTemplate()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("question.html") ?? throw new InvalidOperationException("Savol shabloni ilova ichida topilmadi");
		using StreamReader streamReader = new StreamReader(stream);
		return streamReader.ReadToEnd();
	}

	private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (_loadingTemplate)
		{
			_loadingTemplate = false;
			return;
		}
		e.Cancel = true;
		Logger.Info("Bloklangan navigatsiya: " + Shorten(e.Uri));
	}

	private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
	{
		if (!IsAllowedResource(e.Request.Uri, e.ResourceContext, e.Request.Method))
		{
			Logger.Info("Bloklangan resurs: " + Shorten(e.Request.Uri));
			e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
		}
	}

	private bool IsAllowedResource(string uriText, CoreWebView2WebResourceContext context, string method)
	{
		if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		bool flag;
		switch (result.Scheme)
		{
		case "data":
		case "about":
		case "blob":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return true;
		}
		if (context == CoreWebView2WebResourceContext.Image && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
		{
			return _config.IsAllowedUrl(uriText);
		}
		return false;
	}

	private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		string text;
		try
		{
			text = e.TryGetWebMessageAsString();
		}
		catch (Exception)
		{
			return;
		}
		if (string.IsNullOrEmpty(text) || text.Length > 256)
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("type", out var value))
			{
				return;
			}
			string text2 = value.GetString();
			if (!(text2 == "ready"))
			{
				if (text2 == "select")
				{
					JsonElement value2;
					string text3 = (rootElement.TryGetProperty("key", out value2) ? ExamSession.NormalizeAnswer(value2.GetString()) : null);
					if (text3 != null)
					{
						this.OptionSelected?.Invoke(text3);
					}
				}
#if PRACTICE
				else if (text2 == "assist")
				{
					OnAssistRequested();
				}
#endif
			}
			else
			{
				_ready = true;
				string text4 = _pendingMessage ?? _lastShowMessage;
				_pendingMessage = null;
				if (text4 != null)
				{
					Post(text4);
				}
			}
		}
		catch (JsonException)
		{
		}
	}

	private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
	{
		Logger.Info($"Savol chizuvchi jarayon to'xtadi: {e.ProcessFailedKind}");
		_view.Dispatcher.BeginInvoke((Func<Task>)async delegate
		{
			await Task.Delay(700);
			try
			{
				LoadTemplate();
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Savol chizuvchini tiklab bo'lmadi");
			}
		});
	}

	private static string Shorten(string value)
	{
		if (value.Length > 120)
		{
			return value.Substring(0, 120) + "...";
		}
		return value;
	}
}
