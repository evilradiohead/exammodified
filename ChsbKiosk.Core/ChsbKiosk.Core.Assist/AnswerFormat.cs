using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChsbKiosk.Core.Exam;

namespace ChsbKiosk.Core.Assist;

// Amaliy (practice) AI yordam uchun SOF (platformaga bog'liq bo'lmagan) mantiq:
// so'rov JSON'ini qurish, javob harfini ajratish, HTML -> matn, rasm URL'larini topish.
// Bu Core'da (net10.0) joylashgani uchun testlar uni to'g'ridan-to'g'ri (Windows'siz) sinaydi.
public static class AnswerFormat
{
	public const string SystemPrompt =
		"You help a student PRACTICE multiple-choice questions. "
		+ "You are given a question and four options labelled A, B, C, D (text may be in Uzbek; images may be attached). "
		+ "Work out the best answer and reply with ONLY the single capital letter A, B, C or D. No words, no punctuation.";

	public const int MaxTokens = 1024;
	public const int MaxImages = 8;

	public static readonly string[] Keys = { "A", "B", "C", "D" };

	private static readonly Regex ImgSrc = new Regex(
		"<img\\b[^>]*?\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	// Anthropic Messages so'rovi tanasini (matn + rasm bloklari) tuzadi va JSON qaytaradi.
	public static string BuildRequestJson(string model, string textPrompt,
		IReadOnlyList<(string Label, string MediaType, string Base64)> images)
	{
		var content = new List<object> { new { type = "text", text = textPrompt } };
		if (images != null)
		{
			foreach ((string Label, string MediaType, string Base64) img in images)
			{
				content.Add(new { type = "text", text = "Rasm — " + img.Label });
				content.Add(new
				{
					type = "image",
					source = new { type = "base64", media_type = img.MediaType, data = img.Base64 }
				});
			}
		}
		var body = new
		{
			model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5-5" : model,
			max_tokens = MaxTokens,
			system = SystemPrompt,
			output_config = new { effort = "low" },
			messages = new[] { new { role = "user", content = content } }
		};
		return JsonSerializer.Serialize(body, Json);
	}

	// Javobdagi text bloklaridan birinchi A/B/C/D harfini ajratadi; refusal bo'lsa null.
	public static string? ParseAnswerLetter(string payload)
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

	// Savol + variantlardan matnli so'rov tuzadi.
	public static string BuildTextPrompt(ExamQuestion question)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("Savol:\n").Append(HtmlToText(question.TextHtml)).Append("\n\n");
		foreach (string key in Keys)
		{
			string value = (question.OptionsHtml != null && question.OptionsHtml.TryGetValue(key, out string? html))
				? HtmlToText(html)
				: "";
			sb.Append(key).Append(") ").Append(value).Append('\n');
		}
		sb.Append("\n(Agar quyida rasmlar biriktirilgan bo'lsa, ular savol yoki variantlarga tegishli.)\n");
		sb.Append("Javob (faqat bitta harf: A, B, C yoki D):");
		return sb.ToString();
	}

	// Savol va variantlardagi <img src="..."> larni (yorliq bilan) to'playdi; data: va takror o'tkaziladi.
	public static List<(string Label, string Url)> CollectImageUrls(ExamQuestion question)
	{
		var result = new List<(string, string)>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ExtractInto(result, seen, "savol", question.TextHtml);
		foreach (string key in Keys)
		{
			string? html = (question.OptionsHtml != null && question.OptionsHtml.TryGetValue(key, out string? h)) ? h : null;
			ExtractInto(result, seen, key + " varianti", html);
		}
		return result;
	}

	private static void ExtractInto(List<(string, string)> result, HashSet<string> seen, string label, string? html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return;
		}
		foreach (Match match in ImgSrc.Matches(html))
		{
			string url = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
			if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || !seen.Add(url))
			{
				continue;
			}
			result.Add((label, url));
		}
	}

	public static string HtmlToText(string? html)
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
}
