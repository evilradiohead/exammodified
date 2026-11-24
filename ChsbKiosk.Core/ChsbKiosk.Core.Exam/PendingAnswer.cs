namespace ChsbKiosk.Core.Exam;

public sealed class PendingAnswer
{
	public required string QuestionId { get; init; }

	public string? SelectedAnswer { get; init; }

	public int TimeSpentSec { get; init; }

	public long Version { get; init; }
}
