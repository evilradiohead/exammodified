using System;
using System.Runtime.InteropServices;
using ChsbKiosk.Core.Proctor;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace ChsbKiosk.Proctor;

public sealed class FaceAnalyzer : IDisposable
{
	public const int Width = 320;

	public const int Height = 240;

	private static readonly int PaddedWidth = YuNetDecoder.PadTo32(320);

	private static readonly int PaddedHeight = YuNetDecoder.PadTo32(240);

	private readonly Net _net;

	private readonly Mat _gray = new Mat();

	private readonly Mat _padded = new Mat();

	public FaceAnalyzer(byte[] onnxModel)
	{
		_net = CvDnn.ReadNetFromOnnx(onnxModel) ?? throw new InvalidOperationException("Yuz aniqlash modeli yuklanmadi");
		_net.SetPreferableBackend(Backend.OPENCV);
		_net.SetPreferableTarget(Target.CPU);
	}

	public FrameAnalysis Analyze(Mat bgr)
	{
		if (bgr.Width != 320 || bgr.Height != 240)
		{
			throw new ArgumentException("Kadr 320x240 bo'lishi kerak", "bgr");
		}
		Cv2.CvtColor(bgr, _gray, ColorConversionCodes.BGR2GRAY);
		Cv2.MeanStdDev(_gray, out var mean, out var stddev);
		Cv2.CopyMakeBorder(bgr, _padded, 0, PaddedHeight - 240, 0, PaddedWidth - 320, BorderTypes.Constant, Scalar.All(0.0));
		using Mat blob = CvDnn.BlobFromImage(_padded);
		_net.SetInput(blob);
		Mat[] array = new Mat[YuNetDecoder.OutputNames.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = new Mat();
		}
		try
		{
			_net.Forward(array, YuNetDecoder.OutputNames);
			float[][] array2 = new float[array.Length][];
			for (int j = 0; j < array.Length; j++)
			{
				array2[j] = ToFloatArray(array[j]);
			}
			return new FrameAnalysis
			{
				CameraOk = true,
				Brightness = mean.Val0,
				StdDev = stddev.Val0,
				Faces = YuNetDecoder.Decode(array2, PaddedWidth, PaddedHeight)
			};
		}
		finally
		{
			Mat[] array3 = array;
			for (int k = 0; k < array3.Length; k++)
			{
				array3[k].Dispose();
			}
		}
	}

	private static float[] ToFloatArray(Mat mat)
	{
		if (mat.Depth() != 5)
		{
			throw new InvalidOperationException("Model chiqishi float bo'lishi kerak");
		}
		using Mat mat2 = (mat.IsContinuous() ? null : mat.Clone());
		Mat mat3 = mat2 ?? mat;
		int num = checked((int)mat3.Total());
		float[] array = new float[num];
		if (num > 0)
		{
			Marshal.Copy(mat3.Data, array, 0, num);
		}
		return array;
	}

	public void Dispose()
	{
		_net.Dispose();
		_gray.Dispose();
		_padded.Dispose();
	}
}
