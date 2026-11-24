namespace ChsbKiosk.Core.Api;

public sealed class UpdatePackage
{
	public string Arch { get; set; } = "";

	public string Kind { get; set; } = "";

	public string File { get; set; } = "";

	public long Size { get; set; }

	public string Sha256 { get; set; } = "";

	public string Signature { get; set; } = "";
}
