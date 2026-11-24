#if PRACTICE
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Exam;

namespace ChsbKiosk.Assist;

// Amaliy (practice) qurilishi uchun yashirin yordam. Savol matni VA rasmlarini Claude'ga
// (vision) yuboradi va eng ehtimoliy to'g'ri variant harfini (A/B/C/D) qaytaradi.
// Faqat PRACTICE qurilishida kompilyatsiya qilinadi.
//
// MUHIM: bu javob tasdiqlangan kalit emas, AI taxmini — ba'zan xato bo'lishi mumkin.
public sealed class AnswerAssistant : IDisposable
{
	// Yuklab olingan rasm: baytlar + media turi (image/png ...).
	public delegate Task<(byte[] Bytes, string MediaType)?> ImageFetcher(string url, CancellationToken ct);

	private const string Endpoint = "https://api.anthropic.com/v1/messages";
	private const string AnthropicVersion = "2023-06-01";
	private const int MaxImages = 8;

	private static readonly string[] Keys = { "A", "B", "C", "D" };

	private static readonly Regex ImgSrc = new Regex(
		"<img\\b[^>]*?\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _model;
	private readonly ImageFetcher? _imageFetcher;

	private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
	private readonly object _cacheLock = new object();

	public AnswerAssistant(string apiKey, string model, ImageFetcher? imageFetcher = null)
	{
		_apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
		_model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5-5" : model;
		_imageFetcher = imageFetcher;
		_http = new HttpClient { Timeout = TimeSpan.FromSeconds(45.0) };
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public async Task<string?> PredictAsync(ExamQuestion question, CancellationToken ct = default)
	{
		if (question == null)
		{
			return null;
		}
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(question.Id, out string? cached))
			{
				return cached;
			}
		}

		string? key;
		try
		{
			List<object> content = await BuildContentAsync(question, ct).ConfigureAwait(false);
			key = await RequestAsync(content, ct).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			ChsbKiosk.Logger.Error(exception, "AI yordam: javobni olishda xato");
			return null;
		}

		if (key != null)
		{
			lock (_cacheLock)
			{
				_cache[question.Id] = key;
			}
		}
		return key;
	}

	// Claude 'user' xabari uchun kontent bloklari: matn + (mavjud bo'lsa) rasmlar.
	private async Task<List<object>> BuildContentAsync(ExamQuestion question, CancellationToken ct)
	{
		var content = new List<object> { new { type = "text", text = BuildTextPrompt(question) } };

		if (_imageFetcher == null)
		{
			return content;
		}

		int imageCount = 0;
		foreach ((string label, string url) in CollectImageUrls(question))
		{
			if (imageCount >= MaxImages)
			{
				break;
			}
			(byte[] Bytes, string MediaType)? image;
			try
			{
				image = await _imageFetcher(url, ct).ConfigureAwait(false);
			}
			catch
			{
				image = null;
			}
			if (image == null)
			{
				continue;
			}
			content.Add(new { type = "text", text = "Rasm — " + label });
			content.Add(new
			{
				type = "image",
				source = new
				{
					type = "base64",
					media_type = image.Value.MediaType,
					data = Convert.ToBase64String(image.Value.Bytes)
				}
			});
			imageCount++;
		}
		return content;
	}

	// Savol va variantlardagi <img src="..."> larni (yorliq bilan) to'playdi, takrorlanmaydi.
	private static IEnumerable<(string Label, string Url)> CollectImageUrls(ExamQuestion question)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string Label, string Url) item in ExtractImages("savol", question.TextHtml, seen))
		{
			yield return item;
		}
		foreach (string key in Keys)
		{
			string? html = (question.OptionsHtml != null && question.OptionsHtml.TryGetValue(key, out string? h)) ? h : null;
			foreach ((string Label, string Url) item in ExtractImages(key + " varianti", html, seen))
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<(string Label, string Url)> ExtractImages(string label, string? html, HashSet<string> seen)
	{
		if (string.IsNullOrEmpty(html))
		{
			yield break;
		}
		foreach (Match match in ImgSrc.Matches(html))
		{
			string url = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
			if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || !seen.Add(url))
			{
				continue;
			}
			yield return (label, url);
		}
	}

	private async Task<string?> RequestAsync(List<object> content, CancellationToken ct)
	{
		var body = new
		{
			model = _model,
			max_tokens = 1024,
			system = "You help a student PRACTICE multiple-choice questions. "
				+ "You are given a question and four options labelled A, B, C, D (text may be in Uzbek; images may be attached). "
				+ "Work out the best answer and reply with ONLY the single capital letter A, B, C or D. No words, no punctuation.",
			output_config = new { effort = "low" },
			messages = new[] { new { role = "user", content = content } }
		};

		using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
		request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
		request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
		request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

		using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
		string payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			ChsbKiosk.Logger.Info($"AI yordam: API {(int)response.StatusCode} javob qaytardi");
			return null;
		}
		return ParseLetter(payload);
	}

	private static string? ParseLetter(string payload)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(payload);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("stop_reason", out JsonElement stop)
				&& string.Equals(stop.GetString(), "refusal", StringComparison.Ordinal))
			{
				return null;
			}
			if (!root.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			StringBuilder text = new StringBuilder();
			foreach (JsonElement block in content.EnumerateArray())
			{
				if (block.TryGetProperty("type", out JsonElement type)
					&& string.Equals(type.GetString(), "text", StringComparison.Ordinal)
					&& block.TryGetProperty("text", out JsonElement t))
				{
					text.Append(t.GetString());
				}
			}
			Match match = Regex.Match(text.ToString(), "[ABCD]");
			return match.Success ? match.Value : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string BuildTextPrompt(ExamQuestion question)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("Savol:\n").Append(ToText(question.TextHtml)).Append("\n\n");
		foreach (string key in Keys)
		{
			string value = (question.OptionsHtml != null && question.OptionsHtml.TryGetValue(key, out string? html))
				? ToText(html)
				: "";
			sb.Append(key).Append(") ").Append(value).Append('\n');
		}
		sb.Append("\n(Agar quyida rasmlar biriktirilgan bo'lsa, ular savol yoki variantlarga tegishli.)\n");
		sb.Append("Javob (faqat bitta harf: A, B, C yoki D):");
		return sb.ToString();
	}

	private static string ToText(string? html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return "";
		}
		string s = Regex.Replace(html, "(?is)<(script|style)[^>]*>.*?</\\1>", " ");
		s = Regex.Replace(s, "(?i)<br\\s*/?>", "\n");
		s = Regex.Replace(s, "(?i)</(p|div|li|tr|h[1-6])>", "\n");
		s = Regex.Replace(s, "<[^>]+>", " ");
		s = System.Net.WebUtility.HtmlDecode(s);
		s = Regex.Replace(s, "[ \\t]+", " ");
		s = Regex.Replace(s, "\\s*\\n\\s*", "\n");
		return s.Trim();
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
#endif
