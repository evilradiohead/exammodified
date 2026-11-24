using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;

namespace ChsbKiosk;

public partial class ExitPasswordDialog : Window, IComponentConnector
{
	private readonly KioskConfig _config;

	private ExitPasswordDialog(KioskConfig config)
	{
		_config = config;
		InitializeComponent();
	}

	public static bool TryConfirm(Window owner, KioskConfig config, string? title = null, string? confirmText = null)
	{
		ExitPasswordDialog exitPasswordDialog = new ExitPasswordDialog(config)
		{
			Owner = owner
		};
		if (!string.IsNullOrWhiteSpace(title))
		{
			exitPasswordDialog.TitleText.Text = title;
		}
		if (!string.IsNullOrWhiteSpace(confirmText))
		{
			exitPasswordDialog.ConfirmButton.Content = confirmText;
		}
		return exitPasswordDialog.ShowDialog() == true;
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		PasswordInput.Focus();
	}

	private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			TryExit();
			e.Handled = true;
		}
	}

	private void Exit_Click(object sender, RoutedEventArgs e)
	{
		TryExit();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void TryExit()
	{
		if (_config.VerifyExitPassword(PasswordInput.Password))
		{
			base.DialogResult = true;
			return;
		}
		ErrorText.Visibility = Visibility.Visible;
		PasswordInput.Clear();
		PasswordInput.Focus();
	}
}
