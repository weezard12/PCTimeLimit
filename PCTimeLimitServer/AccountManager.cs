using System.Text.Json;

namespace PCTimeLimitServer;

public class AccountManager
{
    private readonly string _accountsFilePath;
    private readonly Dictionary<string, Account> _accounts;
    
    public AccountManager()
    {
        // Create custom AppData folder for PC Time Limit Server
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PC Time Limit Server");
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        
        _accountsFilePath = Path.Combine(appDataPath, "accounts.json");
        _accounts = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
    }
    
    public void LoadAccounts()
    {
        try
        {
            if (File.Exists(_accountsFilePath))
            {
                var json = File.ReadAllText(_accountsFilePath);
                var accounts = JsonSerializer.Deserialize<List<Account>>(json);
                if (accounts != null)
                {
                    _accounts.Clear();
                    foreach (var account in accounts)
                    {
                        _accounts[account.Username] = account;
                    }
                }
                Console.WriteLine($"Loaded {_accounts.Count} accounts from {_accountsFilePath}");
            }
            else
            {
                Console.WriteLine("No existing accounts file found. Starting with empty account database.");
                Console.WriteLine($"Accounts will be saved to: {_accountsFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading accounts: {ex.Message}");
        }
    }
    
    public void SaveAccounts()
    {
        try
        {
            var accounts = _accounts.Values.ToList();
            var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_accountsFilePath, json);
            Console.WriteLine($"Saved {_accounts.Count} accounts to {_accountsFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving accounts: {ex.Message}");
        }
    }
    
    public AccountResult CreateAccount(string username, string password)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(username))
        {
            return new AccountResult { Success = false, ErrorMessage = "Username cannot be empty" };
        }
        
        if (string.IsNullOrWhiteSpace(password))
        {
            return new AccountResult { Success = false, ErrorMessage = "Password cannot be empty" };
        }
        
        if (username.Length < 3)
        {
            return new AccountResult { Success = false, ErrorMessage = "Username must be at least 3 characters long" };
        }
        
        if (password.Length < 6)
        {
            return new AccountResult { Success = false, ErrorMessage = "Password must be at least 6 characters long" };
        }
        
        // Check if account already exists
        if (_accounts.ContainsKey(username))
        {
            return new AccountResult { Success = false, ErrorMessage = "Account already exists" };
        }
        
        // Create new account (no encryption/hashing)
        var account = new Account
        {
            Username = username,
            Password = password, // Store password in plain text
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = null
        };
        
        _accounts[username] = account;
        SaveAccounts();
        
        return new AccountResult { Success = true };
    }
    
    public AccountResult ValidateLogin(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new AccountResult { Success = false, ErrorMessage = "Username and password are required" };
        }
        
        if (!_accounts.TryGetValue(username, out var account))
        {
            return new AccountResult { Success = false, ErrorMessage = "Invalid username or password" };
        }
        
        // Compare passwords directly (no hashing)
        if (!string.Equals(account.Password, password, StringComparison.Ordinal))
        {
            return new AccountResult { Success = false, ErrorMessage = "Invalid username or password" };
        }
        
        // Update last login time
        account.LastLoginAt = DateTime.UtcNow;
        SaveAccounts();
        
        return new AccountResult { Success = true };
    }
    
    public bool AccountExists(string username)
    {
        return _accounts.ContainsKey(username);
    }
    
    public int GetAccountCount()
    {
        return _accounts.Count;
    }
    
    public string GetAccountsFilePath()
    {
        return _accountsFilePath;
    }
    
    public void ClearAllAccounts()
    {
        _accounts.Clear();
        Console.WriteLine("All accounts cleared from memory.");
    }
    
    public List<string> GetAllUsernames()
    {
        return _accounts.Keys.ToList();
    }
}

public class Account
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Plain text password
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AccountResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
