using System.Collections.Generic;

namespace ChsbKiosk.Core.Exam;

public sealed class ExamQuestion
{
	public required string Id { get; init; }

	public required int Number { get; init; }

	public string? SubjectName { get; init; }

	public required string TextHtml { get; init; }

	public required IReadOnlyDictionary<string, string> OptionsHtml { get; init; }
}
