using System.Windows;
using PCTimeLimitAdmin.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System;

namespace PCTimeLimitAdmin;

public partial class MainWindow : Window
{
    private TcpClientService? _tcpClient;
    private string? _loggedInUsername;
    private List<ComputerInfo> _computers = new();
    private ComputerInfo? _selectedComputer;
    
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }
    
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Show login window first
        var loginWindow = new LoginWindow();
        var result = loginWindow.ShowDialog();
        
        if (result == true)
        {
            _loggedInUsername = loginWindow.LoggedInUsername;
            UserInfoTextBlock.Text = $"Logged in as: {_loggedInUsername}";
            UpdateConnectionStatus(false, "Ready to connect");
            
            // Check if user is admin
            if (await CheckIfAdminAsync(_loggedInUsername))
            {
                StatusText.Text = "Admin account detected. You can manage computers.";
            }
            else
            {
                StatusText.Text = "Regular user account. Limited functionality available.";
            }
        }
        else
        {
            // User cancelled login, close the application
            Application.Current.Shutdown();
        }
    }
    
    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tcpClient?.IsConnected == true)
        {
            // Disconnect
            _tcpClient.Disconnect();
            _tcpClient = null;
            UpdateConnectionStatus(false, "Disconnected");
            ConnectButton.Content = "Connect";
            ComputersDataGrid.ItemsSource = null;
            _computers.Clear();
        }
        else
        {
            // Connect
            ConnectButton.Content = "Connecting...";
            ConnectButton.IsEnabled = false;
            
            try
            {
                _tcpClient = new TcpClientService();
                var connected = await _tcpClient.ConnectAsync();
                
                if (connected)
                {
                    UpdateConnectionStatus(true, "Connected to server");
                    ConnectButton.Content = "Disconnect";
                    
                    // Send heartbeat to test connection
                    var response = await _tcpClient.SendHeartbeatAsync();
                    if (response?.Success == true)
                    {
                        UpdateConnectionStatus(true, "Connected and responding");
                        
                        // Load computers for this admin
                        await LoadComputersAsync();
                    }
                    else
                    {
                        UpdateConnectionStatus(false, "Connected but not responding");
                    }
                }
                else
                {
                    UpdateConnectionStatus(false, "Failed to connect");
                    ConnectButton.Content = "Connect";
                }
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
    }
    
    private async Task<bool> CheckIfAdminAsync(string username)
    {
        if (_tcpClient?.IsConnected != true) return false;
        
        try
        {
            // For now, we'll assume all users are admins
            // In a real implementation, you'd check the user's role
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private async Task LoadComputersAsync()
    {
        if (_tcpClient?.IsConnected != true || string.IsNullOrEmpty(_loggedInUsername)) return;
        
        try
        {
            StatusText.Text = "Loading computers...";
            
            var request = new
            {
                Type = 7, // GetComputersForAdmin
                Data = new
                {
                    AdminUsername = _loggedInUsername
                }
            };
            
            var response = await _tcpClient.SendMessageAsync(request);
            if (response?.Success == true && response.Data != null)
            {
                // Parse computers from response
                var computersJson = response.Data.ToString();
                var computers = System.Text.Json.JsonSerializer.Deserialize<List<ComputerInfo>>(computersJson);
                if (computers != null)
                {
                    _computers = computers;
                    ComputersDataGrid.ItemsSource = _computers;
                    StatusText.Text = $"Loaded {_computers.Count} computers";
                }
            }
            else
            {
                StatusText.Text = "Failed to load computers";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading computers: {ex.Message}";
        }
    }
    
    private void ComputersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedComputer = ComputersDataGrid.SelectedItem as ComputerInfo;
        if (_selectedComputer != null)
        {
            SelectedComputerText.Text = _selectedComputer.ComputerName;
            
            // Parse current time limit
            var timeLimit = _selectedComputer.DailyTimeLimit;
            HoursTextBox.Text = ((int)timeLimit.TotalHours).ToString();
            MinutesTextBox.Text = timeLimit.Minutes.ToString();
            
            UpdateTimeLimitButton.IsEnabled = true;
        }
        else
        {
            SelectedComputerText.Text = "None";
            UpdateTimeLimitButton.IsEnabled = false;
        }
    }
    
    private async void UpdateTimeLimitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComputer == null || _tcpClient?.IsConnected != true) return;
        
        try
        {
            if (!int.TryParse(HoursTextBox.Text, out var hours) || hours < 0)
            {
                MessageBox.Show("Please enter a valid number of hours (0 or greater).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (!int.TryParse(MinutesTextBox.Text, out var minutes) || minutes < 0 || minutes > 59)
            {
                MessageBox.Show("Please enter a valid number of minutes (0-59).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var timeLimit = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            
            var request = new
            {
                Type = 6, // SetComputerTimeLimit
                Data = new
                {
                    ComputerId = _selectedComputer.ComputerId,
                    DailyTimeLimit = timeLimit,
                    AdminUsername = _loggedInUsername
                }
            };
            
            var response = await _tcpClient.SendMessageAsync(request);
            if (response?.Success == true)
            {
                // Update local data
                _selectedComputer.DailyTimeLimit = timeLimit;
                ComputersDataGrid.Items.Refresh();
                
                StatusText.Text = $"Updated time limit for {_selectedComputer.ComputerName} to {timeLimit}";
                MessageBox.Show($"Time limit updated successfully for {_selectedComputer.ComputerName}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = $"Failed to update time limit: {response?.ErrorMessage}";
                MessageBox.Show($"Failed to update time limit: {response?.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error updating time limit: {ex.Message}";
            MessageBox.Show($"Error updating time limit: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadComputersAsync();
    }
    
    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _tcpClient?.Disconnect();
        _tcpClient = null;
        _loggedInUsername = null;
        _computers.Clear();
        ComputersDataGrid.ItemsSource = null;
        
        // Show login window again
        var loginWindow = new LoginWindow();
        var result = loginWindow.ShowDialog();
        
        if (result == true)
        {
            _loggedInUsername = loginWindow.LoggedInUsername;
            UserInfoTextBlock.Text = $"Logged in as: {_loggedInUsername}";
            UpdateConnectionStatus(false, "Ready to connect");
        }
        else
        {
            Application.Current.Shutdown();
        }
    }
    
    private void UpdateConnectionStatus(bool isConnected, string status)
    {
        ConnectionStatusText.Text = status;
        ConnectionStatusText.Foreground = isConnected ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
    }
}

public class ComputerInfo
{
    public string ComputerId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string AdminUsername { get; set; } = "";
    public TimeSpan DailyTimeLimit { get; set; } = TimeSpan.FromHours(1);
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsOnline { get; set; } = false;
}