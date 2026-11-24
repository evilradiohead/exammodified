namespace ChsbKiosk.Core.Storage;

public sealed class LocalAnswer
{
	public string QuestionId { get; set; } = "";

	public string? SelectedAnswer { get; set; }

	public int TimeSpentSec { get; set; }
}
