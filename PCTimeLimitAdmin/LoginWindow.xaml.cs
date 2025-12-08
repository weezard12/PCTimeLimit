using System.Windows;
using System.Text.Json;
using PCTimeLimitAdmin.Services;
using PCTimeLimitAdmin.Configuration;
using PCTimeLimitShared;

namespace PCTimeLimitAdmin;

public partial class LoginWindow : Window
{
    private TcpClientService? _tcpClient;
    public string? LoggedInUsername { get; private set; }
    public string? CreatedAdminCode { get; private set; }
    
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += LoginWindow_Loaded;
    }
    
    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set server info text
        ServerInfoTextBlock.Text = $"Connecting to {Consts.ServerIP}:{Consts.ServerPort}";
        
        // Set focus to username field
        UsernameTextBox.Focus();
    }
    
    private async void CreateAccountButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateAccountAsync();
    }
    
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await LoginAsync();
    }
    
    private async Task CreateAccountAsync()
    {
        if (!ValidateInputs())
            return;
            
        SetStatus("Creating account...", false);
        CreateAccountButton.IsEnabled = false;
        LoginButton.IsEnabled = false;
        
        try
        {
            // Check firewall before attempting connection
            if (!await CheckAndHandleFirewallAsync())
            {
                SetStatus("Firewall configuration cancelled", false);
                return;
            }
            
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            
            _tcpClient = new TcpClientService();
            var response = await _tcpClient.CreateAccountAsync(username, password);
            
            if (response?.Success == true)
            {
                SetStatus("Account created successfully!", true);
                LoggedInUsername = username;
                // Try to extract AdminCode if provided
                try
                {
                    if (response.Data != null)
                    {
                        var dataJson = JsonSerializer.Serialize(response.Data);
                        var dataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
                        if (dataObj != null && dataObj.ContainsKey("AdminCode"))
                        {
                            var code = dataObj["AdminCode"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(code))
                            {
                                CreatedAdminCode = code;
                            }
                        }
                    }
                }
                catch { /* ignore parse errors */ }
                
                // Auto-login after successful account creation
                await Task.Delay(1000);
                await LoginAsync();
            }
            else
            {
                string errorMessage = "Unknown error";
                
                // Try to extract error message from response data
                if (response?.Data != null)
                {
                    try
                    {
                        var dataJson = JsonSerializer.Serialize(response.Data);
                        var dataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
                        if (dataObj != null && dataObj.ContainsKey("Message"))
                        {
                            errorMessage = dataObj["Message"]?.ToString() ?? "Unknown error";
                        }
                    }
                    catch
                    {
                        // Fallback to ErrorMessage field if data parsing fails
                        errorMessage = response?.ErrorMessage ?? "Unknown error";
                    }
                }
                else
                {
                    errorMessage = response?.ErrorMessage ?? "Unknown error";
                }
                
                SetStatus($"Failed to create account: {errorMessage}", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            CreateAccountButton.IsEnabled = true;
            LoginButton.IsEnabled = true;
        }
    }
    
    private async Task LoginAsync()
    {
        if (!ValidateInputs())
            return;
            
        SetStatus("Logging in...", false);
        CreateAccountButton.IsEnabled = false;
        LoginButton.IsEnabled = false;
        
        try
        {
            // Check firewall before attempting connection
            if (!await CheckAndHandleFirewallAsync())
            {
                SetStatus("Firewall configuration cancelled", false);
                return;
            }
            
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            
            _tcpClient = new TcpClientService();
            var response = await _tcpClient.LoginAsync(username, password);
            
            if (response?.Success == true)
            {
                SetStatus("Login successful!", true);
                LoggedInUsername = username;
                // Try to extract AdminCode if provided on login
                try
                {
                    if (response.Data != null)
                    {
                        var dataJson = JsonSerializer.Serialize(response.Data);
                        var dataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
                        if (dataObj != null && dataObj.ContainsKey("AdminCode"))
                        {
                            var code = dataObj["AdminCode"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(code))
                            {
                                CreatedAdminCode = code;
                            }
                        }
                    }
                }
                catch { /* ignore parse errors */ }
                
                // Close login window and open main window
                await Task.Delay(1000);
                DialogResult = true;
                Close();
            }
            else
            {
                string errorMessage = "Unknown error";
                
                // Try to extract error message from response data
                if (response?.Data != null)
                {
                    try
                    {
                        var dataJson = JsonSerializer.Serialize(response.Data);
                        var dataObj = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
                        if (dataObj != null && dataObj.ContainsKey("Message"))
                        {
                            errorMessage = dataObj["Message"]?.ToString() ?? "Unknown error";
                        }
                    }
                    catch
                    {
                        // Fallback to ErrorMessage field if data parsing fails
                        errorMessage = response?.ErrorMessage ?? "Unknown error";
                    }
                }
                else
                {
                    errorMessage = response?.ErrorMessage ?? "Unknown error";
                }
                
                SetStatus($"Login failed: {errorMessage}", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            CreateAccountButton.IsEnabled = true;
            LoginButton.IsEnabled = true;
        }
    }
    
    private bool ValidateInputs()
    {
        // Validate username
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            SetStatus("Please enter username", false);
            UsernameTextBox.Focus();
            return false;
        }
        
        if (UsernameTextBox.Text.Length < ServerConfig.MIN_USERNAME_LENGTH)
        {
            SetStatus($"Username must be at least {ServerConfig.MIN_USERNAME_LENGTH} characters long", false);
            UsernameTextBox.Focus();
            return false;
        }
        
        if (UsernameTextBox.Text.Length > ServerConfig.MAX_USERNAME_LENGTH)
        {
            SetStatus($"Username must be no more than {ServerConfig.MAX_USERNAME_LENGTH} characters long", false);
            UsernameTextBox.Focus();
            return false;
        }
        
        // Validate password
        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            SetStatus("Please enter password", false);
            PasswordBox.Focus();
            return false;
        }
        
        if (PasswordBox.Password.Length < ServerConfig.MIN_PASSWORD_LENGTH)
        {
            SetStatus($"Password must be at least {ServerConfig.MIN_PASSWORD_LENGTH} characters long", false);
            PasswordBox.Focus();
            return false;
        }
        
        if (PasswordBox.Password.Length > ServerConfig.MAX_PASSWORD_LENGTH)
        {
            SetStatus($"Password must be no more than {ServerConfig.MAX_PASSWORD_LENGTH} characters long", false);
            PasswordBox.Focus();
            return false;
        }
        
        return true;
    }
    
    private void SetStatus(string message, bool isSuccess)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isSuccess ? 
            System.Windows.Media.Brushes.Green : 
            System.Windows.Media.Brushes.Red;
    }
    
    private async Task<bool> CheckAndHandleFirewallAsync()
    {
        try
        {
            // Check if the port is blocked by firewall
            if (FirewallHelper.IsPortBlocked(Consts.ServerPort))
            {
                var result = MessageBox.Show(
                    $"Windows Firewall may be blocking communication on port {Consts.ServerPort}.\n\n" +
                    "Would you like to add a firewall rule to allow this application?\n\n" +
                    "This will require administrator privileges.",
                    "Firewall Configuration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SetStatus("Adding firewall rule...", false);
                    var success = await FirewallHelper.AddFirewallRuleAsync(
                        Consts.ServerPort,
                        "PCTimeLimit Admin Client",
                        "TCP",
                        "OUT");

                    if (success)
                    {
                        SetStatus("Firewall rule added successfully", true);
                        await Task.Delay(1000);
                        return true;
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to add firewall rule. You may need to manually configure Windows Firewall.",
                            "Firewall Configuration Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            
            return true; // Firewall not blocking or already configured
        }
        catch
        {
            // If we can't check firewall status, proceed anyway
            return true;
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _tcpClient?.Disconnect();
        base.OnClosed(e);
    }
}
