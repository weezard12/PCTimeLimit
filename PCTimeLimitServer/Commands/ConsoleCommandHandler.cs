using System;
using System.IO;

namespace PCTimeLimitServer;

public class ConsoleCommandHandler
{
    private readonly AccountManager _accountManager;
    private bool _isRunning = true;

    public ConsoleCommandHandler(AccountManager accountManager)
    {
        _accountManager = accountManager;
    }

    public void StartCommandLoop()
    {
        while (_isRunning)
        {
            try
            {
                var input = Console.ReadLine();
                if (input == null) continue;

                var command = input.Trim().ToLower();
                if (string.IsNullOrEmpty(command)) continue;

                ProcessCommand(command);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command: {ex.Message}");
            }
        }
    }

    private void ProcessCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLower();
        
        switch (cmd)
        {
            case "help":
                ShowHelp();
                break;
            case "create-admin":
                if (parts.Length >= 3)
                {
                    CreateAdminAccount(parts[1], parts[2]);
                }
                else
                {
                    Console.WriteLine("Usage: create-admin <username> <password>");
                }
                break;
            case "clear-user-data":
                ClearUserData();
                break;
            case "clear-computer-data":
                ClearComputerData();
                break;
            case "status":
                ShowStatus();
                break;
            case "list-users":
                ListUsers();
                break;
            case "list-computers":
                ListComputers();
                break;
            case "quit":
            case "exit":
                _isRunning = false;
                break;
            default:
                Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                break;
        }
    }

    private void ShowHelp()
    {
        Console.WriteLine("\n=== Available Commands ===");
        Console.WriteLine("help              - Show this help message");
        Console.WriteLine("create-admin <username> <password> - Create a new admin account");
        Console.WriteLine("clear-user-data   - Clear all user accounts and data");
        Console.WriteLine("clear-computer-data - Clear all computer data and registrations");
        Console.WriteLine("status            - Show server status and statistics");
        Console.WriteLine("list-users        - List all registered users");
        Console.WriteLine("list-computers    - List all registered computers");
        Console.WriteLine("quit/exit         - Exit the server");
        Console.WriteLine("=======================\n");
    }

    private void CreateAdminAccount(string username, string password)
    {
        try
        {
            var result = _accountManager.CreateAccount(username, password, true);
            if (result.Success)
            {
                Console.WriteLine($"Admin account created successfully: {username}");
            }
            else
            {
                Console.WriteLine($"Failed to create admin account: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating admin account: {ex.Message}");
        }
    }

    private void ClearUserData()
    {
        Console.Write("Are you sure you want to clear ALL user data? This action cannot be undone. (yes/no): ");
        var confirmation = Console.ReadLine()?.Trim().ToLower();
        
        if (confirmation == "yes")
        {
            try
            {
                // Clear accounts from memory
                var accountCount = _accountManager.GetAccountCount();
                _accountManager.ClearAllAccounts();
                
                // Clear the accounts file
                var accountsFilePath = _accountManager.GetAccountsFilePath();
                if (File.Exists(accountsFilePath))
                {
                    File.Delete(accountsFilePath);
                    Console.WriteLine($"Deleted accounts file: {accountsFilePath}");
                }
                
                Console.WriteLine($"Successfully cleared {accountCount} user accounts and all data.");
                Console.WriteLine("All user accounts have been permanently deleted.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing user data: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("User data clear operation cancelled.");
        }
    }

    private void ClearComputerData()
    {
        Console.Write("Are you sure you want to clear ALL computer data? This action cannot be undone. (yes/no): ");
        var confirmation = Console.ReadLine()?.Trim().ToLower();
        
        if (confirmation == "yes")
        {
            try
            {
                // Clear computers from memory
                var computerCount = _accountManager.GetComputerCount();
                _accountManager.ClearAllComputers();
                
                // Clear the computers file
                var computersFilePath = _accountManager.GetComputersFilePath();
                if (File.Exists(computersFilePath))
                {
                    File.Delete(computersFilePath);
                    Console.WriteLine($"Deleted computers file: {computersFilePath}");
                }
                
                Console.WriteLine($"Successfully cleared {computerCount} computer registrations and all data.");
                Console.WriteLine("All computer registrations have been permanently deleted.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing computer data: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Computer data clear operation cancelled.");
        }
    }

    private void ShowStatus()
    {
        Console.WriteLine("\n=== Server Status ===");
        Console.WriteLine($"Connected clients: {Program.GetConnectedClientsCount()}");
        Console.WriteLine($"Total accounts: {_accountManager.GetAccountCount()}");
        Console.WriteLine($"Total computers: {_accountManager.GetComputerCount()}");
        Console.WriteLine($"Admin accounts: {_accountManager.GetAllAdminUsernames().Count}");
        Console.WriteLine($"Accounts file: {_accountManager.GetAccountsFilePath()}");
        Console.WriteLine($"Computers file: {_accountManager.GetComputersFilePath()}");
        Console.WriteLine($"Server running: {Program.IsServerRunning()}");
        Console.WriteLine("===================\n");
    }
    
    private void ListUsers()
    {
        var usernames = _accountManager.GetAllUsernames();
        var adminUsernames = _accountManager.GetAllAdminUsernames();
        
        if (usernames.Count == 0)
        {
            Console.WriteLine("\n=== Registered Users ===");
            Console.WriteLine("No users registered.");
            Console.WriteLine("=======================\n");
        }
        else
        {
            Console.WriteLine("\n=== Registered Users ===");
            foreach (var username in usernames)
            {
                var isAdmin = adminUsernames.Contains(username);
                var adminStatus = isAdmin ? " [ADMIN]" : "";
                Console.WriteLine($"- {username}{adminStatus}");
            }
            Console.WriteLine($"Total: {usernames.Count} users ({adminUsernames.Count} admins)");
            Console.WriteLine("=======================\n");
        }
    }

    private void ListComputers()
    {
        var computers = _accountManager.GetAllComputers();
        if (computers.Count == 0)
        {
            Console.WriteLine("\n=== Registered Computers ===");
            Console.WriteLine("No computers registered.");
            Console.WriteLine("============================\n");
        }
        else
        {
            Console.WriteLine("\n=== Registered Computers ===");
            foreach (var computer in computers)
            {
                var status = computer.IsOnline ? "ONLINE" : "OFFLINE";
                var lastSeen = computer.LastSeen.ToString("yyyy-MM-dd HH:mm:ss");
                Console.WriteLine($"- {computer.ComputerName} ({computer.ComputerId})");
                Console.WriteLine($"  Admin: {computer.AdminUsername}");
                Console.WriteLine($"  Daily Limit: {computer.DailyTimeLimit}");
                Console.WriteLine($"  Status: {status}");
                Console.WriteLine($"  Last Seen: {lastSeen}");
                Console.WriteLine($"  Registered: {computer.RegisteredAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
            }
            Console.WriteLine($"Total: {computers.Count} computers");
            Console.WriteLine("============================\n");
        }
    }

    public void Stop()
    {
        _isRunning = false;
    }
}
