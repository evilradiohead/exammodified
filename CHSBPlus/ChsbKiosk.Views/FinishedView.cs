using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Views;

public partial class FinishedView : UserControl, IDisposable, IComponentConnector
{
	private const int AutoLogoutSeconds = 120;

	private static readonly Brush TrackBrush = Frozen(241, 245, 249);

	private static readonly Brush GoodBrush = Frozen(22, 163, 74);

	private static readonly Brush MiddleBrush = Frozen(27, 41, 75);

	private static readonly Brush LowBrush = Frozen(217, 119, 6);

	private static readonly Brush BadBrush = Frozen(239, 68, 68);

	private readonly IShell _shell;

	private readonly string? _testResultId;

	private readonly bool _offerSurvey;

	private readonly DispatcherTimer _timer;

	private PendingSurveyInfo? _survey;

	private int _secondsLeft = 120;

	private bool _leaving;

	private bool _disposed;

	public FinishedView(IShell shell, string title, string message, string? testResultId = null, bool offerSurvey = true)
	{
		_shell = shell;
		_testResultId = testResultId;
		_offerSurvey = offerSurvey;
		InitializeComponent();
		TitleText.Text = title;
		MessageText.Text = message;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_timer.Tick += delegate
		{
			_secondsLeft--;
			UpdateCountdown();
			if (_secondsLeft <= 0)
			{
				LeaveAsync();
			}
		};
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		base.Loaded -= OnLoaded;
		UpdateCountdown();
		_timer.Start();
		try
		{
			await LoadAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Yakuniy ekranni yuklashda xato");
			LoadingPanel.Visibility = Visibility.Collapsed;
		}
		if (!_disposed && !_leaving)
		{
			_secondsLeft = 120;
			UpdateCountdown();
			_timer.Start();
		}
	}

	private async Task LoadAsync()
	{
		if (!string.IsNullOrEmpty(_testResultId))
		{
			LoadingPanel.Visibility = Visibility.Visible;
			ResultResponse resultResponse = await FetchResultAsync(_testResultId);
			if (_disposed)
			{
				return;
			}
			LoadingPanel.Visibility = Visibility.Collapsed;
			if (resultResponse != null)
			{
				ShowResult(resultResponse);
			}
		}
		if (_offerSurvey)
		{
			await LoadSurveyAsync();
		}
	}

	private async Task<ResultResponse?> FetchResultAsync(string testResultId)
	{
		for (int attempt = 0; attempt < 3; attempt++)
		{
			if (_disposed)
			{
				break;
			}
			ApiResult<ResultResponse> apiResult = await _shell.Services.Api.GetResultAsync(testResultId);
			if (_disposed)
			{
				return null;
			}
			_shell.SetConnection(!apiResult.IsNetworkError);
			if (apiResult.Ok)
			{
				ResultResponse value = apiResult.Value;
				if (value != null && value.Completed)
				{
					return value;
				}
			}
			int num;
			if (!apiResult.IsTransient)
			{
				if (apiResult.Ok)
				{
					ResultResponse value2 = apiResult.Value;
					num = ((value2 != null && !value2.Completed) ? 1 : 0);
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 1;
			}
			if (num == 0)
			{
				return null;
			}
			await Task.Delay(TimeSpan.FromSeconds(2L));
		}
		return null;
	}

	private void ShowResult(ResultResponse result)
	{
		if (!string.IsNullOrWhiteSpace(result.Title))
		{
			TestTitleText.Text = result.Title;
			TestTitleText.Visibility = Visibility.Visible;
		}
		int value = Math.Max(0, result.TotalQuestions);
		ScoreText.Text = result.Correct.ToString(CultureInfo.InvariantCulture);
		ScoreOfText.Text = $"{value} ta savoldan";
		PercentText.Text = FormatPercent(result.Percent);
		CorrectText.Text = $"{result.Correct} ta";
		IncorrectText.Text = $"{result.Incorrect} ta";
		UnansweredText.Text = $"{result.Unanswered} ta";
		List<SubjectResult> list = result.Subjects ?? new List<SubjectResult>();
		SubjectsPanel.Children.Clear();
		foreach (SubjectResult item in list)
		{
			SubjectsPanel.Children.Add(BuildSubjectRow(item));
		}
		if (list.Count == 0)
		{
			SubjectsBlock.Visibility = Visibility.Collapsed;
			SubjectsColumn.Width = new GridLength(0.0);
		}
		ResultPanel.Visibility = Visibility.Visible;
		NoteText.Visibility = Visibility.Visible;
	}

	private Border BuildSubjectRow(SubjectResult subject)
	{
		double num = Math.Clamp(subject.Percent, 0.0, 100.0);
		Brush brush = ((num >= 86.0) ? GoodBrush : ((num >= 66.0) ? MiddleBrush : ((num >= 46.0) ? LowBrush : BadBrush)));
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock element = new TextBlock
		{
			Text = subject.Name,
			Style = (Style)FindResource("Text.Base"),
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.NoWrap,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element, 0);
		TextBlock element2 = new TextBlock
		{
			Text = $"{subject.Correct} / {subject.Total} ta to'g'ri",
			Style = (Style)FindResource("Text.Muted"),
			Margin = new Thickness(12.0, 0.0, 12.0, 0.0),
			TextWrapping = TextWrapping.NoWrap,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element2, 1);
		TextBlock element3 = new TextBlock
		{
			Text = FormatPercent(num),
			Style = (Style)FindResource("Text.Base"),
			FontWeight = FontWeights.Bold,
			Foreground = brush,
			MinWidth = 52.0,
			TextAlignment = TextAlignment.Right,
			TextWrapping = TextWrapping.NoWrap,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element3, 2);
		grid.Children.Add(element);
		grid.Children.Add(element2);
		grid.Children.Add(element3);
		Grid grid2 = new Grid();
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(num, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(100.0 - num, GridUnitType.Star)
		});
		Border element4 = new Border
		{
			Background = brush,
			CornerRadius = new CornerRadius(3.0)
		};
		Grid.SetColumn(element4, 0);
		grid2.Children.Add(element4);
		Border element5 = new Border
		{
			Height = 6.0,
			CornerRadius = new CornerRadius(3.0),
			Background = TrackBrush,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Child = grid2
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(element5);
		return new Border
		{
			Style = (Style)FindResource("Card"),
			Padding = new Thickness(14.0, 11.0, 14.0, 12.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Child = stackPanel
		};
	}

	private static string FormatPercent(double value)
	{
		double num = Math.Round(value, 1);
		if (!(Math.Abs(num - Math.Round(num)) < 0.05))
		{
			return num.ToString("0.0", CultureInfo.InvariantCulture) + "%";
		}
		return $"{Math.Round(num):0}%";
	}

	private async Task LoadSurveyAsync()
	{
		try
		{
			ApiResult<TestsResponse> apiResult = await _shell.Services.Api.GetTestsAsync();
			if (!_disposed && apiResult.Ok && apiResult.Value?.PendingSurvey != null)
			{
				_survey = apiResult.Value.PendingSurvey;
				SurveyButtonText.Text = "So'rovnoma: " + _survey.Title;
				SurveyButton.Visibility = Visibility.Visible;
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "So'rovnomani tekshirishda xato");
		}
	}

	private void UpdateCountdown()
	{
		CountdownText.Text = $"{Math.Max(0, _secondsLeft)} soniyadan keyin hisobdan avtomatik chiqiladi.";
	}

	private void OnSurveyClick(object sender, RoutedEventArgs e)
	{
		if (_survey != null && !_leaving)
		{
			_timer.Stop();
			_shell.ShowSurvey(_survey.Id, _survey.TestResultId);
		}
	}

	private void OnExitClick(object sender, RoutedEventArgs e)
	{
		LeaveAsync();
	}

	private async Task LeaveAsync()
	{
		if (_leaving)
		{
			return;
		}
		_leaving = true;
		_timer.Stop();
		ExitButton.IsEnabled = false;
		SurveyButton.IsEnabled = false;
		try
		{
			await _shell.Services.Api.LogoutAsync();
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Logout xatosi");
		}
		try
		{
			_shell.Services.ForgetSession();
			_shell.ShowLogin();
		}
		catch (Exception exception2)
		{
			Logger.Error(exception2, "Kirish ekraniga o'tishda xato");
			if (!_disposed)
			{
				_leaving = false;
				ExitButton.IsEnabled = true;
				SurveyButton.IsEnabled = true;
				_secondsLeft = 15;
				UpdateCountdown();
				_timer.Start();
			}
		}
	}

	private static Brush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	public void Dispose()
	{
		_disposed = true;
		_timer.Stop();
	}
}
