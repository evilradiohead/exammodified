namespace ChsbKiosk.Core.Api;

public sealed class PendingSurveyInfo
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public int QuestionsCount { get; set; }

	public string TestResultId { get; set; } = "";
}
