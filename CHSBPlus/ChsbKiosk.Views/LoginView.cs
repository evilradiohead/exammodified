using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Views;

public partial class LoginView : UserControl, IComponentConnector
{
	private readonly IShell _shell;

	private bool _busy;

	public LoginView(IShell shell, string? notice)
	{
		_shell = shell;
		InitializeComponent();
		OrganizationText.Text = "Ixtisoslashtirilgan ta'lim muassasalari agentligi";
		if (!string.IsNullOrWhiteSpace(notice))
		{
			NoticeText.Text = notice;
			NoticePanel.Visibility = Visibility.Visible;
		}
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		UsernameBox.Focus();
	}

	private void OnInputKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			if (sender == UsernameBox && PasswordInput.Password.Length == 0)
			{
				PasswordInput.Focus();
			}
			else
			{
				LoginAsync();
			}
		}
	}

	private void OnLoginClick(object sender, RoutedEventArgs e)
	{
		LoginAsync();
	}

	private async Task LoginAsync()
	{
		if (_busy)
		{
			return;
		}
		string username = UsernameBox.Text.Trim();
		string password = PasswordInput.Password;
		if (username.Length == 0 || password.Length == 0)
		{
			ShowError("Login va parolni kiriting");
			return;
		}
		SetBusy(busy: true);
		try
		{
			ApiResult<LoginResponse> apiResult = await _shell.Services.Api.LoginAsync(username, password);
			_shell.SetConnection(!apiResult.IsNetworkError);
			if (!apiResult.Ok)
			{
				PasswordInput.Clear();
				ShowError(apiResult.ErrorOr("Kirib bo'lmadi"));
				PasswordInput.Focus();
			}
			else
			{
				_shell.Services.PersistSession(username);
				PasswordInput.Clear();
				_shell.ShowTests();
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "Login xatosi");
			ShowError("Kutilmagan xato. Qayta urinib ko'ring.");
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		LoginButton.IsEnabled = !busy;
		LoginButton.Content = (busy ? "Tekshirilmoqda..." : "Kirish");
		UsernameBox.IsEnabled = !busy;
		PasswordInput.IsEnabled = !busy;
		if (busy)
		{
			ErrorPanel.Visibility = Visibility.Collapsed;
		}
	}

	private void ShowError(string message)
	{
		ErrorText.Text = message;
		ErrorPanel.Visibility = Visibility.Visible;
	}
}
