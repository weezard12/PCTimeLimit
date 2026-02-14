using System.Windows;
using System.Windows.Controls;
using PCTimeLimitAdmin.Services;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimitAdmin;

public partial class MainWindow : Window
{
    private TcpClientService? _apiClient;
    private string? _loggedInUsername;
    private List<ComputerDto> _computers = new();
    private ComputerDto? _selectedComputer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ShowLogin();

        if (!string.IsNullOrWhiteSpace(_loggedInUsername))
        {
            UpdateConnectionStatus(false, "Ready to connect");
            StatusText.Text = "Login successful. Click Connect to load computers.";
        }

        await Task.CompletedTask;
    }

    private void ShowLogin()
    {
        var loginWindow = new LoginWindow();
        var result = loginWindow.ShowDialog();

        if (result != true)
        {
            Application.Current.Shutdown();
            return;
        }

        _loggedInUsername = loginWindow.LoggedInUsername;
        UserInfoTextBlock.Text = $"Logged in as: {_loggedInUsername}";

        if (!string.IsNullOrWhiteSpace(loginWindow.CreatedAdminCode))
        {
            AdminCodeTextBlock.Text = $"Your Admin Code: {loginWindow.CreatedAdminCode}";
            AdminCodeTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            AdminCodeTextBlock.Text = string.Empty;
            AdminCodeTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient?.IsConnected == true)
        {
            Disconnect();
            return;
        }

        ConnectButton.Content = "Connecting...";
        ConnectButton.IsEnabled = false;

        try
        {
            _apiClient?.Dispose();
            _apiClient = new TcpClientService();

            var connected = await _apiClient.ConnectAsync();
            if (!connected)
            {
                UpdateConnectionStatus(false, "Unable to establish session");
                ConnectButton.Content = "Connect";
                return;
            }

            var heartbeat = await _apiClient.SendHeartbeatAsync();
            if (!heartbeat)
            {
                UpdateConnectionStatus(false, "Server did not respond");
                ConnectButton.Content = "Connect";
                return;
            }

            UpdateConnectionStatus(true, "Connected");
            ConnectButton.Content = "Disconnect";
            await LoadComputersAsync();
        }
        catch (Exception ex)
        {
            UpdateConnectionStatus(false, $"Connection error: {ex.Message}");
            ConnectButton.Content = "Connect";
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void Disconnect()
    {
        _apiClient?.Dispose();
        _apiClient = null;
        _selectedComputer = null;
        _computers.Clear();
        ComputersDataGrid.ItemsSource = null;
        UpdateConnectionStatus(false, "Disconnected");
        ConnectButton.Content = "Connect";
        StatusText.Text = "Disconnected.";
    }

    private async Task LoadComputersAsync()
    {
        if (_apiClient?.IsConnected != true)
        {
            return;
        }

        StatusText.Text = "Loading computers...";

        var response = await _apiClient.GetComputersForAdminAsync();
        if (response?.Success != true)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(response?.Message)
                ? "Failed to load computers."
                : response!.Message;
            return;
        }

        _computers = response.Computers;
        ComputersDataGrid.ItemsSource = _computers;
        ComputersDataGrid.Items.Refresh();
        StatusText.Text = $"Loaded {_computers.Count} computers";
    }

    private void ComputersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedComputer = ComputersDataGrid.SelectedItem as ComputerDto;
        if (_selectedComputer is null)
        {
            SelectedComputerText.Text = "None";
            UpdateTimeLimitButton.IsEnabled = false;
            ResetTimerButton.IsEnabled = false;
            SetZeroButton.IsEnabled = false;
            UpdateAllowedUsageButton.IsEnabled = false;
            return;
        }

        SelectedComputerText.Text = _selectedComputer.ComputerName;
        HoursTextBox.Text = ((int)_selectedComputer.DailyTimeLimit.TotalHours).ToString();
        MinutesTextBox.Text = _selectedComputer.DailyTimeLimit.Minutes.ToString();

        UpdateTimeLimitButton.IsEnabled = true;
        ResetTimerButton.IsEnabled = true;
        SetZeroButton.IsEnabled = true;
        UpdateAllowedUsageButton.IsEnabled = true;
    }

    private async void UpdateTimeLimitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComputer is null || _apiClient?.IsConnected != true)
        {
            return;
        }

        if (!int.TryParse(HoursTextBox.Text, out var hours) || hours < 0)
        {
            MessageBox.Show("Please enter valid hours (0+).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MinutesTextBox.Text, out var minutes) || minutes < 0 || minutes > 59)
        {
            MessageBox.Show("Please enter valid minutes (0-59).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var timeLimit = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        var response = await _apiClient.SetComputerTimeLimitAsync(_selectedComputer.ComputerId, timeLimit);

        if (response?.Success != true)
        {
            MessageBox.Show(response?.Message ?? "Failed to update time limit.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _selectedComputer.DailyTimeLimit = timeLimit;
        ComputersDataGrid.Items.Refresh();
        StatusText.Text = $"Updated time limit for {_selectedComputer.ComputerName}.";
    }

    private async void UpdateAllowedUsageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComputer is null || _apiClient?.IsConnected != true)
        {
            return;
        }

        UpdateAllowedUsageButton.IsEnabled = false;
        try
        {
            var current = await _apiClient.GetComputerAllowedUsageAsync(_selectedComputer.ComputerId);
            if (current?.Success != true)
            {
                MessageBox.Show(
                    current?.Message ?? "Failed to load current schedule from server.",
                    "Schedule Load Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var editor = new AllowedUsageWindow(current.Schedule);
            editor.Owner = this;
            var ok = editor.ShowDialog();
            if (ok != true)
            {
                return;
            }

            var response = await _apiClient.SetComputerAllowedUsageAsync(_selectedComputer.ComputerId, editor.ResultRanges);

            if (response?.Success != true)
            {
                MessageBox.Show(
                    response?.Message ?? "Failed to update allowed usage.",
                    "Schedule Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _selectedComputer.AllowedUsageSchedule = response.Schedule;
            ComputersDataGrid.Items.Refresh();
            StatusText.Text = $"Updated allowed usage schedule for {_selectedComputer.ComputerName}.";
        }
        finally
        {
            UpdateAllowedUsageButton.IsEnabled = _selectedComputer is not null;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadComputersAsync();
    }

    private async void ResetTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComputer is null || _apiClient?.IsConnected != true)
        {
            return;
        }

        ResetTimerButton.IsEnabled = false;
        try
        {
            var response = await _apiClient.ResetComputerTimerAsync(_selectedComputer.ComputerId);
            if (response?.Success == true)
            {
                StatusText.Text = $"Reset queued for {_selectedComputer.ComputerName}.";
                await LoadComputersAsync();
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Failed to queue reset.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            ResetTimerButton.IsEnabled = _selectedComputer is not null;
        }
    }

    private async void SetZeroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComputer is null || _apiClient?.IsConnected != true)
        {
            return;
        }

        SetZeroButton.IsEnabled = false;
        try
        {
            var response = await _apiClient.ForceLockoutAsync(_selectedComputer.ComputerId);
            if (response?.Success == true)
            {
                StatusText.Text = $"Force lockout queued for {_selectedComputer.ComputerName}.";
                await LoadComputersAsync();
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Failed to queue force lockout.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetZeroButton.IsEnabled = _selectedComputer is not null;
        }
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is not null)
        {
            await _apiClient.LogoutAsync();
        }

        Disconnect();
        _loggedInUsername = null;
        ShowLogin();
        UpdateConnectionStatus(false, "Ready to connect");
    }

    private void UpdateConnectionStatus(bool isConnected, string status)
    {
        ConnectionStatusText.Text = status;
        ConnectionStatusText.Foreground = isConnected
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Gray;
    }
}
