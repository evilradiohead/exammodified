using System.Text;
using ChsbKiosk.Core.Assist;
using ChsbKiosk.Core.Exam;

// AnswerFormat (practice AI yordam mantig'i) uchun minimal regressiya testlari.
// Core'ni to'g'ridan-to'g'ri sinaydi — Windows kerak emas. Xato bo'lsa, exit code 1.

int failed = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok) failed++;
}

var q = new ExamQuestion
{
    Id = "q1",
    Number = 1,
    TextHtml = "<p>2 + 2 nechaga teng? <img src=\"/media/q1.png\"></p>",
    OptionsHtml = new Dictionary<string, string>
    {
        ["A"] = "3",
        ["B"] = "<b>4</b>",
        ["C"] = "5",
        ["D"] = "6"
    }
};

// 1) HTML -> matn
Check("HtmlToText strips tags", AnswerFormat.HtmlToText("<b>4</b>") == "4");

// 2) matnli so'rov
string prompt = AnswerFormat.BuildTextPrompt(q);
Check("prompt has question", prompt.Contains("2 + 2 nechaga teng?"));
Check("prompt has 'A) 3'", prompt.Contains("A) 3"));
Check("prompt has 'B) 4' (tags stripped)", prompt.Contains("B) 4"));
Check("prompt asks for a single letter", prompt.Contains("Javob (faqat bitta harf"));

// 3) rasm URL'larini topish (data: va takror o'tkaziladi)
var urls = AnswerFormat.CollectImageUrls(q);
Check("collects 1 image url", urls.Count == 1 && urls[0].Url == "/media/q1.png");

// 4) so'rov JSON wire-format (snake_case saqlanishi SHART)
string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("ABC")); // "QUJD"
string json = AnswerFormat.BuildRequestJson(
    "claude-opus-5-5", prompt,
    new List<(string, string, string)> { ("A varianti", "image/png", b64) });
foreach (var key in new[] { "\"model\"", "\"max_tokens\"", "\"system\"", "\"output_config\"",
    "\"effort\"", "\"messages\"", "\"role\":\"user\"", "\"content\"", "\"type\":\"text\"",
    "\"type\":\"image\"", "\"source\"", "\"media_type\":\"image/png\"", "\"data\":\"QUJD\"" })
{
    Check($"json has {key}", json.Contains(key));
}
foreach (var bad in new[] { "maxTokens", "outputConfig", "mediaType" })
{
    Check($"json must NOT have {bad}", !json.Contains(bad));
}

// 5) javob harfini ajratish (bo'sh thinking blokini e'tiborsiz qoldiradi)
string sample = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":\"B\"}]}";
Check("parses 'B'", AnswerFormat.ParseAnswerLetter(sample) == "B");
Check("refusal -> null", AnswerFormat.ParseAnswerLetter("{\"stop_reason\":\"refusal\",\"content\":[]}") == null);
Check("garbage -> null", AnswerFormat.ParseAnswerLetter("not json") == null);

Console.WriteLine(failed == 0 ? "\nALL PASS" : $"\n{failed} FAILED");
return failed == 0 ? 0 : 1;
