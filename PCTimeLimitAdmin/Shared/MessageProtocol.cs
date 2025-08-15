using System.Text.Json;

namespace PCTimeLimitAdmin.Shared;

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
