namespace ChsbKiosk.Core.Api;

public sealed class SurveyOptionDto
{
	public string Id { get; set; } = "";

	public int OrderIndex { get; set; }

	public string? Label { get; set; }

	public string Text { get; set; } = "";
}
