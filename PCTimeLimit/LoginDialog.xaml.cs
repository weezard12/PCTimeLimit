using System.Windows;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PCTimeLimit;

public partial class LoginDialog : Window
{
    public string? AdminUsername { get; private set; }
    public string? AdminPassword { get; private set; }
    public bool IsAuthenticated { get; private set; } = false;
    
    private readonly string _serverAddress = "127.0.0.1";
    private readonly int _serverPort = 8888;
    
    public LoginDialog()
    {
        InitializeComponent();
        Loaded += LoginDialog_Loaded;
    }
    
    private void LoginDialog_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameTextBox.Focus();
    }
    
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameTextBox.Text?.Trim();
        var password = PasswordBox.Password;
        
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show("Please enter both username and password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        LoginButton.IsEnabled = false;
        LoginButton.Content = "Authenticating...";
        
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
                MessageBox.Show("Invalid username or password. Please check your credentials.", "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                PasswordBox.Password = "";
                PasswordBox.Focus();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection error: {ex.Message}\n\nPlease ensure the server is running and accessible.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Login";
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
            using var client = new TcpClient();
            await client.ConnectAsync(_serverAddress, _serverPort);
            
            using var stream = client.GetStream();
            
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
            
            // Read response
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
            return responseObj.GetProperty("Success").GetBoolean();
        }
        catch
        {
            return false;
        }
    }
}
