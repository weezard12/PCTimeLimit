using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PCTimeLinitShared.Messaging;

namespace PCTimeLimitServer;

class Program
{
    private static TcpListener? _listener;
    private static readonly Dictionary<string, ClientConnection> _clients = new();
    private static readonly AccountManager _accountManager = new();
    private static bool _isRunning = true;
    private static readonly ConsoleCommandHandler _commandHandler = new ConsoleCommandHandler(_accountManager);
    
    public static int GetConnectedClientsCount() => _clients.Count;
    public static bool IsServerRunning() => _isRunning;

    static async Task Main(string[] args)
    {
        Console.WriteLine("PCTimeLimit Server Starting...");
        
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
