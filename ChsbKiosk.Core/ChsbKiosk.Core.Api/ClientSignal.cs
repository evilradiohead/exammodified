using System;

namespace ChsbKiosk.Core.Api;

public sealed class ClientSignal
{
	public string Kind { get; set; } = "";

	public DateTimeOffset? At { get; set; }

	public int Count { get; set; } = 1;

	public string? Detail { get; set; }
}
