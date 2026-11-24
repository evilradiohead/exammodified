namespace ChsbKiosk.Core.Api;

public sealed class DeviceInfo
{
	public string DeviceId { get; init; } = "";

	public string Fingerprint { get; init; } = "";

	public string OsDescription { get; init; } = "Windows";
}
