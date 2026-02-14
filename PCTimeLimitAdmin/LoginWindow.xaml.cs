using System.Windows;
using PCTimeLimitAdmin.Configuration;
using PCTimeLimitAdmin.Services;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimitAdmin;

public partial class LoginWindow : Window
{
    private TcpClientService? _apiClient;

    public string? LoggedInUsername { get; private set; }
    public string? CreatedAdminCode { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += LoginWindow_Loaded;
    }

    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ServerInfoTextBlock.Text = $"Server: {ServerConfig.GetApiBaseUrl()}";
        UsernameTextBox.Focus();
    }

    private async void CreateAccountButton_Click(object sender, RoutedEventArgs e)
    {
        await AuthenticateAsync(createAccount: true);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await AuthenticateAsync(createAccount: false);
    }

    private async Task AuthenticateAsync(bool createAccount)
    {
        if (!ValidateInputs())
        {
            return;
        }

        SetBusyState(true);
        SetStatus(createAccount ? "Creating account..." : "Logging in...", false);

        try
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;

            _apiClient = new TcpClientService();
            TokenResponse? response = createAccount
                ? await _apiClient.CreateAccountAsync(username, password)
                : await _apiClient.LoginAsync(username, password);

            if (response?.Success == true)
            {
                LoggedInUsername = response.Username;
                CreatedAdminCode = response.AdminCode;
                SetStatus(createAccount ? "Account created successfully." : "Login successful.", true);

                await Task.Delay(400);
                DialogResult = true;
                Close();
                return;
            }

            var message = response?.Message;
            SetStatus(string.IsNullOrWhiteSpace(message) ? "Authentication failed." : message, false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            SetStatus("Please enter username", false);
            UsernameTextBox.Focus();
            return false;
        }

        if (UsernameTextBox.Text.Length < ServerConfig.MinUsernameLength)
        {
            SetStatus($"Username must be at least {ServerConfig.MinUsernameLength} characters.", false);
            UsernameTextBox.Focus();
            return false;
        }

        if (UsernameTextBox.Text.Length > ServerConfig.MaxUsernameLength)
        {
            SetStatus($"Username must be no more than {ServerConfig.MaxUsernameLength} characters.", false);
            UsernameTextBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            SetStatus("Please enter password", false);
            PasswordBox.Focus();
            return false;
        }

        if (PasswordBox.Password.Length < ServerConfig.MinPasswordLength)
        {
            SetStatus($"Password must be at least {ServerConfig.MinPasswordLength} characters.", false);
            PasswordBox.Focus();
            return false;
        }

        if (PasswordBox.Password.Length > ServerConfig.MaxPasswordLength)
        {
            SetStatus($"Password must be no more than {ServerConfig.MaxPasswordLength} characters.", false);
            PasswordBox.Focus();
            return false;
        }

        return true;
    }

    private void SetBusyState(bool isBusy)
    {
        CreateAccountButton.IsEnabled = !isBusy;
        LoginButton.IsEnabled = !isBusy;
    }

    private void SetStatus(string message, bool isSuccess)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isSuccess
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;
    }

    protected override void OnClosed(EventArgs e)
    {
        _apiClient?.Dispose();
        base.OnClosed(e);
    }
}
