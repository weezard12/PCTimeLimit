using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PCTimeLinitShared.Messaging;
using PCTimeLimitAdmin.Configuration;

namespace PCTimeLimitAdmin.Services;

public class TcpClientService
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly string _serverAddress = ServerConfig.SERVER_ADDRESS;
    private readonly int _serverPort = ServerConfig.SERVER_PORT;
    
    public bool IsConnected => _client?.Connected == true;
    
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_serverAddress, _serverPort);
            _stream = _client.GetStream();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<MessageResponse?> SendMessageAsync(object message)
    {
        if (!IsConnected) return null;
        
        try
        {
            var json = JsonSerializer.Serialize(message);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);
            
            // Read response
            var buffer = new byte[1024];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            return JsonSerializer.Deserialize<MessageResponse>(response);
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<MessageResponse?> SendHeartbeatAsync()
    {
        var request = new MessageRequest
        {
            Type = MessageType.Heartbeat,
            Data = new { Status = "Ping" }
        };
        
        return await SendMessageAsync(request);
    }
    
    public async Task<MessageResponse?> CreateAccountAsync(string username, string password, bool isAdmin = true)
    {
        if (!IsConnected)
        {
            var connected = await ConnectAsync();
            if (!connected) return new MessageResponse { Type = MessageType.Error, Success = false, ErrorMessage = "Unable to connect to server" };
        }
        
        var request = new MessageRequest
        {
            Type = MessageType.CreateAccount,
            Data = new CreateAccountData { Username = username, Password = password, IsAdmin = isAdmin }
        };
        
        return await SendMessageAsync(request);
    }
    
    public async Task<MessageResponse?> LoginAsync(string username, string password)
    {
        if (!IsConnected)
        {
            var connected = await ConnectAsync();
            if (!connected) return new MessageResponse { Type = MessageType.Error, Success = false, ErrorMessage = "Unable to connect to server" };
        }
        
        var request = new MessageRequest
        {
            Type = MessageType.Login,
            Data = new LoginData { Username = username, Password = password }
        };
        
        return await SendMessageAsync(request);
    }
    
    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }
}
