using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Update;

namespace ChsbKiosk.Core.Api;

public sealed class ApiClient : IDisposable
{
	private sealed record SaveAnswerBody(string TestResultId, string QuestionId, string? SelectedAnswer, int TimeSpentSec);

	public const string AuthCookieName = "student-auth-token";

	private const long MaxResponseBytes = 100663296L;

	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25L);

	private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(120L);

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	private readonly Uri _baseUri;

	private readonly CookieContainer _cookies = new CookieContainer();

	private readonly HttpClient _http;

	private static readonly JsonSerializerOptions JsonWithNulls = new JsonSerializerOptions(Json)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	};

	public Uri BaseUri => _baseUri;

	public ApiClient(Uri baseUri, DeviceInfo device, string appVersion, bool allowInsecureHttp = false)
	{
		if (!IsAllowedBase(baseUri, allowInsecureHttp))
		{
			throw new ArgumentException("Server manzili HTTPS bo'lishi shart", "baseUri");
		}
		_baseUri = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/");
		SocketsHttpHandler handler = new SocketsHttpHandler
		{
			CookieContainer = _cookies,
			UseCookies = true,
			AllowAutoRedirect = false,
			AutomaticDecompression = DecompressionMethods.All,
			ConnectTimeout = TimeSpan.FromSeconds(12L),
			PooledConnectionLifetime = TimeSpan.FromMinutes(10L),
			PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2L),
			MaxConnectionsPerServer = 4
		};
		_http = new HttpClient(handler, disposeHandler: true)
		{
			BaseAddress = _baseUri,
			Timeout = Timeout.InfiniteTimeSpan
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("CHSBPlus/" + appVersion);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("(" + Sanitize(device.OsDescription) + "; kiosk)");
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("x-kiosk-version", appVersion);
		if (IsCleanId(device.DeviceId))
		{
			_http.DefaultRequestHeaders.TryAddWithoutValidation("x-device-id", device.DeviceId);
		}
		if (IsCleanId(device.Fingerprint))
		{
			_http.DefaultRequestHeaders.TryAddWithoutValidation("x-device-fp", device.Fingerprint);
		}
	}

	public static bool IsAllowedBase(Uri uri, bool allowInsecureHttp = false)
	{
		if (!uri.IsAbsoluteUri || string.IsNullOrEmpty(uri.Host))
		{
			return false;
		}
		if (uri.Scheme == Uri.UriSchemeHttps)
		{
			return true;
		}
		if (uri.Scheme == Uri.UriSchemeHttp)
		{
			return uri.IsLoopback || allowInsecureHttp;
		}
		return false;
	}

	public string? ExportAuthToken()
	{
		Cookie cookie = _cookies.GetCookies(_baseUri)["student-auth-token"];
		if (cookie != null && !cookie.Expired && !string.IsNullOrEmpty(cookie.Value))
		{
			return cookie.Value;
		}
		return null;
	}

	public void ImportAuthToken(string token)
	{
		if (!string.IsNullOrWhiteSpace(token) && token.Length <= 4096 && !token.Any(delegate(char c)
		{
			switch (c)
			{
			case '\n':
			case '\r':
			case ' ':
			case ',':
			case ';':
				return true;
			default:
				return false;
			}
		}))
		{
			_cookies.Add(_baseUri, new Cookie("student-auth-token", token, "/")
			{
				HttpOnly = true,
				Secure = (_baseUri.Scheme == Uri.UriSchemeHttps)
			});
		}
	}

	public void ClearSession()
	{
		foreach (Cookie allCookie in _cookies.GetAllCookies())
		{
			allCookie.Expired = true;
		}
	}

	public Task<ApiResult<BootstrapResponse>> BootstrapAsync(CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<BootstrapResponse>(HttpMethod.Get, "api/kiosk/v1/bootstrap", null, DefaultTimeout, ct);
	}

	public Task<ApiResult<LoginResponse>> LoginAsync(string username, string password, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<LoginResponse>(HttpMethod.Post, "api/student/auth/login", new { username, password }, DefaultTimeout, ct);
	}

	public Task<ApiResult<Empty>> LogoutAsync(CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/auth/logout", new { }, DefaultTimeout, ct);
	}

	public Task<ApiResult<Empty>> RenewSessionAsync(CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/auth/renew", new { }, DefaultTimeout, ct);
	}

	public Task<ApiResult<TestsResponse>> GetTestsAsync(CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<TestsResponse>(HttpMethod.Get, "api/kiosk/v1/tests", null, DefaultTimeout, ct);
	}

	public Task<ApiResult<Empty>> VerifyTestPinAsync(string testId, string pinCode, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/tests/verify-pin", new { testId, pinCode }, DefaultTimeout, ct);
	}

	public Task<ApiResult<StartResponse>> StartTestAsync(string testId, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<StartResponse>(HttpMethod.Post, "api/student/tests/start", new { testId }, LongTimeout, ct);
	}

	public Task<ApiResult<Empty>> SaveAnswerAsync(string testResultId, AnswerInput answer, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/tests/save-answer", new SaveAnswerBody(testResultId, answer.QuestionId, answer.SelectedAnswer, answer.TimeSpentSec), DefaultTimeout, ct, writeNulls: true);
	}

	public Task<ApiResult<BatchSaveResponse>> SaveAnswersBatchAsync(string testResultId, IReadOnlyList<AnswerInput> answers, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<BatchSaveResponse>(HttpMethod.Post, "api/student/tests/save-answers-batch", new { testResultId, answers }, TimeSpan.FromSeconds(45L), ct, writeNulls: true);
	}

	public Task<ApiResult<SubmitResponse>> SubmitTestAsync(string testResultId, string completionType, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<SubmitResponse>(HttpMethod.Post, "api/student/tests/submit", new { testResultId, completionType }, LongTimeout, ct);
	}

	public Task<ApiResult<CheatResponse>> ReportViolationAsync(string testResultId, string type, string? detail, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<CheatResponse>(HttpMethod.Post, "api/student/tests/cheat", new
		{
			testResultId = testResultId,
			type = type,
			reason = Truncate(detail, 300)
		}, DefaultTimeout, ct);
	}

	public Task<ApiResult<CheatVerifyResponse>> VerifyUnlockPinAsync(string testResultId, string pinCode, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<CheatVerifyResponse>(HttpMethod.Post, "api/student/tests/cheat/verify", new { testResultId, pinCode }, DefaultTimeout, ct);
	}

	public Task<ApiResult<Empty>> SendSignalsAsync(string testResultId, IReadOnlyList<ClientSignal> signals, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/tests/activity", new { testResultId, signals }, DefaultTimeout, ct);
	}

	public Task<ApiResult<HeartbeatResponse>> HeartbeatAsync(string testResultId, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<HeartbeatResponse>(HttpMethod.Post, "api/kiosk/v1/heartbeat", new { testResultId }, DefaultTimeout, ct);
	}

	public Task<ApiResult<SnapshotResponse>> UploadSnapshotAsync(string testResultId, string type, string phase, byte[]? jpeg, SnapshotDetails? details, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<SnapshotResponse>(HttpMethod.Post, "api/kiosk/v1/proctor/snapshot", new
		{
			testResultId = testResultId,
			type = type,
			phase = phase,
			image = ((jpeg != null && jpeg.Length > 0) ? Convert.ToBase64String(jpeg) : null),
			details = details
		}, TimeSpan.FromSeconds(40L), ct);
	}

	public Task<ApiResult<ProctorStateResponse>> GetProctorStateAsync(string testResultId, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<ProctorStateResponse>(HttpMethod.Get, "api/kiosk/v1/proctor/state?testResultId=" + Uri.EscapeDataString(testResultId), null, DefaultTimeout, ct);
	}

	public Task<ApiResult<ResultResponse>> GetResultAsync(string testResultId, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<ResultResponse>(HttpMethod.Get, "api/kiosk/v1/result?testResultId=" + Uri.EscapeDataString(testResultId), null, DefaultTimeout, ct);
	}

	public Task<ApiResult<SurveyResponse>> GetSurveyAsync(string surveyId, string? testResultId, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<SurveyResponse>(HttpMethod.Get, "api/student/surveys/" + Uri.EscapeDataString(surveyId) + ((testResultId == null) ? "" : ("?testResultId=" + Uri.EscapeDataString(testResultId))), null, DefaultTimeout, ct);
	}

	public Task<ApiResult<Empty>> SubmitSurveyAsync(string surveyId, string? testResultId, IReadOnlyList<SurveyAnswerInput> answers, CancellationToken ct = default(CancellationToken))
	{
		return SendAsync<Empty>(HttpMethod.Post, "api/student/surveys/" + Uri.EscapeDataString(surveyId), new { testResultId, answers }, DefaultTimeout, ct);
	}

	public async Task<ApiResult<Empty>> DownloadAsync(string fileName, string destinationPath, long expectedSize, IProgress<long>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (!UpdateVerifier.IsValidFileName(fileName) || expectedSize <= 0)
		{
			return ApiResult<Empty>.Failure(400, "Fayl nomi noto'g'ri");
		}
		string partPath = destinationPath + ".part";
		TimeSpan stallTimeout = TimeSpan.FromSeconds(60L);
		using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
		watchdog.CancelAfter(stallTimeout);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
			long existing = 0L;
			if (File.Exists(partPath))
			{
				existing = new FileInfo(partPath).Length;
				if (existing >= expectedSize)
				{
					File.Delete(partPath);
					existing = 0L;
				}
			}
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "api/kiosk/v1/download/" + Uri.EscapeDataString(fileName)));
			request.Headers.Accept.Clear();
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
			request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
			if (existing > 0)
			{
				request.Headers.Range = new RangeHeaderValue(existing, null);
			}
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, watchdog.Token).ConfigureAwait(continueOnCapturedContext: false);
			int statusCode = (int)response.StatusCode;
			bool flag;
			switch (statusCode)
			{
			case 416:
				File.Delete(partPath);
				return ApiResult<Empty>.Network("Yuklash qayta boshlanadi");
			case 200:
			case 206:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				return ApiResult<Empty>.Failure(statusCode, DefaultMessage(statusCode));
			}
			bool append = statusCode == 206 && existing > 0;
			if (!append)
			{
				existing = 0L;
			}
			long total = existing;
			await using (Stream source = await response.Content.ReadAsStreamAsync(watchdog.Token).ConfigureAwait(continueOnCapturedContext: false))
			{
				await using FileStream target = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
				byte[] buffer = new byte[131072];
				int num;
				while ((num = await source.ReadAsync(buffer, watchdog.Token).ConfigureAwait(continueOnCapturedContext: false)) > 0)
				{
					total += num;
					if (total > expectedSize)
					{
						break;
					}
					await target.WriteAsync(buffer.AsMemory(0, num), watchdog.Token).ConfigureAwait(continueOnCapturedContext: false);
					watchdog.CancelAfter(stallTimeout);
					progress?.Report(total);
				}
			}
			if (total != expectedSize)
			{
				if (total > expectedSize)
				{
					File.Delete(partPath);
					return ApiResult<Empty>.Failure(502, "Server noto'g'ri fayl yubordi");
				}
				return ApiResult<Empty>.Network("Yuklash uzilib qoldi");
			}
			File.Move(partPath, destinationPath, overwrite: true);
			return ApiResult<Empty>.Success(Empty.Value, 200);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return ApiResult<Empty>.Network("Server javob bermadi. Internet aloqasini tekshiring.");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex3) when (((ex3 is HttpRequestException || ex3 is IOException || ex3 is InvalidOperationException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return ApiResult<Empty>.Network("Yuklab olishda xato: " + ex3.GetType().Name);
		}
	}

	private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string relativePath, object? body, TimeSpan timeout, CancellationToken ct, bool writeNulls = false) where T : class
	{
		using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeoutSource.CancelAfter(timeout);
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath));
			if (body != null)
			{
				byte[] content = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), writeNulls ? JsonWithNulls : Json);
				request.Content = new ByteArrayContent(content);
				request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
				{
					CharSet = "utf-8"
				};
			}
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			int status = (int)response.StatusCode;
			byte[] array = await ReadLimitedAsync(response, timeoutSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (response.IsSuccessStatusCode)
			{
				if (typeof(T) == typeof(Empty))
				{
					return ApiResult<T>.Success((T)(object)Empty.Value, status);
				}
				if (array.Length == 0)
				{
					return ApiResult<T>.Failure(status, "Server bo'sh javob qaytardi");
				}
				try
				{
					T val = JsonSerializer.Deserialize<T>(array, Json);
					return (val == null) ? ApiResult<T>.Failure(status, "Server javobini o'qib bo'lmadi") : ApiResult<T>.Success(val, status);
				}
				catch (JsonException)
				{
					return ApiResult<T>.Failure(status, "Server javobini o'qib bo'lmadi");
				}
			}
			var (text, code) = ParseError(array);
			return ApiResult<T>.Failure(status, text ?? DefaultMessage(status), code);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return ApiResult<T>.Network("Server javob bermadi. Internet aloqasini tekshiring.");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex4) when (((ex4 is HttpRequestException || ex4 is IOException || ex4 is InvalidOperationException) ? 1 : 0) != 0)
		{
			return ApiResult<T>.Network("Serverga ulanib bo'lmadi. Internet aloqasini tekshiring.");
		}
	}

	private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
	{
		long? contentLength = response.Content.Headers.ContentLength;
		if (contentLength.HasValue && contentLength.GetValueOrDefault() > 100663296)
		{
			throw new IOException("Javob hajmi juda katta");
		}
		byte[] result;
		await using (Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[81920];
			int num;
			while ((num = await stream.ReadAsync(chunk, ct).ConfigureAwait(continueOnCapturedContext: false)) > 0)
			{
				if (buffer.Length + num > 100663296)
				{
					throw new IOException("Javob hajmi juda katta");
				}
				buffer.Write(chunk, 0, num);
			}
			result = buffer.ToArray();
		}
		return result;
	}

	private static (string? Error, string? Code) ParseError(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return (Error: null, Code: null);
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(bytes);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				return (Error: null, Code: null);
			}
			string item = null;
			string item2 = null;
			if (jsonDocument.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String)
			{
				item = Truncate(value.GetString(), 400);
			}
			if (jsonDocument.RootElement.TryGetProperty("code", out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				item2 = Truncate(value2.GetString(), 64);
			}
			return (Error: item, Code: item2);
		}
		catch (JsonException)
		{
			return (Error: null, Code: null);
		}
	}

	private static string DefaultMessage(int status)
	{
		if (status < 500)
		{
			return status switch
			{
				401 => "Sessiya tugagan. Qayta kiring.", 
				403 => "Ruxsat yo'q", 
				404 => "Topilmadi", 
				423 => "Test bloklangan", 
				429 => "Juda ko'p so'rov. Birozdan keyin urinib ko'ring.", 
				_ => $"So'rov bajarilmadi ({status})", 
			};
		}
		return "Serverda xatolik. Birozdan keyin urinib ko'ring.";
	}

	private static string? Truncate(string? value, int max)
	{
		if (value != null)
		{
			if (value.Length > max)
			{
				return value.Substring(0, max);
			}
			return value;
		}
		return null;
	}

	private static bool IsCleanId(string value)
	{
		int length = value.Length;
		if (length > 0 && length <= 128)
		{
			return value.All((char c) => char.IsAsciiLetterOrDigit(c) || c == '-');
		}
		return false;
	}

	private static string Sanitize(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			bool flag = char.IsAsciiLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == ' ' || c == '-' || c == '.') ? true : false);
				flag = flag2;
			}
			stringBuilder2.Append(flag ? c : '_');
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return "Windows";
	}

	// Savol ichidagi rasmni (autentifikatsiyalangan klient orqali) yuklab, baytlar + media turini qaytaradi.
	// Faqat shu portal (base) hostidan, faqat rasm turlari, hajm cheklovi bilan. Amaliy AI yordam uchun.
	public async Task<(byte[] Bytes, string MediaType)?> FetchImageAsync(string url, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && !Uri.TryCreate(_baseUri, url, out uri))
		{
			return null;
		}
		bool secure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
		if ((!secure && !uri.IsLoopback) || !string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.Clear();
			request.Headers.Accept.ParseAdd("image/*");
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
			if (mediaType != "image/jpeg" && mediaType != "image/png" && mediaType != "image/gif" && mediaType != "image/webp")
			{
				return null;
			}
			if (response.Content.Headers.ContentLength > 6000000)
			{
				return null;
			}
			byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (bytes.Length == 0 || bytes.Length > 6000000)
			{
				return null;
			}
			return (bytes, mediaType);
		}
		catch
		{
			return null;
		}
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
