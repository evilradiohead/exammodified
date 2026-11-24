namespace ChsbKiosk.Core.Api;

public sealed class SnapshotDetails
{
	public int? Faces { get; set; }

	public double? Yaw { get; set; }

	public double? Pitch { get; set; }

	public double? Brightness { get; set; }

	public int? DurationMs { get; set; }
}
