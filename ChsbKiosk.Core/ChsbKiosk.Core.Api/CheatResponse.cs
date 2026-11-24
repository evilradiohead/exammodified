namespace ChsbKiosk.Core.Api;

public sealed class CheatResponse
{
	public bool Success { get; set; }

	public CheatEventInfo? Event { get; set; }

	public int ViolationCount { get; set; }

	public int UnlockCount { get; set; }

	public int MaxUnlocks { get; set; } = 2;

	public bool Finalized { get; set; }

	public string? Message { get; set; }
}
