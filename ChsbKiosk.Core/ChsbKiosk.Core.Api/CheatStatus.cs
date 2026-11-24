using System;

namespace ChsbKiosk.Core.Api;

public sealed class CheatStatus
{
	public string Id { get; set; } = "";

	public string? Reason { get; set; }

	public bool HasPin { get; set; }

	public string Kind { get; set; } = "VIOLATION";

	public bool IsInterruption => string.Equals(Kind, "INTERRUPTION", StringComparison.Ordinal);
}
