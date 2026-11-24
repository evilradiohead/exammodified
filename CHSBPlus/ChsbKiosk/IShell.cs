using System;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk;

public interface IShell
{
	AppServices Services { get; }

	event Action? FocusLost;

	void ShowLogin(string? message = null);

	void ShowTests(TestsResponse? preloaded = null);

	void ShowExam(StartResponse start, CameraMode camera);

	void ShowSurvey(string surveyId, string? testResultId);

	void ShowFinished(string title, string message, string? testResultId = null, bool offerSurvey = true);

	void SetConnection(bool online);

	bool ConfirmSupervisor(string title, string confirmText);
}
