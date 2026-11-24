using System;
using System.Collections.Generic;

namespace ChsbKiosk.Core.Proctor;

public sealed class ProctorPolicy
{
	private const int MaxFrameGapMs = 1000;

	private const int CalibrationSamples = 24;

	private const double CenterAdaptMs = 60000.0;

	private const double MaxCenterShift = 0.1;

	private static readonly ProctorCondition[] Tracked = new ProctorCondition[5]
	{
		ProctorCondition.CameraOff,
		ProctorCondition.CameraCovered,
		ProctorCondition.MultipleFaces,
		ProctorCondition.NoFace,
		ProctorCondition.LookingAway
	};

	private readonly ProctorSettings _settings;

	private readonly Dictionary<ProctorCondition, double> _active = new Dictionary<ProctorCondition, double>();

	private long _lastFrameMs = -1L;

	private long _quietUntilMs;

	private double _clearMs;

	private readonly List<double> _calibration = new List<double>(24);

	public bool InAlarm { get; private set; }

	public ProctorCondition AlarmCondition { get; private set; }

	public ProctorCondition LastCondition { get; private set; }

	public double YawCenter { get; private set; } = 0.5;

	public bool IsCalibrated { get; private set; }

	public ProctorPolicy(ProctorSettings settings)
	{
		_settings = settings.Sanitized();
		ProctorCondition[] tracked = Tracked;
		foreach (ProctorCondition key in tracked)
		{
			_active[key] = 0.0;
		}
	}

	public ProctorCondition Classify(FrameAnalysis frame)
	{
		if (!frame.CameraOk)
		{
			return ProctorCondition.CameraOff;
		}
		if (frame.Brightness < _settings.CoveredBrightness || frame.StdDev < _settings.CoveredStdDev)
		{
			return ProctorCondition.CameraCovered;
		}
		if (frame.Faces.Count == 0)
		{
			return ProctorCondition.NoFace;
		}
		FaceDetection faceDetection = frame.Faces[0];
		foreach (FaceDetection face in frame.Faces)
		{
			if (face.Area > faceDetection.Area)
			{
				faceDetection = face;
			}
		}
		foreach (FaceDetection face2 in frame.Faces)
		{
			if (face2 != faceDetection && face2.Score >= 0.7f && (double)face2.Area >= (double)faceDetection.Area * _settings.SecondFaceMinRatio)
			{
				return ProctorCondition.MultipleFaces;
			}
		}
		if (Math.Abs(faceDetection.YawRatio - YawCenter) > _settings.YawThreshold)
		{
			return ProctorCondition.LookingAway;
		}
		if (_settings.PitchDownThreshold < 1.0 && faceDetection.PitchRatio > _settings.PitchDownThreshold)
		{
			return ProctorCondition.LookingAway;
		}
		return ProctorCondition.Ok;
	}

	public ProctorSignal Update(FrameAnalysis frame, long nowMs)
	{
		Calibrate(frame);
		ProctorCondition proctorCondition = (LastCondition = Classify(frame));
		long num = ((_lastFrameMs < 0) ? 0 : Math.Clamp(nowMs - _lastFrameMs, 0L, 1000L));
		_lastFrameMs = nowMs;
		if (proctorCondition == ProctorCondition.Ok)
		{
			AdaptCenter(frame, num);
		}
		if (InAlarm)
		{
			if (proctorCondition == ProctorCondition.Ok)
			{
				_clearMs += num;
				if (_clearMs >= (double)_settings.RecoverMs)
				{
					ProctorCondition alarmCondition = AlarmCondition;
					Reset(nowMs);
					return new ProctorSignal(ProctorSignalKind.Recovered, alarmCondition, 0);
				}
			}
			else
			{
				_clearMs = 0.0;
			}
			return ProctorSignal.None;
		}
		if (nowMs < _quietUntilMs)
		{
			return ProctorSignal.None;
		}
		ProctorCondition[] tracked = Tracked;
		foreach (ProctorCondition proctorCondition3 in tracked)
		{
			double num2 = _active[proctorCondition3];
			if (proctorCondition3 == proctorCondition)
			{
				num2 += (double)num;
			}
			else if (proctorCondition3 == ProctorCondition.LookingAway && proctorCondition == ProctorCondition.NoFace && num2 > 0.0)
			{
				num2 += (double)num;
			}
			else if (!AreRelated(proctorCondition3, proctorCondition))
			{
				num2 = Math.Max(0.0, num2 - (double)num * Decay(proctorCondition3));
			}
			_active[proctorCondition3] = num2;
		}
		if (proctorCondition != ProctorCondition.Ok)
		{
			ProctorCondition proctorCondition4 = ProctorCondition.Ok;
			if (_active[proctorCondition] >= (double)Threshold(proctorCondition))
			{
				proctorCondition4 = proctorCondition;
			}
			else if (proctorCondition == ProctorCondition.NoFace && _active[ProctorCondition.LookingAway] >= (double)Threshold(ProctorCondition.LookingAway))
			{
				proctorCondition4 = ProctorCondition.LookingAway;
			}
			if (proctorCondition4 != ProctorCondition.Ok)
			{
				int durationMs = (int)_active[proctorCondition4];
				InAlarm = true;
				AlarmCondition = proctorCondition4;
				_clearMs = 0.0;
				return new ProctorSignal(ProctorSignalKind.Incident, proctorCondition4, durationMs);
			}
		}
		return ProctorSignal.None;
	}

	public void Reset(long nowMs, int quietMs = 3000)
	{
		InAlarm = false;
		AlarmCondition = ProctorCondition.Ok;
		_clearMs = 0.0;
		_quietUntilMs = nowMs + Math.Max(0, quietMs);
		_lastFrameMs = -1L;
		ProctorCondition[] tracked = Tracked;
		foreach (ProctorCondition key in tracked)
		{
			_active[key] = 0.0;
		}
	}

	private static bool AreRelated(ProctorCondition a, ProctorCondition b)
	{
		if (a != ProctorCondition.LookingAway || b != ProctorCondition.NoFace)
		{
			if (a == ProctorCondition.NoFace)
			{
				return b == ProctorCondition.LookingAway;
			}
			return false;
		}
		return true;
	}

	private double Decay(ProctorCondition condition)
	{
		if (condition != ProctorCondition.LookingAway)
		{
			return 2.0;
		}
		return _settings.LookAwayDecay;
	}

	private void Calibrate(FrameAnalysis frame)
	{
		if (IsCalibrated || !frame.CameraOk || frame.Faces.Count != 1)
		{
			return;
		}
		double yawRatio = frame.Faces[0].YawRatio;
		if (!double.IsNaN(yawRatio) && !(Math.Abs(yawRatio - 0.5) > 0.3))
		{
			_calibration.Add(yawRatio);
			if (_calibration.Count >= 24)
			{
				_calibration.Sort();
				double num = _calibration[6];
				double num2 = _calibration[17];
				YawCenter = ClampCenter((num + num2) / 2.0);
				IsCalibrated = true;
				_calibration.Clear();
			}
		}
	}

	private void AdaptCenter(FrameAnalysis frame, long dt)
	{
		if (!IsCalibrated || dt <= 0 || frame.Faces.Count == 0)
		{
			return;
		}
		FaceDetection faceDetection = frame.Faces[0];
		foreach (FaceDetection face in frame.Faces)
		{
			if (face.Area > faceDetection.Area)
			{
				faceDetection = face;
			}
		}
		double yawRatio = faceDetection.YawRatio;
		if (!double.IsNaN(yawRatio))
		{
			double num = Math.Min(1.0, (double)dt / 60000.0);
			YawCenter = ClampCenter(YawCenter + (yawRatio - YawCenter) * num);
		}
	}

	private static double ClampCenter(double value)
	{
		return Math.Clamp(value, 0.4, 0.6);
	}

	private int Threshold(ProctorCondition condition)
	{
		return condition switch
		{
			ProctorCondition.LookingAway => _settings.LookAwayMs, 
			ProctorCondition.MultipleFaces => _settings.MultiFaceMs, 
			ProctorCondition.NoFace => _settings.NoFaceMs, 
			ProctorCondition.CameraCovered => _settings.CoveredMs, 
			ProctorCondition.CameraOff => _settings.CameraOffMs, 
			_ => int.MaxValue, 
		};
	}
}
