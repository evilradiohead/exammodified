using System;

namespace ChsbKiosk.Core.Api;

public sealed class StartResponse
{
	public DateTimeOffset ServerTime { get; set; }

	public CheatStatus? CheatStatus { get; set; }

	public AttemptInfo TestResult { get; set; } = new AttemptInfo();

	public StartTestInfo Test { get; set; } = new StartTestInfo();
}
