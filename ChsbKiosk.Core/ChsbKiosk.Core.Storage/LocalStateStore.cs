using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ChsbKiosk.Core.Storage;

public sealed class LocalStateStore
{
	private static readonly TimeSpan AttemptRetention = TimeSpan.FromDays(3);

	private static readonly TimeSpan TokenRetention = TimeSpan.FromHours(6);

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly string _path;

	private readonly IDataProtector _protector;

	private readonly object _sync = new object();

	private LocalStateData _data = new LocalStateData();

	public string? AuthToken
	{
		get
		{
			lock (_sync)
			{
				return _data.AuthToken;
			}
		}
	}

	public string? StudentName
	{
		get
		{
			lock (_sync)
			{
				return _data.StudentName;
			}
		}
	}

	public bool CameraSeen
	{
		get
		{
			lock (_sync)
			{
				return _data.CameraSeen;
			}
		}
	}

	public LocalStateStore(string path, IDataProtector protector)
	{
		_path = path;
		_protector = protector;
	}

	public void Load(DateTimeOffset now)
	{
		lock (_sync)
		{
			_data = ReadFile(_path) ?? ReadFile(_path + ".bak") ?? new LocalStateData();
			LocalStateData data = _data;
			if (data.Attempts == null)
			{
				Dictionary<string, AttemptLocalState> dictionary = (data.Attempts = new Dictionary<string, AttemptLocalState>(StringComparer.Ordinal));
			}
			foreach (string item in (from pair in _data.Attempts
				where pair.Value == null || now - pair.Value.UpdatedAt > AttemptRetention
				select pair.Key).ToList())
			{
				_data.Attempts.Remove(item);
			}
			DateTimeOffset? tokenSavedAt = _data.TokenSavedAt;
			if (tokenSavedAt.HasValue)
			{
				DateTimeOffset valueOrDefault = tokenSavedAt.GetValueOrDefault();
				if (now - valueOrDefault > TokenRetention)
				{
					_data.AuthToken = null;
					_data.StudentName = null;
					_data.TokenSavedAt = null;
				}
			}
		}
	}

	public void SetAuth(string? token, string? studentName, DateTimeOffset now)
	{
		lock (_sync)
		{
			_data.AuthToken = token;
			_data.StudentName = ((token == null) ? null : studentName);
			_data.TokenSavedAt = ((token == null) ? ((DateTimeOffset?)null) : new DateTimeOffset?(now));
			SaveLocked();
		}
	}

	public void MarkCameraSeen()
	{
		lock (_sync)
		{
			if (!_data.CameraSeen)
			{
				_data.CameraSeen = true;
				SaveLocked();
			}
		}
	}

	public AttemptLocalState GetAttempt(string testResultId)
	{
		lock (_sync)
		{
			AttemptLocalState value;
			return _data.Attempts.TryGetValue(testResultId, out value) ? Clone(value) : new AttemptLocalState();
		}
	}

	public void UpdateAttempt(string testResultId, Action<AttemptLocalState> update)
	{
		lock (_sync)
		{
			if (!_data.Attempts.TryGetValue(testResultId, out AttemptLocalState value))
			{
				value = new AttemptLocalState();
				_data.Attempts[testResultId] = value;
			}
			update(value);
			value.UpdatedAt = DateTimeOffset.UtcNow;
			SaveLocked();
		}
	}

	public void RemoveAttempt(string testResultId)
	{
		lock (_sync)
		{
			if (_data.Attempts.Remove(testResultId))
			{
				SaveLocked();
			}
		}
	}

	private static AttemptLocalState Clone(AttemptLocalState state)
	{
		return new AttemptLocalState
		{
			Pending = state.Pending.Select((LocalAnswer a) => new LocalAnswer
			{
				QuestionId = a.QuestionId,
				SelectedAnswer = a.SelectedAnswer,
				TimeSpentSec = a.TimeSpentSec
			}).ToList(),
			WarningsUsed = state.WarningsUsed,
			CameraWaived = state.CameraWaived,
			PendingViolationType = state.PendingViolationType,
			LastQuestionIndex = state.LastQuestionIndex,
			UpdatedAt = state.UpdatedAt
		};
	}

	private LocalStateData? ReadFile(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			byte[] array = File.ReadAllBytes(path);
			if (array.Length == 0)
			{
				return null;
			}
			return JsonSerializer.Deserialize<LocalStateData>(_protector.Unprotect(array), Json);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void SaveLocked()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(_path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			byte[] plain = JsonSerializer.SerializeToUtf8Bytes(_data, Json);
			byte[] array = _protector.Protect(plain);
			string text = _path + ".tmp";
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(_path))
			{
				File.Replace(text, _path, _path + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(text, _path);
			}
		}
		catch (Exception)
		{
		}
	}
}
