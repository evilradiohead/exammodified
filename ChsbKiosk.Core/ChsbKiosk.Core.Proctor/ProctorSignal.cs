namespace ChsbKiosk.Core.Proctor;

public readonly record struct ProctorSignal(ProctorSignalKind Kind, ProctorCondition Condition, int DurationMs)
{
	public static readonly ProctorSignal None = new ProctorSignal(ProctorSignalKind.None, ProctorCondition.Ok, 0);
}
