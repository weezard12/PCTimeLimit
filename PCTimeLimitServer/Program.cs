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
        
        var result = _accountManager.CreateAccount(data.Username, data.Password);
        if (result.Success)
        {
            connection.Username = data.Username;
            connection.IsAuthenticated = true;
            Console.WriteLine($"Account created: {data.Username}");
            return CreateResponse(MessageType.CreateAccount, new { Success = true, Message = "Account created successfully" }, true);
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
}

public class LoginData
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
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
        switch (command)
        {
            case "help":
                ShowHelp();
                break;
            case "clear-user-data":
                ClearUserData();
                break;
            case "status":
                ShowStatus();
                break;
            case "list-users":
                ListUsers();
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
        Console.WriteLine("clear-user-data   - Clear all user accounts and data");
        Console.WriteLine("status            - Show server status and statistics");
        Console.WriteLine("list-users        - List all registered users");
        Console.WriteLine("quit/exit         - Exit the server");
        Console.WriteLine("=======================\n");
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

    private void ShowStatus()
    {
        Console.WriteLine("\n=== Server Status ===");
        Console.WriteLine($"Connected clients: {Program.GetConnectedClientsCount()}");
        Console.WriteLine($"Total accounts: {_accountManager.GetAccountCount()}");
        Console.WriteLine($"Accounts file: {_accountManager.GetAccountsFilePath()}");
        Console.WriteLine($"Server running: {Program.IsServerRunning()}");
        Console.WriteLine("===================\n");
    }
    
    private void ListUsers()
    {
        var usernames = _accountManager.GetAllUsernames();
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
                Console.WriteLine($"- {username}");
            }
            Console.WriteLine($"Total: {usernames.Count} users");
            Console.WriteLine("=======================\n");
        }
    }

    public void Stop()
    {
        _isRunning = false;
    }
}
