using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCTimeLimitServer;

class Program
{
    private static TcpListener? _listener;
    private static readonly Dictionary<string, ClientConnection> _clients = new();
    private static readonly AccountManager _accountManager = new();
    private static bool _isRunning = true;
    private static readonly ConsoleCommandHandler _commandHandler;
    
    public static int GetConnectedClientsCount() => _clients.Count;
    public static bool IsServerRunning() => _isRunning;

    static Program()
    {
        _commandHandler = new ConsoleCommandHandler(_accountManager);
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("PCTimeLimit Server Starting...");
        
        // Check if running in test mode
        if (args.Length > 0 && args[0].ToLower() == "test")
        {
            Console.WriteLine("Running in test mode...");
            await TestClient.RunTests();
            return;
        }
        
        // Check if running in local test mode
        if (args.Length > 0 && args[0].ToLower() == "localtest")
        {
            Console.WriteLine("Running local tests...");
            LocalTest.RunAccountManagerTests();
            return;
        }
        
        // Check if running login fix test
        if (args.Length > 0 && args[0].ToLower() == "testloginfix")
        {
            Console.WriteLine("Running login validation fix test...");
            TestLoginFix.TestLoginValidation();
            return;
        }
        

        
        // Load existing accounts
        _accountManager.LoadAccounts();
        _accountManager.LoadComputers();
        
        // Start TCP server
        _listener = new TcpListener(IPAddress.Any, 8888);
        _listener.Start();
        
        Console.WriteLine($"Server started on port 8888");
        Console.WriteLine("Waiting for connections...");
        Console.WriteLine("Type 'help' for available commands");
        Console.WriteLine("Press Ctrl+C to stop the server");
        
        // Handle shutdown gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
            _listener?.Stop();
        };
        
        // Start console command handler in background
        var commandTask = Task.Run(() => _commandHandler.StartCommandLoop());
        
        // Accept client connections
        while (_isRunning)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
            catch (Exception ex) when (!_isRunning)
            {
                // Server is shutting down
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
        
        Console.WriteLine("Server shutting down...");
        
        // Wait for command handler to finish
        try
        {
            await commandTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in command handler: {ex.Message}");
        }
    }
    
    private static async Task HandleClientAsync(TcpClient client)
    {
        var clientId = Guid.NewGuid().ToString();
        var connection = new ClientConnection(client, clientId);
        
        Console.WriteLine($"Client {clientId} connected from {client.Client.RemoteEndPoint}");
        
        try
        {
            _clients[clientId] = connection;
            
            using var stream = client.GetStream();
            var buffer = new byte[1024];
            
            while (client.Connected && _isRunning)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break; // Client disconnected
                
                var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var response = await ProcessMessageAsync(message, connection);
                
                if (!string.IsNullOrEmpty(response))
                {
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client {clientId}: {ex.Message}");
        }
        finally
        {
            _clients.Remove(clientId);
            client.Close();
            Console.WriteLine($"Client {clientId} disconnected");
        }
    }
    
    private static async Task<string> ProcessMessageAsync(string message, ClientConnection connection)
    {
        try
        {
            var request = JsonSerializer.Deserialize<MessageRequest>(message);
            if (request == null) return CreateErrorResponse("Invalid message format");
            
            switch (request.Type)
            {
                case MessageType.CreateAccount:
                    return await HandleCreateAccountAsync(request, connection);
                    
                case MessageType.Login:
                    return await HandleLoginAsync(request, connection);
                    
                case MessageType.Heartbeat:
                    return CreateResponse(MessageType.Heartbeat, new { Status = "OK" });
                    
                case MessageType.RegisterComputer:
                    return await HandleRegisterComputerAsync(request, connection);
                    
                case MessageType.UpdateComputerStatus:
                    return await HandleUpdateComputerStatusAsync(request, connection);
                    
                case MessageType.SetComputerTimeLimit:
                    return await HandleSetComputerTimeLimitAsync(request, connection);
                    
                case MessageType.GetComputersForAdmin:
                    return await HandleGetComputersForAdminAsync(request, connection);
                    
                case MessageType.ResetComputerTimer:
                    return await HandleResetComputerTimerAsync(request, connection);
                
                case MessageType.AcknowledgeReset:
                    return await HandleAcknowledgeResetAsync(request, connection);
                
                default:
                    return CreateErrorResponse($"Unknown message type: {request.Type}");
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error processing message: {ex.Message}");
        }
    }
    
    private static async Task<string> HandleCreateAccountAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<CreateAccountData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.Username) || string.IsNullOrWhiteSpace(data.Password))
        {
            return CreateErrorResponse("Username and password are required");
        }
        
        var result = _accountManager.CreateAccount(data.Username, data.Password, data.IsAdmin);
        if (result.Success)
        {
            var accountType = data.IsAdmin ? "admin" : "user";
            connection.Username = data.Username;
            connection.IsAuthenticated = true;
            Console.WriteLine($"Account created: {data.Username} ({accountType})");
            return CreateResponse(MessageType.CreateAccount, new { Success = true, Message = $"{accountType} account created successfully" }, true);
        }
        else
        {
            return CreateResponse(MessageType.CreateAccount, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }
    
    private static async Task<string> HandleLoginAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<LoginData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.Username) || string.IsNullOrWhiteSpace(data.Password))
        {
            return CreateErrorResponse("Username and password are required");
        }
        
        var result = _accountManager.ValidateLogin(data.Username, data.Password);
        if (result.Success)
        {
            connection.Username = data.Username;
            connection.IsAuthenticated = true;
            Console.WriteLine($"User logged in: {data.Username}");
            return CreateResponse(MessageType.Login, new { Success = true, Message = "Login successful" }, true);
        }
        else
        {
            return CreateResponse(MessageType.Login, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleRegisterComputerAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<RegisterComputerData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.ComputerName) || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Computer ID, name, and admin username are required");
        }

        var result = _accountManager.RegisterComputer(data.ComputerId, data.ComputerName, data.AdminUsername);
        if (result.Success)
        {
            Console.WriteLine($"Computer registered: {data.ComputerName} ({data.ComputerId}) under admin {data.AdminUsername}");
            return CreateResponse(MessageType.RegisterComputer, new { Success = true, Message = "Computer registered successfully", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.RegisterComputer, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleUpdateComputerStatusAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<UpdateComputerStatusData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId))
        {
            return CreateErrorResponse("Computer ID is required");
        }

        var result = _accountManager.UpdateComputerStatus(data.ComputerId, data.IsOnline);
        if (result.Success)
        {
            var status = data.IsOnline ? "online" : "offline";
            Console.WriteLine($"Computer {data.ComputerId} status updated to {status}");
            return CreateResponse(MessageType.UpdateComputerStatus, new { Success = true, Message = $"Computer status updated to {status}", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.UpdateComputerStatus, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleSetComputerTimeLimitAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<SetComputerTimeLimitData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Computer ID and admin username are required");
        }

        var result = _accountManager.SetComputerTimeLimit(data.ComputerId, data.DailyTimeLimit, data.AdminUsername);
        if (result.Success)
        {
            Console.WriteLine($"Computer {data.ComputerId} time limit set to {data.DailyTimeLimit} by admin {data.AdminUsername}");
            return CreateResponse(MessageType.SetComputerTimeLimit, new { Success = true, Message = "Time limit updated successfully", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.SetComputerTimeLimit, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleGetComputersForAdminAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<GetComputersForAdminData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Admin username is required");
        }

        var computers = _accountManager.GetComputersForAdmin(data.AdminUsername);
        Console.WriteLine($"Retrieved {computers.Count} computers for admin {data.AdminUsername}");
        return CreateResponse(MessageType.GetComputersForAdmin, new { Success = true, Computers = computers }, true);
    }

    private static async Task<string> HandleResetComputerTimerAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<ResetComputerTimerData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Computer ID and admin username are required");
        }

        var result = _accountManager.QueueResetTimer(data.ComputerId, data.AdminUsername);
        if (result.Success)
        {
            Console.WriteLine($"Queued reset for computer {data.ComputerId} by admin {data.AdminUsername}");
            return CreateResponse(MessageType.ResetComputerTimer, new { Success = true, Message = "Reset queued", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.ResetComputerTimer, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleAcknowledgeResetAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<AcknowledgeResetData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId))
        {
            return CreateErrorResponse("Computer ID is required");
        }

        var result = _accountManager.AcknowledgeReset(data.ComputerId);
        if (result.Success)
        {
            Console.WriteLine($"Reset acknowledged by computer {data.ComputerId}");
            return CreateResponse(MessageType.AcknowledgeReset, new { Success = true, Message = "Reset acknowledged", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.AcknowledgeReset, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }
    
    private static string CreateResponse(MessageType type, object data)
    {
        var response = new MessageResponse
        {
            Type = type,
            Success = true,
            Data = data
        };
        return JsonSerializer.Serialize(response);
    }
    
    private static string CreateResponse(MessageType type, object data, bool success)
    {
        var response = new MessageResponse
        {
            Type = type,
            Success = success,
            Data = data
        };
        return JsonSerializer.Serialize(response);
    }
    
    private static string CreateErrorResponse(string errorMessage)
    {
        var response = new MessageResponse
        {
            Type = MessageType.Error,
            Success = false,
            ErrorMessage = errorMessage
        };
        return JsonSerializer.Serialize(response);
    }
}

public enum MessageType
{
    CreateAccount = 1,
    Login = 2,
    Heartbeat = 3,
    RegisterComputer = 4,
    UpdateComputerStatus = 5,
    SetComputerTimeLimit = 6,
    GetComputersForAdmin = 7,
    ResetComputerTimer = 8,
    AcknowledgeReset = 9,
    Error = 99
}

public class MessageRequest
{
    public MessageType Type { get; set; }
    public object? Data { get; set; }
}

public class MessageResponse
{
    public MessageType Type { get; set; }
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateAccountData
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool IsAdmin { get; set; } = false;
}

public class LoginData
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class RegisterComputerData
{
    public string ComputerId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string AdminUsername { get; set; } = "";
}

public class UpdateComputerStatusData
{
    public string ComputerId { get; set; } = "";
    public bool IsOnline { get; set; } = false;
}

public class SetComputerTimeLimitData
{
    public string ComputerId { get; set; } = "";
    public TimeSpan DailyTimeLimit { get; set; } = TimeSpan.FromHours(1);
    public string AdminUsername { get; set; } = "";
}

public class GetComputersForAdminData
{
    public string AdminUsername { get; set; } = "";
}

public class ResetComputerTimerData
{
    public string ComputerId { get; set; } = "";
    public string AdminUsername { get; set; } = "";
}

public class AcknowledgeResetData
{
    public string ComputerId { get; set; } = "";
}

public class ClientConnection
{
    public TcpClient Client { get; }
    public string ClientId { get; }
    public string? Username { get; set; }
    public bool IsAuthenticated { get; set; }
    public DateTime ConnectedAt { get; }
    
    public ClientConnection(TcpClient client, string clientId)
    {
        Client = client;
        ClientId = clientId;
        ConnectedAt = DateTime.UtcNow;
    }
}

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
