namespace ChsbKiosk.Core.Api;

public sealed class QuestionDto
{
	public string Id { get; set; } = "";

	public int OrderNumber { get; set; }

	public string? SkillType { get; set; }

	public string? SubjectId { get; set; }

	public string? SubjectName { get; set; }

	public string? SelectedAnswer { get; set; }

	public int TimeSpentSec { get; set; }

	public string QuestionText { get; set; } = "";

	public string? OptionA { get; set; }

	public string? OptionB { get; set; }

	public string? OptionC { get; set; }

	public string? OptionD { get; set; }
}
