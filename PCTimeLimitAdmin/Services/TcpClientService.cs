using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PCTimeLimitAdmin.Shared;
using PCTimeLimitAdmin.Configuration;

namespace PCTimeLimitAdmin.Services;

public class TcpClientService
{
    // Built-in server configuration
    private const string DEFAULT_SERVER_ADDRESS = ServerConfig.SERVER_ADDRESS;
    private const int DEFAULT_SERVER_PORT = ServerConfig.SERVER_PORT;
    
    private readonly string _serverAddress;
    private readonly int _serverPort;
    private TcpClient? _client;
    private NetworkStream? _stream;
    
    public bool IsConnected => _client?.Connected == true;
    
    public TcpClientService()
    {
        _serverAddress = DEFAULT_SERVER_ADDRESS;
        _serverPort = DEFAULT_SERVER_PORT;
    }
    
    public TcpClientService(string serverAddress, int serverPort)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }
    
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_serverAddress, _serverPort);
            _stream = _client.GetStream();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            return false;
        }
    }
    
    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }
    
    public async Task<MessageResponse?> SendMessageAsync(MessageRequest request)
    {
        if (_client?.Connected != true || _stream == null)
        {
            if (!await ConnectAsync())
            {
                return new MessageResponse
                {
                    Type = MessageType.Error,
                    Success = false,
                    ErrorMessage = "Failed to connect to server"
                };
            }
        }
        
        try
        {
            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            await _stream.WriteAsync(bytes, 0, bytes.Length);
            
            var buffer = new byte[1024];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            return JsonSerializer.Deserialize<MessageResponse>(response);
        }
        catch (Exception ex)
        {
            return new MessageResponse
            {
                Type = MessageType.Error,
                Success = false,
                ErrorMessage = $"Communication error: {ex.Message}"
            };
        }
    }
    
    public async Task<MessageResponse?> CreateAccountAsync(string username, string password)
    {
        var request = new MessageRequest
        {
            Type = MessageType.CreateAccount,
            Data = new CreateAccountData
            {
                Username = username,
                Password = password
            }
        };
        
        return await SendMessageAsync(request);
    }
    
    public async Task<MessageResponse?> LoginAsync(string username, string password)
    {
        var request = new MessageRequest
        {
            Type = MessageType.Login,
            Data = new LoginData
            {
                Username = username,
                Password = password
            }
        };
        
        return await SendMessageAsync(request);
    }
    
    public async Task<MessageResponse?> SendHeartbeatAsync()
    {
        var request = new MessageRequest
        {
            Type = MessageType.Heartbeat,
            Data = null
        };
        
        return await SendMessageAsync(request);
    }
}
