namespace ChsbKiosk.Proctor;

public sealed class PreviewFrame
{
	public required byte[] Bgr { get; init; }

	public required int Width { get; init; }

	public required int Height { get; init; }

	public required int Stride { get; init; }
}
