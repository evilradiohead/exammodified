using System;
using ChsbKiosk.Core.Proctor;

namespace ChsbKiosk.Core.Api;

public sealed class BootstrapResponse
{
	public DateTimeOffset ServerTime { get; set; }

	public string MinVersion { get; set; } = "";

	public bool Supported { get; set; } = true;

	public int HeartbeatSec { get; set; } = 120;

	public ProctorSettings Proctor { get; set; } = new ProctorSettings();

	public UpdateInfo? Update { get; set; }
}
