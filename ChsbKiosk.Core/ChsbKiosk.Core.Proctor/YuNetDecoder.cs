using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Proctor;

public static class YuNetDecoder
{
	public static readonly int[] Strides = new int[3] { 8, 16, 32 };

	public static readonly string[] OutputNames = new string[12]
	{
		"cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32", "bbox_8", "bbox_16", "bbox_32", "kps_8",
		"kps_16", "kps_32"
	};

	public static int PadTo32(int size)
	{
		return ((size - 1) / 32 + 1) * 32;
	}

	public static List<FaceDetection> Decode(IReadOnlyList<float[]> outputs, int paddedWidth, int paddedHeight, float scoreThreshold = 0.6f, float nmsThreshold = 0.3f, int maxFaces = 8)
	{
		if (outputs.Count != OutputNames.Length)
		{
			throw new ArgumentException("YuNet 12 ta chiqish qaytarishi kerak", "outputs");
		}
		List<FaceDetection> list = new List<FaceDetection>();
		for (int i = 0; i < Strides.Length; i++)
		{
			int stride = Strides[i];
			int num = paddedWidth / stride;
			int num2 = paddedHeight / stride;
			int num3 = num * num2;
			float[] array = outputs[i];
			float[] array2 = outputs[3 + i];
			float[] array3 = outputs[6 + i];
			float[] kps = outputs[9 + i];
			if (array.Length < num3 || array2.Length < num3 || array3.Length < num3 * 4 || kps.Length < num3 * 10)
			{
				throw new ArgumentException("YuNet chiqish o'lchami kutilganidan kichik", "outputs");
			}
			int r;
			for (r = 0; r < num2; r++)
			{
				int c;
				for (c = 0; c < num; c++)
				{
					int idx = r * num + c;
					float num4 = Math.Clamp(array[idx], 0f, 1f);
					float num5 = Math.Clamp(array2[idx], 0f, 1f);
					float num6 = MathF.Sqrt(num4 * num5);
					if (!(num6 < scoreThreshold))
					{
						float num7 = ((float)c + array3[idx * 4]) * (float)stride;
						float num8 = ((float)r + array3[idx * 4 + 1]) * (float)stride;
						float num9 = MathF.Exp(array3[idx * 4 + 2]) * (float)stride;
						float num10 = MathF.Exp(array3[idx * 4 + 3]) * (float)stride;
						list.Add(new FaceDetection
						{
							X = num7 - num9 / 2f,
							Y = num8 - num10 / 2f,
							Width = num9,
							Height = num10,
							Score = num6,
							EyeA = Landmark(0),
							EyeB = Landmark(1),
							Nose = Landmark(2),
							MouthA = Landmark(3),
							MouthB = Landmark(4)
						});
					}
					PointF2 Landmark(int n)
					{
						return new PointF2((kps[idx * 10 + 2 * n] + (float)c) * (float)stride, (kps[idx * 10 + 2 * n + 1] + (float)r) * (float)stride);
					}
				}
			}
		}
		return NonMaxSuppression(list, nmsThreshold, maxFaces);
	}

	private static List<FaceDetection> NonMaxSuppression(List<FaceDetection> candidates, float threshold, int maxFaces)
	{
		candidates.Sort((FaceDetection a, FaceDetection b) => b.Score.CompareTo(a.Score));
		List<FaceDetection> list = new List<FaceDetection>();
		foreach (FaceDetection candidate in candidates)
		{
			bool flag = false;
			foreach (FaceDetection item in list)
			{
				if (IoU(candidate, item) > threshold)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list.Add(candidate);
				if (list.Count >= maxFaces)
				{
					break;
				}
			}
		}
		return list;
	}

	private static float IoU(FaceDetection a, FaceDetection b)
	{
		float num = Math.Max(a.X, b.X);
		float num2 = Math.Max(a.Y, b.Y);
		float num3 = Math.Min(a.X + a.Width, b.X + b.Width);
		float num4 = Math.Min(a.Y + a.Height, b.Y + b.Height);
		float num5 = Math.Max(0f, num3 - num) * Math.Max(0f, num4 - num2);
		float num6 = a.Area + b.Area - num5;
		if (!(num6 <= 0f))
		{
			return num5 / num6;
		}
		return 0f;
	}
}
