using System;

namespace PCTimeLimitServer;

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
