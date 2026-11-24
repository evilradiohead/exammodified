namespace ChsbKiosk.Core.Api;

public sealed class CheatEventInfo
{
	public string Id { get; set; } = "";

	public string? Reason { get; set; }

	public bool HasPin { get; set; }
}
