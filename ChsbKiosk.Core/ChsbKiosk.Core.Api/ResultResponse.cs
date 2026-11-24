using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class ResultResponse
{
	public bool Completed { get; set; }

	public string? Title { get; set; }

	public string? CompletionType { get; set; }

	public int? DurationMinutes { get; set; }

	public int TotalQuestions { get; set; }

	public int Correct { get; set; }

	public int Incorrect { get; set; }

	public int Unanswered { get; set; }

	public double Percent { get; set; }

	public List<SubjectResult> Subjects { get; set; } = new List<SubjectResult>();
}
