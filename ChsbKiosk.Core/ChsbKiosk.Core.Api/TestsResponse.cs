using System;
using System.Collections.Generic;
using ChsbKiosk.Core.Proctor;

namespace ChsbKiosk.Core.Api;

public sealed class TestsResponse
{
	public DateTimeOffset ServerTime { get; set; }

	public StudentInfo Student { get; set; } = new StudentInfo();

	public List<KioskTest> Tests { get; set; } = new List<KioskTest>();

	public PendingSurveyInfo? PendingSurvey { get; set; }

	public ProctorSettings? Proctor { get; set; }
}
