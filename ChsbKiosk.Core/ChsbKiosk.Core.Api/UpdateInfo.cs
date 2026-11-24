using System.Collections.Generic;

namespace ChsbKiosk.Core.Api;

public sealed class UpdateInfo
{
	public string Version { get; set; } = "";

	public string? Notes { get; set; }

	public List<UpdatePackage> Packages { get; set; } = new List<UpdatePackage>();
}
