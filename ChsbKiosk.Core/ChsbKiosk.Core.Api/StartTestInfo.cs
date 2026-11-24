using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class StartTestInfo
{
	public string Id { get; set; } = "";

	public string Variant { get; set; } = "";

	public string? Title { get; set; }

	public string TestType { get; set; } = "CHSB";

	public int Duration { get; set; }

	public int DurationWithExtra { get; set; }

	public int QuestionCount { get; set; }

	public List<QuestionDto> Questions { get; set; } = new List<QuestionDto>();

	public string DisplayTitle
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Title))
			{
				return Title;
			}
			return "Variant " + Variant;
		}
	}
}
