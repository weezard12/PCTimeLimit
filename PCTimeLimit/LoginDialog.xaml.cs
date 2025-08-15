using System.Windows;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCTimeLimit;

public partial class LoginDialog : Window
{
    public string? AdminUsername { get; private set; }
    public string? AdminPassword { get; private set; }
    public bool IsAuthenticated { get; private set; } = false;
    
    private readonly string _serverAddress = "127.0.0.1";
    private readonly int _serverPort = 8888;
    private const int ConnectionTimeoutMs = 5000;
    private const int ReadTimeoutMs = 5000;
    
    public LoginDialog()
    {
        InitializeComponent();
        Loaded += LoginDialog_Loaded;
    }
    
    private void LoginDialog_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameTextBox.Focus();
        StatusTextBlock.Text = string.Empty;
    }
    
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameTextBox.Text?.Trim();
        var password = PasswordBox.Password;
        
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            StatusTextBlock.Text = "Please enter both username and password.";
            return;
        }
        
        LoginButton.IsEnabled = false;
        LoginButton.Content = "Authenticating...";
        StatusTextBlock.Text = "Connecting to server...";
        
        try
        {
            var isAuthenticated = await AuthenticateWithServerAsync(username, password);
            if (isAuthenticated)
            {
                AdminUsername = username;
                AdminPassword = password;
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                StatusTextBlock.Text = "Invalid username or password. Please check your credentials.";
                PasswordBox.Password = "";
                PasswordBox.Focus();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Connection error: {ex.Message}";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Login";
            if (string.IsNullOrEmpty(StatusTextBlock.Text))
                StatusTextBlock.Text = string.Empty;
        }
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    private async Task<bool> AuthenticateWithServerAsync(string username, string password)
    {
        try
        {
            using var cts = new CancellationTokenSource(ConnectionTimeoutMs);
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(_serverAddress, _serverPort);
            var completed = await Task.WhenAny(connectTask, Task.Delay(ConnectionTimeoutMs, cts.Token));
            if (completed != connectTask)
                throw new TimeoutException("Connection timed out");
            await connectTask; // ensure exceptions are observed
            
            using var stream = client.GetStream();
            stream.ReadTimeout = ReadTimeoutMs;
            
            var request = new
            {
                Type = 2, // Login
                Data = new
                {
                    Username = username,
                    Password = password
                }
            };
            
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(data, 0, data.Length);
            
            // Read response with timeout
            var buffer = new byte[2048];
            using var readCts = new CancellationTokenSource(ReadTimeoutMs);
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
            if (bytesRead <= 0) return false;
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            try
            {
                var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
                if (responseObj.TryGetProperty("Success", out var successProp))
                {
                    return successProp.GetBoolean();
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
