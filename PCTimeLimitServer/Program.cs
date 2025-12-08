using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using PCTimeLimitShared.Messaging;
using static PCTimeLimitShared.Consts;

namespace PCTimeLimitServer;

class Program
{
    private static TcpListener? _listener;
    private static readonly Dictionary<string, ClientConnection> _clients = new();
    private static readonly AccountManager _accountManager = new();
    private static bool _isRunning = true;
    private static readonly ConsoleCommandHandler _commandHandler = new ConsoleCommandHandler(_accountManager);
    private static DateTime _lastConsoleClear = DateTime.UtcNow;
    private static readonly TimeSpan ConsoleClearInterval = TimeSpan.FromHours(1); // Clear every hour
    
    public static int GetConnectedClientsCount() => _clients.Count;
    public static bool IsServerRunning() => _isRunning;

    static async Task Main(string[] args)
    {
        Console.WriteLine("PCTimeLimit Server Starting...");
        
        // Check and terminate any existing server on the port
        await TerminateExistingServerOnPort(ServerPort);
        
        // Load existing accounts
        _accountManager.LoadAccounts();
        _accountManager.LoadComputers();

        // Start TCP server - listens on all interfaces (0.0.0.0)
        _listener = new TcpListener(IPAddress.Any, ServerPort);

        try
        {
            _listener.Start();
            Console.WriteLine($"Server started on port {ServerPort}");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            Console.WriteLine($"Error: Permission denied to bind to port {ServerPort}");
            Console.WriteLine("Ports below 1024 require elevated privileges on Linux.");
            Console.WriteLine("Solutions:");
            Console.WriteLine("1. Run with sudo: sudo ./PCTimeLimitServer");
            Console.WriteLine("2. Use a port above 1024 (modify ServerPort in Consts.cs)");
            Console.WriteLine("3. Set capabilities: sudo setcap 'cap_net_bind_service=+ep' ./PCTimeLimitServer");
            Environment.Exit(1);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.WriteLine($"Error: Port {ServerPort} is still in use");
            Console.WriteLine("Please manually stop the process using this port:");
            Console.WriteLine($"Linux: sudo lsof -i :{ServerPort}");
            Console.WriteLine($"Windows: netstat -ano | findstr :{ServerPort}");
            Environment.Exit(1);
        }
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
        
        // Start console clearing task for long-running server
        var consoleClearTask = Task.Run(() => StartConsoleClearingTask());
        
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
        
        // Dispose resources
        try
        {
            _accountManager.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disposing resources: {ex.Message}");
        }
    }

    private static async Task TerminateExistingServerOnPort(int port)
    {
        try
        {
            bool isPortInUse = IsPortInUse(port);
            
            if (!isPortInUse)
            {
                return; // Port is free, nothing to do
            }
            
            Console.WriteLine($"Port {port} is already in use. Attempting to free it...");
            
            // Get the process using the port
            var processes = GetProcessesUsingPort(port);
            
            foreach (var pid in processes)
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    Console.WriteLine($"Terminating process {pid} ({process.ProcessName}) using port {port}");
                    process.Kill();
                    await Task.Delay(500); // Wait a bit for port to be released
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not terminate process {pid}: {ex.Message}");
                }
            }
            
            // Wait a bit for the port to be released
            await Task.Delay(1000);
            
            if (IsPortInUse(port))
            {
                Console.WriteLine($"Warning: Port {port} is still in use. The server may fail to start.");
                Console.WriteLine("Please manually stop any processes using this port or run with elevated privileges.");
                Console.WriteLine("You can also try a different port by modifying the ServerPort in Consts.cs");
            }
            else
            {
                Console.WriteLine($"Port {port} has been freed.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking/terminating processes on port {port}: {ex.Message}");
        }
    }
    
    private static bool IsPortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }
    
    private static List<int> GetProcessesUsingPort(int port)
    {
        var pids = new List<int>();
        
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: use netstat to find the process
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = $"-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.Contains($":{port}") && line.Contains("LISTENING"))
                        {
                            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && int.TryParse(parts.Last(), out var pid))
                            {
                                pids.Add(pid);
                            }
                        }
                    }
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux: use lsof to find the process
                var startInfo = new ProcessStartInfo
                {
                    FileName = "lsof",
                    Arguments = $"-ti :{port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    foreach (var line in output.Split('\n'))
                    {
                        if (int.TryParse(line.Trim(), out var pid))
                        {
                            pids.Add(pid);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return pids.Distinct().ToList();
    }

    private static async Task StartConsoleClearingTask()
    {
        while (_isRunning)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30)); // Check every 30 minutes
                
                if (DateTime.UtcNow - _lastConsoleClear >= ConsoleClearInterval)
                {
                    ClearConsole();
                    _lastConsoleClear = DateTime.UtcNow;
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Server running: {_clients.Count} clients, {_accountManager.GetComputerCount()} computers");
                }
            }
            catch (Exception ex)
            {
                // Silently handle errors in console clearing task
            }
        }
    }

    private static void ClearConsole()
    {
        try
        {
            // Only clear if running on Linux (Ubuntu) - this is a server environment optimization
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                Console.Clear();
            }
        }
        catch
        {
            // Ignore clear failures
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        var clientId = Guid.NewGuid().ToString();
        var connection = new ClientConnection(client, clientId);
        
        // Reduced logging for performance
        if (_clients.Count % 10 == 0 || _clients.Count < 10)
        {
            Console.WriteLine($"Client {clientId} connected from {client.Client.RemoteEndPoint}");
        }
        
        try
        {
            _clients[clientId] = connection;
            
            using var stream = client.GetStream();
            var buffer = new byte[4096]; // Increased buffer size for better performance
            
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
        catch (Exception ex) when (ex is SocketException || ex is IOException)
        {
            // Silently handle common network errors
        }
        catch (Exception ex)
        {
            if (_clients.Count < 10 || DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Error handling client {clientId}: {ex.Message}");
            }
        }
        finally
        {
            _clients.Remove(clientId);
            
            try
            {
                client.Close();
            }
            catch
            {
                // Ignore close errors
            }
            
            // Only log disconnections periodically to reduce console clutter
            if (_clients.Count % 10 == 0 || _clients.Count < 10)
            {
                Console.WriteLine($"Client {clientId} disconnected");
            }
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
                
                case MessageType.ForceLockout:
                    return await HandleForceLockoutAsync(request, connection);

                case MessageType.AcknowledgeForceLockout:
                    return await HandleAcknowledgeForceLockoutAsync(request, connection);

                case MessageType.SetComputerAllowedUsage:
                    return await HandleSetComputerAllowedUsageAsync(request, connection);

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
            // Only log account creation periodically to reduce console output
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Account created: {data.Username} ({accountType})");
            }
            var adminCode = data.IsAdmin ? _accountManager.GetAdminCode(data.Username) : null;
            return CreateResponse(
                MessageType.CreateAccount,
                new { Success = true, Message = $"{accountType} account created successfully", AdminCode = adminCode },
                true);
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
            // Only log logins periodically to reduce console output
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"User logged in: {data.Username}");
            }
            var adminCode = _accountManager.GetAdminCode(data.Username);
            return CreateResponse(
                MessageType.Login,
                new { Success = true, Message = "Login successful", AdminCode = adminCode },
                true);
        }
        else
        {
            return CreateResponse(MessageType.Login, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleRegisterComputerAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<RegisterComputerData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.ComputerName))
        {
            return CreateErrorResponse("Computer ID and name are required");
        }

        // Resolve admin username by AdminCode if provided
        var adminUsername = data.AdminUsername;
        if (string.IsNullOrWhiteSpace(adminUsername) && !string.IsNullOrWhiteSpace(data.AdminCode))
        {
            adminUsername = _accountManager.GetAdminUsernameByCode(data.AdminCode);
        }
        if (string.IsNullOrWhiteSpace(adminUsername))
        {
            return CreateResponse(MessageType.RegisterComputer, new { Success = false, Message = "Invalid or missing admin identifier" }, false);
        }

        var result = _accountManager.RegisterComputer(data.ComputerId, data.ComputerName, adminUsername);
        if (result.Success)
        {
            // Only log computer registrations periodically
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Computer registered: {data.ComputerName} ({data.ComputerId}) under admin {adminUsername}");
            }
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
            // Status updates are frequent, don't log every one
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
            // Only log time limit changes periodically
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Computer {data.ComputerId} time limit set to {data.DailyTimeLimit} by admin {data.AdminUsername}");
            }
            return CreateResponse(MessageType.SetComputerTimeLimit, new { Success = true, Message = "Time limit updated successfully", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.SetComputerTimeLimit, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleSetComputerAllowedUsageAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<SetComputerAllowedUsageData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Computer ID and admin username are required");
        }

        var result = _accountManager.SetComputerAllowedUsage(data.ComputerId, data.AllowedUsageJson ?? string.Empty, data.AdminUsername);
        if (result.Success)
        {
            return CreateResponse(MessageType.SetComputerAllowedUsage, new { Success = true, Message = "Allowed usage updated successfully", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.SetComputerAllowedUsage, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleGetComputersForAdminAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<GetComputersForAdminData>(request.Data?.ToString() ?? "{}");
        if (data == null)
        {
            return CreateErrorResponse("Invalid request");
        }

        var adminUsername = data.AdminUsername;
        if (string.IsNullOrWhiteSpace(adminUsername) && !string.IsNullOrWhiteSpace(data.AdminCode))
        {
            adminUsername = _accountManager.GetAdminUsernameByCode(data.AdminCode);
        }
        if (string.IsNullOrWhiteSpace(adminUsername))
        {
            return CreateResponse(MessageType.GetComputersForAdmin, new { Success = false, Message = "Invalid or missing admin identifier" }, false);
        }

        var computers = _accountManager.GetComputersForAdmin(adminUsername);
        // Don't log every retrieval
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
            // Only log reset operations periodically
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Queued reset for computer {data.ComputerId} by admin {data.AdminUsername}");
            }
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
            // Don't log every acknowledgement
            return CreateResponse(MessageType.AcknowledgeReset, new { Success = true, Message = "Reset acknowledged", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.AcknowledgeReset, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleForceLockoutAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<ForceLockoutData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId) || string.IsNullOrWhiteSpace(data.AdminUsername))
        {
            return CreateErrorResponse("Computer ID and admin username are required");
        }

        var result = _accountManager.QueueForceLockout(data.ComputerId, data.AdminUsername);
        if (result.Success)
        {
            // Only log force lockout operations periodically
            if (DateTime.UtcNow.Second < 5)
            {
                Console.WriteLine($"Queued force lockout for computer {data.ComputerId} by admin {data.AdminUsername}");
            }
            return CreateResponse(MessageType.ForceLockout, new { Success = true, Message = "Force lockout queued", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.ForceLockout, new { Success = false, Message = result.ErrorMessage }, false);
        }
    }

    private static async Task<string> HandleAcknowledgeForceLockoutAsync(MessageRequest request, ClientConnection connection)
    {
        var data = JsonSerializer.Deserialize<AcknowledgeForceLockoutData>(request.Data?.ToString() ?? "{}");
        if (data == null || string.IsNullOrWhiteSpace(data.ComputerId))
        {
            return CreateErrorResponse("Computer ID is required");
        }

        var result = _accountManager.AcknowledgeForceLockout(data.ComputerId);
        if (result.Success)
        {
            // Don't log every acknowledgement
            return CreateResponse(MessageType.AcknowledgeForceLockout, new { Success = true, Message = "Force lockout acknowledged", Computer = result.Data }, true);
        }
        else
        {
            return CreateResponse(MessageType.AcknowledgeForceLockout, new { Success = false, Message = result.ErrorMessage }, false);
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
