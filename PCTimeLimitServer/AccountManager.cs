using System.Text.Json;

namespace PCTimeLimitServer;

public class AccountManager
{
    private readonly string _accountsFilePath;
    private readonly string _computersFilePath;
    private readonly Dictionary<string, Account> _accounts;
    private readonly Dictionary<string, ComputerInfo> _computers;
    
    public AccountManager()
    {
        // Create custom AppData folder for PC Time Limit Server
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PC Time Limit Server");
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        
        _accountsFilePath = Path.Combine(appDataPath, "accounts.json");
        _computersFilePath = Path.Combine(appDataPath, "computers.json");
        _accounts = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
        _computers = new Dictionary<string, ComputerInfo>(StringComparer.OrdinalIgnoreCase);
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

    public void LoadComputers()
    {
        try
        {
            if (File.Exists(_computersFilePath))
            {
                var json = File.ReadAllText(_computersFilePath);
                var computers = JsonSerializer.Deserialize<List<ComputerInfo>>(json);
                if (computers != null)
                {
                    _computers.Clear();
                    foreach (var computer in computers)
                    {
                        _computers[computer.ComputerId] = computer;
                    }
                }
                Console.WriteLine($"Loaded {_computers.Count} computers from {_computersFilePath}");
            }
            else
            {
                Console.WriteLine("No existing computers file found. Starting with empty computer database.");
                Console.WriteLine($"Computers will be saved to: {_computersFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading computers: {ex.Message}");
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

    public void SaveComputers()
    {
        try
        {
            var computers = _computers.Values.ToList();
            var json = JsonSerializer.Serialize(computers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_computersFilePath, json);
            Console.WriteLine($"Saved {_computers.Count} computers to {_computersFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving computers: {ex.Message}");
        }
    }
    
    public AccountResult CreateAccount(string username, string password, bool isAdmin = false)
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
            IsAdmin = isAdmin,
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
        
        return new AccountResult { Success = true, Data = account };
    }
    
    public bool AccountExists(string username)
    {
        return _accounts.ContainsKey(username);
    }

    public bool IsAdmin(string username)
    {
        return _accounts.TryGetValue(username, out var account) && account.IsAdmin;
    }

    public ComputerResult RegisterComputer(string computerId, string computerName, string adminUsername)
    {
        if (string.IsNullOrWhiteSpace(computerId))
        {
            return new ComputerResult { Success = false, ErrorMessage = "Computer ID cannot be empty" };
        }

        if (string.IsNullOrWhiteSpace(computerName))
        {
            return new ComputerResult { Success = false, ErrorMessage = "Computer name cannot be empty" };
        }

        if (string.IsNullOrWhiteSpace(adminUsername))
        {
            return new ComputerResult { Success = false, ErrorMessage = "Admin username is required" };
        }

        if (!_accounts.TryGetValue(adminUsername, out var account) || !account.IsAdmin)
        {
            return new ComputerResult { Success = false, ErrorMessage = "Invalid admin account" };
        }

        // Check if computer already exists
        if (_computers.ContainsKey(computerId))
        {
            // Update existing computer
            var existingComputer = _computers[computerId];
            existingComputer.ComputerName = computerName;
            existingComputer.AdminUsername = adminUsername;
            existingComputer.LastSeen = DateTime.UtcNow;
            existingComputer.IsOnline = true;
        }
        else
        {
            // Create new computer
            var computer = new ComputerInfo
            {
                ComputerId = computerId,
                ComputerName = computerName,
                AdminUsername = adminUsername,
                DailyTimeLimit = TimeSpan.FromHours(1), // Default 1 hour
                RegisteredAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                IsOnline = true
            };
            _computers[computerId] = computer;
        }

        SaveComputers();
        return new ComputerResult { Success = true, Data = _computers[computerId] };
    }

    public ComputerResult UpdateComputerStatus(string computerId, bool isOnline)
    {
        if (_computers.TryGetValue(computerId, out var computer))
        {
            computer.IsOnline = isOnline;
            computer.LastSeen = DateTime.UtcNow;
            SaveComputers();
            return new ComputerResult { Success = true, Data = computer };
        }

        return new ComputerResult { Success = false, ErrorMessage = "Computer not found" };
    }

    public ComputerResult SetComputerTimeLimit(string computerId, TimeSpan timeLimit, string adminUsername)
    {
        if (!_accounts.TryGetValue(adminUsername, out var account) || !account.IsAdmin)
        {
            return new ComputerResult { Success = false, ErrorMessage = "Invalid admin account" };
        }

        if (_computers.TryGetValue(computerId, out var computer))
        {
            // Check if admin owns this computer
            if (!string.Equals(computer.AdminUsername, adminUsername, StringComparison.OrdinalIgnoreCase))
            {
                return new ComputerResult { Success = false, ErrorMessage = "You can only modify computers under your control" };
            }

            computer.DailyTimeLimit = timeLimit;
            SaveComputers();
            return new ComputerResult { Success = true, Data = computer };
        }

        return new ComputerResult { Success = false, ErrorMessage = "Computer not found" };
    }

    public List<ComputerInfo> GetComputersForAdmin(string adminUsername)
    {
        if (!_accounts.TryGetValue(adminUsername, out var account) || !account.IsAdmin)
        {
            return new List<ComputerInfo>();
        }

        return _computers.Values
            .Where(c => string.Equals(c.AdminUsername, adminUsername, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<ComputerInfo> GetAllComputers()
    {
        return _computers.Values.ToList();
    }

    public ComputerInfo? GetComputer(string computerId)
    {
        _computers.TryGetValue(computerId, out var computer);
        return computer;
    }
    
    public int GetAccountCount()
    {
        return _accounts.Count;
    }

    public int GetComputerCount()
    {
        return _computers.Count;
    }
    
    public string GetAccountsFilePath()
    {
        return _accountsFilePath;
    }

    public string GetComputersFilePath()
    {
        return _computersFilePath;
    }
    
    public void ClearAllAccounts()
    {
        _accounts.Clear();
        Console.WriteLine("All accounts cleared from memory.");
    }

    public void ClearAllComputers()
    {
        _computers.Clear();
        Console.WriteLine("All computers cleared from memory.");
    }
    
    public List<string> GetAllUsernames()
    {
        return _accounts.Keys.ToList();
    }

    public List<string> GetAllAdminUsernames()
    {
        return _accounts.Values.Where(a => a.IsAdmin).Select(a => a.Username).ToList();
    }
}

public class Account
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Plain text password
    public bool IsAdmin { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
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

public class AccountResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
}

public class ComputerResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
}
