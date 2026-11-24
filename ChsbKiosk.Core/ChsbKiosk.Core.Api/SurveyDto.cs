using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class SurveyDto
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public string? Description { get; set; }

	public int RemainingAttempts { get; set; }

	public List<SurveyQuestionDto> Questions { get; set; } = new List<SurveyQuestionDto>();
}
