using System.Windows;
using System.Linq;

namespace PCTimeLimit;

public partial class LoginDialog : Window
{
    public string? AdminCode { get; private set; }
    public bool IsAuthenticated { get; private set; } = false;
    
    public LoginDialog()
    {
        InitializeComponent();
        Loaded += LoginDialog_Loaded;
    }
    
    private void LoginDialog_Loaded(object sender, RoutedEventArgs e)
    {
        AdminCodeTextBox.Focus();
        StatusTextBlock.Text = string.Empty;
    }
    
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var code = AdminCodeTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            StatusTextBlock.Text = "Please enter the admin code.";
            return;
        }
        if (code.Length != 6 || !code.All(c => c >= 'A' && c <= 'Z'))
        {
            StatusTextBlock.Text = "Admin code must be 6 capital letters (A-Z).";
            return;
        }

        AdminCode = code;
        IsAuthenticated = true;
        DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}


