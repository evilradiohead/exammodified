using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Views;

public partial class SurveyView : UserControl, IComponentConnector
{
	private readonly IShell _shell;

	private readonly string _surveyId;

	private readonly string? _testResultId;

	private readonly Dictionary<string, Func<SurveyAnswerInput?>> _readers = new Dictionary<string, Func<SurveyAnswerInput>>(StringComparer.Ordinal);

	private SurveyDto? _survey;

	private bool _busy;

	public SurveyView(IShell shell, string surveyId, string? testResultId)
	{
		_shell = shell;
		_surveyId = surveyId;
		_testResultId = testResultId;
		InitializeComponent();
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		base.Loaded -= OnLoaded;
		try
		{
			await LoadAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "So'rovnomani yuklashda xato");
			StatusText.Text = "So'rovnomani yuklab bo'lmadi.";
		}
	}

	private async Task LoadAsync()
	{
		ApiResult<SurveyResponse> apiResult = await _shell.Services.Api.GetSurveyAsync(_surveyId, _testResultId);
		_shell.SetConnection(!apiResult.IsNetworkError);
		if (apiResult.IsUnauthorized)
		{
			_shell.Services.ForgetSession();
			_shell.ShowLogin("Sessiya tugagan. Qayta kiring.");
			return;
		}
		if (!apiResult.Ok || apiResult.Value?.Survey == null || apiResult.Value.Survey.Questions.Count == 0)
		{
			StatusText.Text = apiResult.ErrorOr("So'rovnoma topilmadi.");
			return;
		}
		_survey = apiResult.Value.Survey;
		if (_survey.RemainingAttempts <= 0)
		{
			Leave("Rahmat!", "Siz bu so'rovnomada allaqachon qatnashgansiz.");
			return;
		}
		TitleText.Text = _survey.Title;
		if (!string.IsNullOrWhiteSpace(_survey.Description))
		{
			DescriptionText.Text = _survey.Description;
		}
		StatusText.Visibility = Visibility.Collapsed;
		foreach (SurveyQuestionDto item in _survey.Questions.OrderBy((SurveyQuestionDto q) => q.OrderNumber))
		{
			QuestionsPanel.Children.Add(BuildQuestion(item));
		}
		SubmitButton.IsEnabled = true;
	}

	private Border BuildQuestion(SurveyQuestionDto question)
	{
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"{question.OrderNumber}. {question.Text}",
			Style = (Style)FindResource("Text.Base"),
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
		});
		if (question.IsText)
		{
			TextBox box = new TextBox
			{
				Style = (Style)FindResource("Input"),
				AcceptsReturn = true,
				TextWrapping = TextWrapping.Wrap,
				MinHeight = 96.0,
				MaxLength = 2000,
				Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
				VerticalContentAlignment = VerticalAlignment.Top,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
			stackPanel.Children.Add(box);
			_readers[question.Id] = delegate
			{
				string text = box.Text.Trim();
				return (text.Length != 0) ? new SurveyAnswerInput
				{
					QuestionId = question.Id,
					TextValue = text
				} : null;
			};
		}
		else
		{
			List<(RadioButton Button, string OptionId)> buttons = new List<(RadioButton, string)>();
			foreach (SurveyOptionDto item in question.Options.OrderBy((SurveyOptionDto o) => o.OrderIndex))
			{
				RadioButton radioButton = new RadioButton
				{
					GroupName = "survey-" + question.Id,
					Style = (Style)FindResource("Radio.Option"),
					Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
					Cursor = Cursors.Hand,
					Content = new TextBlock
					{
						Text = (string.IsNullOrWhiteSpace(item.Label) ? item.Text : (item.Label + ") " + item.Text)),
						Style = (Style)FindResource("Text.Base")
					}
				};
				buttons.Add((radioButton, item.Id));
				stackPanel.Children.Add(radioButton);
			}
			_readers[question.Id] = delegate
			{
				foreach (var (radioButton2, optionId) in buttons)
				{
					if (radioButton2.IsChecked == true)
					{
						return new SurveyAnswerInput
						{
							QuestionId = question.Id,
							OptionId = optionId
						};
					}
				}
				return (SurveyAnswerInput?)null;
			};
		}
		return new Border
		{
			Style = (Style)FindResource("Card"),
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0),
			Child = stackPanel
		};
	}

	private void OnSubmitClick(object sender, RoutedEventArgs e)
	{
		SubmitAsync();
	}

	private void OnSkipClick(object sender, RoutedEventArgs e)
	{
		Leave("Test topshirildi", "Javoblaringiz qabul qilindi.");
	}

	private async Task SubmitAsync()
	{
		if (_busy || _survey == null)
		{
			return;
		}
		List<SurveyAnswerInput> list = new List<SurveyAnswerInput>();
		foreach (SurveyQuestionDto item in _survey.Questions.OrderBy((SurveyQuestionDto q) => q.OrderNumber))
		{
			Func<SurveyAnswerInput> value;
			SurveyAnswerInput surveyAnswerInput = (_readers.TryGetValue(item.Id, out value) ? value() : null);
			if (surveyAnswerInput == null)
			{
				ErrorText.Text = $"{item.OrderNumber}-savolga javob bering";
				return;
			}
			list.Add(surveyAnswerInput);
		}
		_busy = true;
		ErrorText.Text = "";
		SubmitButton.IsEnabled = false;
		SkipButton.IsEnabled = false;
		SubmitButton.Content = "Yuborilmoqda...";
		try
		{
			ApiResult<Empty> apiResult = await _shell.Services.Api.SubmitSurveyAsync(_surveyId, _testResultId, list);
			_shell.SetConnection(!apiResult.IsNetworkError);
			if (apiResult.Ok)
			{
				Leave("Rahmat!", "So'rovnoma javoblaringiz qabul qilindi.");
			}
			else if (apiResult.IsUnauthorized)
			{
				_shell.Services.ForgetSession();
				_shell.ShowLogin("Sessiya tugagan. Qayta kiring.");
			}
			else
			{
				ErrorText.Text = apiResult.ErrorOr("Javoblarni yuborib bo'lmadi");
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "So'rovnomani yuborishda xato");
			ErrorText.Text = "Kutilmagan xato. Qayta urinib ko'ring.";
		}
		finally
		{
			_busy = false;
			SubmitButton.IsEnabled = true;
			SkipButton.IsEnabled = true;
			SubmitButton.Content = "Javoblarni yuborish";
		}
	}

	private void Leave(string title, string message)
	{
		_shell.ShowFinished(title, message, _testResultId, offerSurvey: false);
	}
}
