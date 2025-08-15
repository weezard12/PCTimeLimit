using System.Windows;
using PCTimeLimitAdmin.Services;

namespace PCTimeLimitAdmin;

public partial class MainWindow : Window
{
    private TcpClientService? _tcpClient;
    private string? _loggedInUsername;
    
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
    
    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _tcpClient?.Disconnect();
        _tcpClient = null;
        _loggedInUsername = null;
        
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
            // User cancelled login, close the application
            Application.Current.Shutdown();
        }
    }
    
    private void UpdateConnectionStatus(bool isConnected, string status)
    {
        ConnectionStatusIndicator.Fill = isConnected ? 
            System.Windows.Media.Brushes.Green : 
            System.Windows.Media.Brushes.Red;
        ConnectionStatusText.Text = status;
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _tcpClient?.Disconnect();
        base.OnClosed(e);
    }
}