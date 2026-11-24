namespace ChsbKiosk.Core.Api;

public sealed class SnapshotResponse
{
	public bool Success { get; set; }

	public bool Stored { get; set; }

	public string? Id { get; set; }

	public int WarningCount { get; set; }
}
