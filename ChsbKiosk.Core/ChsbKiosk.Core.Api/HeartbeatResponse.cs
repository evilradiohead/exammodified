using System;
using ChsbKiosk.Core.Proctor;

namespace ChsbKiosk.Core.Api;

public sealed class HeartbeatResponse
{
	public DateTimeOffset ServerTime { get; set; }

	public bool IsCompleted { get; set; }

	public bool CheatLocked { get; set; }

	public int ExtraTimeMinutes { get; set; }

	public DateTimeOffset EndsAt { get; set; }

	public ProctorSettings? Proctor { get; set; }
}
