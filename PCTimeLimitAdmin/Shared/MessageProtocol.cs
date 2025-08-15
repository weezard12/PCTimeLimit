using System.Text.Json;

namespace PCTimeLimitAdmin.Shared;

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
