using System;
using System.Net.Sockets;

namespace PCTimeLimitServer;

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
