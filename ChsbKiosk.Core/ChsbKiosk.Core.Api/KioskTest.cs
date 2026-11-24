using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class KioskTest
{
	public string Id { get; set; } = "";

	public string Variant { get; set; } = "";

	public string? Title { get; set; }

	public string TestType { get; set; } = "CHSB";

	public bool IsDemo { get; set; }

	public string? Grade { get; set; }

	public int Duration { get; set; }

	public int QuestionCount { get; set; }

	public DateTimeOffset? ActiveTo { get; set; }

	public List<TestSubjectInfo> Subjects { get; set; } = new List<TestSubjectInfo>();

	public bool InProgress { get; set; }

	public string? TestResultId { get; set; }

	public bool CheatLocked { get; set; }

	public bool RequiresPin { get; set; }

	public bool IsBlocked { get; set; }

	public string? BlockedReason { get; set; }

	public string DisplayTitle
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Title))
			{
				return Title;
			}
			return "Variant " + Variant;
		}
	}
}
