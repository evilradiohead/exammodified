#if PRACTICE
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Assist;
using ChsbKiosk.Core.Exam;

namespace ChsbKiosk.Assist;

// Amaliy (practice) qurilishi uchun yashirin yordam. Sof mantiq (JSON tuzish, javobni o'qish,
// HTML->matn, rasm URL'lari) ChsbKiosk.Core.Assist.AnswerFormat ichida (testlanadigan). Bu sinf
// faqat tarmoq (HTTP), kesh va rasm yuklashni boshqaradi. Faqat PRACTICE qurilishida kompilyatsiya qilinadi.
//
// MUHIM: javob tasdiqlangan kalit emas, AI taxmini — ba'zan xato bo'lishi mumkin.
public sealed class AnswerAssistant : IDisposable
{
	// Yuklab olingan rasm: baytlar + media turi (image/png ...).
	public delegate Task<(byte[] Bytes, string MediaType)?> ImageFetcher(string url, CancellationToken ct);

	private const string Endpoint = "https://api.anthropic.com/v1/messages";
	private const string AnthropicVersion = "2023-06-01";

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
			List<(string Label, string MediaType, string Base64)> images = await FetchImagesAsync(question, ct).ConfigureAwait(false);
			string json = AnswerFormat.BuildRequestJson(_model, AnswerFormat.BuildTextPrompt(question), images);
			key = await RequestAsync(json, ct).ConfigureAwait(false);
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

	// Savol/variantlardagi rasmlarni (autentifikatsiyalangan fetcher orqali) yuklab, base64'ga aylantiradi.
	private async Task<List<(string Label, string MediaType, string Base64)>> FetchImagesAsync(ExamQuestion question, CancellationToken ct)
	{
		var images = new List<(string, string, string)>();
		if (_imageFetcher == null)
		{
			return images;
		}
		foreach ((string label, string url) in AnswerFormat.CollectImageUrls(question))
		{
			if (images.Count >= AnswerFormat.MaxImages)
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
			images.Add((label, image.Value.MediaType, Convert.ToBase64String(image.Value.Bytes)));
		}
		return images;
	}

	private async Task<string?> RequestAsync(string json, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
		request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
		request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
		request.Content = new StringContent(json, Encoding.UTF8, "application/json");

		using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
		string payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			ChsbKiosk.Logger.Info($"AI yordam: API {(int)response.StatusCode} javob qaytardi");
			return null;
		}
		return AnswerFormat.ParseAnswerLetter(payload);
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
#endif
