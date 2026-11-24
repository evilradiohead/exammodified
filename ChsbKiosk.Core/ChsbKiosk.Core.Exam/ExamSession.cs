using System;
using System.Collections.Generic;
using System.Linq;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Core.Exam;

public sealed class ExamSession
{
	private sealed class AnswerState
	{
		public string? Selected;

		public int TimeSpentSec;

		public long Version;

		public long SavedVersion;

		public bool Dirty => Version != SavedVersion;
	}

	public static readonly IReadOnlyList<string> OptionKeys = new string[4] { "A", "B", "C", "D" };

	private readonly object _sync = new object();

	private readonly Dictionary<string, AnswerState> _answers = new Dictionary<string, AnswerState>(StringComparer.Ordinal);

	private long _versionCounter;

	public string TestResultId { get; }

	public string TestId { get; }

	public string Title { get; }

	public string TestType { get; }

	public IReadOnlyList<ExamQuestion> Questions { get; }

	public DateTimeOffset StartedAt { get; }

	public int DurationMinutes { get; }

	public int ExtraTimeMinutes { get; private set; }

	public bool IsProctored => string.Equals(TestType, "CHSB", StringComparison.Ordinal);

	public DateTimeOffset EndsAt
	{
		get
		{
			lock (_sync)
			{
				return StartedAt.AddMinutes(DurationMinutes + ExtraTimeMinutes);
			}
		}
	}

	public int AnsweredCount
	{
		get
		{
			lock (_sync)
			{
				return _answers.Values.Count((AnswerState a) => a.Selected != null);
			}
		}
	}

	public int PendingCount
	{
		get
		{
			lock (_sync)
			{
				return _answers.Values.Count((AnswerState a) => a.Dirty);
			}
		}
	}

	private ExamSession(string testResultId, string testId, string title, string testType, IReadOnlyList<ExamQuestion> questions, DateTimeOffset startedAt, int durationMinutes, int extraTimeMinutes)
	{
		TestResultId = testResultId;
		TestId = testId;
		Title = title;
		TestType = testType;
		Questions = questions;
		StartedAt = startedAt;
		DurationMinutes = durationMinutes;
		ExtraTimeMinutes = extraTimeMinutes;
	}

	public static ExamSession FromStart(StartResponse response, IEnumerable<PendingAnswer>? localUnsent = null)
	{
		List<ExamQuestion> questions = response.Test.Questions.OrderBy((QuestionDto q) => q.OrderNumber).Select((QuestionDto q, int index) => new ExamQuestion
		{
			Id = q.Id,
			Number = ((q.OrderNumber > 0) ? q.OrderNumber : (index + 1)),
			SubjectName = q.SubjectName,
			TextHtml = (q.QuestionText ?? ""),
			OptionsHtml = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["A"] = q.OptionA ?? "",
				["B"] = q.OptionB ?? "",
				["C"] = q.OptionC ?? "",
				["D"] = q.OptionD ?? ""
			}
		}).ToList();
		ExamSession examSession = new ExamSession(response.TestResult.Id, response.Test.Id, response.Test.DisplayTitle, response.Test.TestType, questions, response.TestResult.StartedAt, response.Test.Duration, response.TestResult.ExtraTimeMinutes);
		foreach (QuestionDto question in response.Test.Questions)
		{
			examSession._answers[question.Id] = new AnswerState
			{
				Selected = NormalizeAnswer(question.SelectedAnswer),
				TimeSpentSec = Math.Max(0, question.TimeSpentSec)
			};
		}
		if (localUnsent != null)
		{
			foreach (PendingAnswer item in localUnsent)
			{
				if (!examSession._answers.TryGetValue(item.QuestionId, out AnswerState value))
				{
					continue;
				}
				string text = NormalizeAnswer(item.SelectedAnswer);
				if (text != null)
				{
					if (!string.Equals(value.Selected, text, StringComparison.Ordinal))
					{
						value.Selected = text;
						value.Version = ++examSession._versionCounter;
					}
					value.TimeSpentSec = Math.Max(value.TimeSpentSec, item.TimeSpentSec);
				}
			}
		}
		return examSession;
	}

	public string? GetAnswer(string questionId)
	{
		lock (_sync)
		{
			AnswerState value;
			return _answers.TryGetValue(questionId, out value) ? value.Selected : null;
		}
	}

	public bool IsDirty(string questionId)
	{
		lock (_sync)
		{
			AnswerState value;
			return _answers.TryGetValue(questionId, out value) && value.Dirty;
		}
	}

	public bool Select(string questionId, string optionKey)
	{
		string text = NormalizeAnswer(optionKey);
		if (text == null)
		{
			return false;
		}
		lock (_sync)
		{
			if (!_answers.TryGetValue(questionId, out AnswerState value))
			{
				return false;
			}
			if (string.Equals(value.Selected, text, StringComparison.Ordinal))
			{
				return false;
			}
			value.Selected = text;
			value.Version = ++_versionCounter;
			return true;
		}
	}

	public void AddTime(string questionId, int seconds)
	{
		if (seconds <= 0)
		{
			return;
		}
		lock (_sync)
		{
			if (_answers.TryGetValue(questionId, out AnswerState value))
			{
				value.TimeSpentSec = Math.Min(value.TimeSpentSec + seconds, 86400);
			}
		}
	}

	public IReadOnlyList<PendingAnswer> GetPending()
	{
		lock (_sync)
		{
			return (from pair in _answers
				where pair.Value.Dirty
				select new PendingAnswer
				{
					QuestionId = pair.Key,
					SelectedAnswer = pair.Value.Selected,
					TimeSpentSec = pair.Value.TimeSpentSec,
					Version = pair.Value.Version
				}).ToList();
		}
	}

	public void MarkSaved(IEnumerable<PendingAnswer> saved)
	{
		lock (_sync)
		{
			foreach (PendingAnswer item in saved)
			{
				if (_answers.TryGetValue(item.QuestionId, out AnswerState value) && item.Version > value.SavedVersion)
				{
					value.SavedVersion = Math.Min(item.Version, value.Version);
				}
			}
		}
	}

	public bool UpdateExtraTime(int extraTimeMinutes)
	{
		lock (_sync)
		{
			if (extraTimeMinutes == ExtraTimeMinutes || extraTimeMinutes < 0)
			{
				return false;
			}
			ExtraTimeMinutes = extraTimeMinutes;
			return true;
		}
	}

	public static string? NormalizeAnswer(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim().ToUpperInvariant();
		if (!OptionKeys.Contains(text))
		{
			return null;
		}
		return text;
	}
}
