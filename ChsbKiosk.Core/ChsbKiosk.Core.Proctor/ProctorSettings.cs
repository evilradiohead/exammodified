using System;

namespace ChsbKiosk.Core.Proctor;

public sealed class ProctorSettings
{
	public string Mode { get; set; } = "enforce";

	public int MaxWarnings { get; set; } = 2;

	public int CountdownSec { get; set; } = 3;

	public int LookAwayMs { get; set; } = 2000;

	public int MultiFaceMs { get; set; } = 1500;

	public int NoFaceMs { get; set; } = 4000;

	public int CoveredMs { get; set; } = 4000;

	public int CameraOffMs { get; set; } = 6000;

	public double YawThreshold { get; set; } = 0.14;

	public double LookAwayDecay { get; set; } = 0.5;

	public double PitchDownThreshold { get; set; } = 1.0;

	public double SecondFaceMinRatio { get; set; } = 0.35;

	public double AnalyzeFps { get; set; } = 4.0;

	public double CoveredBrightness { get; set; } = 18.0;

	public double CoveredStdDev { get; set; } = 7.0;

	public int RecoverMs { get; set; } = 700;

	public bool IsOff => string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase);

	public bool IsShadow => string.Equals(Mode, "shadow", StringComparison.OrdinalIgnoreCase);

	public bool IsEnforce
	{
		get
		{
			if (!IsOff)
			{
				return !IsShadow;
			}
			return false;
		}
	}

	public bool SameAs(ProctorSettings other)
	{
		if (string.Equals(Mode, other.Mode, StringComparison.OrdinalIgnoreCase) && MaxWarnings == other.MaxWarnings && CountdownSec == other.CountdownSec && LookAwayMs == other.LookAwayMs && MultiFaceMs == other.MultiFaceMs && NoFaceMs == other.NoFaceMs && CoveredMs == other.CoveredMs && CameraOffMs == other.CameraOffMs && Near(YawThreshold, other.YawThreshold) && Near(LookAwayDecay, other.LookAwayDecay) && Near(PitchDownThreshold, other.PitchDownThreshold) && Near(SecondFaceMinRatio, other.SecondFaceMinRatio) && Near(AnalyzeFps, other.AnalyzeFps) && Near(CoveredBrightness, other.CoveredBrightness) && Near(CoveredStdDev, other.CoveredStdDev))
		{
			return RecoverMs == other.RecoverMs;
		}
		return false;
	}

	private static bool Near(double a, double b)
	{
		return Math.Abs(a - b) < 0.0005;
	}

	public ProctorSettings Sanitized()
	{
		return new ProctorSettings
		{
			Mode = Mode,
			MaxWarnings = Math.Clamp(MaxWarnings, 0, 10),
			CountdownSec = Math.Clamp(CountdownSec, 3, 15),
			LookAwayMs = Math.Clamp(LookAwayMs, 800, 20000),
			MultiFaceMs = Math.Clamp(MultiFaceMs, 500, 20000),
			NoFaceMs = Math.Clamp(NoFaceMs, 1500, 60000),
			CoveredMs = Math.Clamp(CoveredMs, 1500, 60000),
			CameraOffMs = Math.Clamp(CameraOffMs, 2000, 60000),
			YawThreshold = Math.Clamp(YawThreshold, 0.08, 0.45),
			LookAwayDecay = Math.Clamp(LookAwayDecay, 0.1, 4.0),
			PitchDownThreshold = Math.Clamp(PitchDownThreshold, 0.55, 2.0),
			SecondFaceMinRatio = Math.Clamp(SecondFaceMinRatio, 0.1, 1.0),
			AnalyzeFps = Math.Clamp(AnalyzeFps, 1.0, 8.0),
			CoveredBrightness = Math.Clamp(CoveredBrightness, 2.0, 80.0),
			CoveredStdDev = Math.Clamp(CoveredStdDev, 1.0, 40.0),
			RecoverMs = Math.Clamp(RecoverMs, 400, 5000)
		};
	}
}
