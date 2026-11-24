namespace ChsbKiosk.Core.Api;

public sealed class CheatVerifyResponse
{
	public bool Success { get; set; }

	public int UnlockCount { get; set; }

	public int RemainingUnlocks { get; set; }

	public int CompensatedMinutes { get; set; }
}
