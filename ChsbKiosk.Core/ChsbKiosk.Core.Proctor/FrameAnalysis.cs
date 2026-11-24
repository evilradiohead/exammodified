using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Proctor;

public sealed class FrameAnalysis
{
	public bool CameraOk { get; init; }

	public double Brightness { get; init; }

	public double StdDev { get; init; }

	public IReadOnlyList<FaceDetection> Faces { get; init; } = Array.Empty<FaceDetection>();
}
