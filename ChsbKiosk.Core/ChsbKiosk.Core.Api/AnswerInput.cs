namespace ChsbKiosk.Core.Api;

public sealed class AnswerInput
{
	public string QuestionId { get; set; } = "";

	public string? SelectedAnswer { get; set; }

	public int TimeSpentSec { get; set; }
}
