namespace ChsbKiosk.Core.Api;

public sealed class SurveyAnswerInput
{
	public string QuestionId { get; set; } = "";

	public string? OptionId { get; set; }

	public string? TextValue { get; set; }
}
