using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Proctor;

namespace ChsbKiosk.Proctor;

public sealed class ProctorIncident
{
	public required ProctorCondition Condition { get; init; }

	public byte[]? Jpeg { get; init; }

	public required SnapshotDetails Details { get; init; }
}
