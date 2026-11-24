using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ChsbKiosk.Core.Api;
using ChsbKiosk.Core.Proctor;
using OpenCvSharp;

namespace ChsbKiosk.Proctor;

public sealed class CameraProctor : IDisposable
{
	private const int PreviewIntervalMs = 110;

	private const int ReopenIntervalMs = 2000;

	private const int MaxReadFailures = 12;

	private readonly ProctorSettings _settings;

	private readonly int _cameraIndex;

	private readonly int _analyzeIntervalMs;

	private Thread? _thread;

	private volatile bool _running;

	private volatile bool _paused;

	private int _resetQuietMs = -1;

	public bool IsRunning => _running;

	public bool Paused
	{
		get
		{
			return _paused;
		}
		set
		{
			_paused = value;
		}
	}

	public event Action<PreviewFrame>? Preview;

	public event Action<ProctorIncident>? Incident;

	public event Action<ProctorCondition>? Recovered;

	public event Action<ProctorCondition>? ConditionChanged;

	public event Action<string>? Faulted;

	public CameraProctor(ProctorSettings settings, int cameraIndex)
	{
		_settings = settings.Sanitized();
		_cameraIndex = Math.Max(0, cameraIndex);
		_analyzeIntervalMs = (int)Math.Round(1000.0 / _settings.AnalyzeFps);
	}

	public void RequestReset(int quietMs)
	{
		Interlocked.Exchange(ref _resetQuietMs, Math.Max(0, quietMs));
	}

	public static bool Probe(int cameraIndex, TimeSpan timeout)
	{
		try
		{
			using VideoCapture videoCapture = Open(Math.Max(0, cameraIndex));
			if (videoCapture == null)
			{
				return false;
			}
			using Mat mat = new Mat();
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (stopwatch.Elapsed < timeout)
			{
				if (videoCapture.Read(mat) && !mat.Empty())
				{
					return true;
				}
				Thread.Sleep(80);
			}
			return false;
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Kamerani tekshirib bo'lmadi");
			return false;
		}
	}

	public void Start()
	{
		if (!_running)
		{
			_running = true;
			_thread = new Thread(Run)
			{
				IsBackground = true,
				Name = "CHSB camera proctor",
				Priority = ThreadPriority.BelowNormal
			};
			_thread.Start();
		}
	}

	public void Stop()
	{
		_running = false;
		Thread thread = _thread;
		_thread = null;
		if (thread != null && thread != Thread.CurrentThread)
		{
			thread.Join(TimeSpan.FromSeconds(3L));
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private static VideoCapture? Open(int index)
	{
		VideoCaptureAPIs[] array = new VideoCaptureAPIs[3]
		{
			VideoCaptureAPIs.DSHOW,
			VideoCaptureAPIs.MSMF,
			VideoCaptureAPIs.ANY
		};
		foreach (VideoCaptureAPIs videoCaptureAPIs in array)
		{
			VideoCapture videoCapture = null;
			try
			{
				videoCapture = new VideoCapture(index, videoCaptureAPIs);
				if (videoCapture.IsOpened())
				{
					videoCapture.Set(VideoCaptureProperties.FrameWidth, 640.0);
					videoCapture.Set(VideoCaptureProperties.FrameHeight, 480.0);
					videoCapture.Set(VideoCaptureProperties.Fps, 15.0);
					videoCapture.Set(VideoCaptureProperties.BufferSize, 1.0);
					return videoCapture;
				}
			}
			catch (Exception exception)
			{
				Logger.Error(exception, $"Kamera ochilmadi ({videoCaptureAPIs})");
			}
			videoCapture?.Dispose();
		}
		return null;
	}

	private static byte[] LoadModel()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("yunet.onnx") ?? throw new InvalidOperationException("Yuz aniqlash modeli ilova ichida topilmadi");
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private void Run()
	{
		VideoCapture videoCapture = null;
		FaceAnalyzer faceAnalyzer = null;
		using Mat mat = new Mat();
		using Mat mat2 = new Mat();
		using Mat mirrored = new Mat();
		ProctorPolicy proctorPolicy = new ProctorPolicy(_settings);
		Stopwatch stopwatch = Stopwatch.StartNew();
		long num = -4611686018427387904L;
		long num2 = -4611686018427387904L;
		long num3 = -4611686018427387904L;
		int num4 = 0;
		int num5 = 0;
		ProctorCondition last = ProctorCondition.Ok;
		bool flag = false;
		try
		{
			faceAnalyzer = new FaceAnalyzer(LoadModel());
			while (_running)
			{
				long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
				int num6 = Interlocked.Exchange(ref _resetQuietMs, -1);
				if (num6 >= 0)
				{
					proctorPolicy.Reset(elapsedMilliseconds, num6);
				}
				bool paused = _paused;
				if (flag && !paused)
				{
					proctorPolicy.Reset(elapsedMilliseconds);
				}
				flag = paused;
				if (videoCapture == null && elapsedMilliseconds - num3 >= 2000)
				{
					num3 = elapsedMilliseconds;
					videoCapture = Open(_cameraIndex);
					num4 = 0;
				}
				bool flag2 = false;
				if (videoCapture != null)
				{
					try
					{
						flag2 = videoCapture.Read(mat) && !mat.Empty();
					}
					catch (Exception)
					{
						flag2 = false;
					}
					if (flag2)
					{
						num4 = 0;
					}
					else if (++num4 >= 12)
					{
						videoCapture.Dispose();
						videoCapture = null;
						num3 = elapsedMilliseconds;
					}
				}
				if (!flag2)
				{
					if (!paused && elapsedMilliseconds - num >= _analyzeIntervalMs)
					{
						num = elapsedMilliseconds;
						ProctorSignal signal = proctorPolicy.Update(new FrameAnalysis
						{
							CameraOk = false
						}, elapsedMilliseconds);
						NotifyCondition(proctorPolicy.LastCondition, ref last);
						Dispatch(signal, null, null);
					}
					Thread.Sleep(120);
					continue;
				}
				Cv2.Resize(mat, mat2, new Size(320, 240), 0.0, 0.0, InterpolationFlags.Area);
				if (elapsedMilliseconds - num2 >= 110 && this.Preview != null)
				{
					num2 = elapsedMilliseconds;
					EmitPreview(mat2, mirrored);
				}
				if (!paused && elapsedMilliseconds - num >= _analyzeIntervalMs)
				{
					num = elapsedMilliseconds;
					FrameAnalysis frameAnalysis;
					try
					{
						frameAnalysis = faceAnalyzer.Analyze(mat2);
						num5 = 0;
					}
					catch (Exception innerException)
					{
						if (++num5 >= 5)
						{
							throw new InvalidOperationException("Kadr tahlili ishlamayapti", innerException);
						}
						Thread.Sleep(50);
						continue;
					}
					ProctorSignal signal2 = proctorPolicy.Update(frameAnalysis, elapsedMilliseconds);
					NotifyCondition(proctorPolicy.LastCondition, ref last);
					Dispatch(signal2, mat, frameAnalysis);
				}
				Thread.Sleep(15);
			}
		}
		catch (Exception ex2)
		{
			Logger.Error(ex2, "Kamera nazorati to'xtadi");
			_running = false;
			this.Faulted?.Invoke(ex2.GetBaseException().Message);
		}
		finally
		{
			videoCapture?.Dispose();
			faceAnalyzer?.Dispose();
		}
	}

	private void NotifyCondition(ProctorCondition current, ref ProctorCondition last)
	{
		if (current != last)
		{
			last = current;
			this.ConditionChanged?.Invoke(current);
		}
	}

	private void Dispatch(ProctorSignal signal, Mat? frame, FrameAnalysis? analysis)
	{
		switch (signal.Kind)
		{
		case ProctorSignalKind.Incident:
			this.Incident?.Invoke(new ProctorIncident
			{
				Condition = signal.Condition,
				Jpeg = ((frame == null) ? null : EncodeEvidence(frame)),
				Details = BuildDetails(signal, analysis)
			});
			break;
		case ProctorSignalKind.Recovered:
			this.Recovered?.Invoke(signal.Condition);
			break;
		}
	}

	private static SnapshotDetails BuildDetails(ProctorSignal signal, FrameAnalysis? analysis)
	{
		SnapshotDetails snapshotDetails = new SnapshotDetails
		{
			DurationMs = Math.Clamp(signal.DurationMs, 0, 600000)
		};
		if (analysis == null)
		{
			return snapshotDetails;
		}
		snapshotDetails.Faces = Math.Min(analysis.Faces.Count, 20);
		snapshotDetails.Brightness = Math.Round(Math.Clamp(analysis.Brightness, 0.0, 255.0), 1);
		FaceDetection faceDetection = null;
		foreach (FaceDetection face in analysis.Faces)
		{
			if (faceDetection == null || face.Area > faceDetection.Area)
			{
				faceDetection = face;
			}
		}
		if (faceDetection != null)
		{
			snapshotDetails.Yaw = Math.Round(Math.Clamp(faceDetection.YawRatio, -5.0, 5.0), 3);
			snapshotDetails.Pitch = Math.Round(Math.Clamp(faceDetection.PitchRatio, -5.0, 5.0), 3);
		}
		return snapshotDetails;
	}

	private static byte[]? EncodeEvidence(Mat frame)
	{
		try
		{
			using Mat mat = new Mat();
			double num = 480.0 / (double)Math.Max(1, frame.Width);
			if (num < 1.0)
			{
				Cv2.Resize(frame, mat, new Size(480, Math.Max(1, (int)Math.Round((double)frame.Height * num))), 0.0, 0.0, InterpolationFlags.Area);
			}
			else
			{
				frame.CopyTo(mat);
			}
			Cv2.ImEncode(".jpg", mat, out byte[] buf, new ImageEncodingParam(ImwriteFlags.JpegQuality, 72));
			return buf;
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Rasmni kodlab bo'lmadi");
			return null;
		}
	}

	private void EmitPreview(Mat small, Mat mirrored)
	{
		try
		{
			Cv2.Flip(small, mirrored, FlipMode.Y);
			int num = (int)mirrored.Step();
			byte[] array = new byte[num * mirrored.Height];
			Marshal.Copy(mirrored.Data, array, 0, array.Length);
			this.Preview?.Invoke(new PreviewFrame
			{
				Bgr = array,
				Width = mirrored.Width,
				Height = mirrored.Height,
				Stride = num
			});
		}
		catch (Exception)
		{
		}
	}
}
