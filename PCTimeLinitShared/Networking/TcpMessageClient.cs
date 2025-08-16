using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCTimeLinitShared.Networking;

public class TcpMessageClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    public bool IsConnected => _client?.Connected == true;

    public async Task<bool> ConnectAsync(string host, int port)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            _stream = _client.GetStream();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TResponse?> SendAsync<TResponse>(object request)
    {
        if (!IsConnected || _stream == null) return default;
        try
        {
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream.WriteAsync(data, 0, data.Length);

            var buffer = new byte[1024];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return JsonSerializer.Deserialize<TResponse>(response);
        }
        catch
        {
            return default;
        }
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }
}
