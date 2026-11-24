using System;

namespace ChsbKiosk.Core.Proctor;

public sealed class FaceDetection
{
	public float X { get; init; }

	public float Y { get; init; }

	public float Width { get; init; }

	public float Height { get; init; }

	public float Score { get; init; }

	public PointF2 EyeA { get; init; }

	public PointF2 EyeB { get; init; }

	public PointF2 Nose { get; init; }

	public PointF2 MouthA { get; init; }

	public PointF2 MouthB { get; init; }

	public float Area => Math.Max(0f, Width) * Math.Max(0f, Height);

	public double YawRatio
	{
		get
		{
			float num = Math.Min(EyeA.X, EyeB.X);
			float num2 = Math.Max(EyeA.X, EyeB.X);
			float num3 = num2 - num;
			if (num3 < Math.Max(2f, Width * 0.12f))
			{
				if (!(Nose.X >= (num + num2) / 2f))
				{
					return -0.5;
				}
				return 1.5;
			}
			return (Nose.X - num) / num3;
		}
	}

	public double PitchRatio
	{
		get
		{
			float num = (EyeA.Y + EyeB.Y) / 2f;
			float num2 = (MouthA.Y + MouthB.Y) / 2f - num;
			if (!(num2 < 2f))
			{
				return (Nose.Y - num) / num2;
			}
			return 0.5;
		}
	}
}
