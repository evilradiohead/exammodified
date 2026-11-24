using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class SurveyQuestionDto
{
	public string Id { get; set; } = "";

	public int OrderNumber { get; set; }

	public string Text { get; set; } = "";

	public string Type { get; set; } = "SINGLE_CHOICE";

	public List<SurveyOptionDto> Options { get; set; } = new List<SurveyOptionDto>();

	public bool IsText => string.Equals(Type, "TEXT", StringComparison.Ordinal);
}
