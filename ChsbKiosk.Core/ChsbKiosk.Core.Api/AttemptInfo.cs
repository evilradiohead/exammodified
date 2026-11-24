using System;

namespace ChsbKiosk.Core.Api;

public sealed class AttemptInfo
{
	public string Id { get; set; } = "";

	public DateTimeOffset StartedAt { get; set; }

	public int ExtraTimeMinutes { get; set; }

	public bool CheatLocked { get; set; }

	public string? CheatReason { get; set; }

	public int ViolationCount { get; set; }

	public int UnlockCount { get; set; }

	public int MaxUnlocks { get; set; } = 2;
}
