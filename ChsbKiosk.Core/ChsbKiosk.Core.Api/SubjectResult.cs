namespace ChsbKiosk.Core.Api;

public sealed class SubjectResult
{
	public string Name { get; set; } = "";

	public int Total { get; set; }

	public int Correct { get; set; }

	public int Incorrect { get; set; }

	public int Unanswered { get; set; }

	public double Percent { get; set; }
}
