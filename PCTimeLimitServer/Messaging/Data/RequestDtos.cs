using System;

namespace PCTimeLimitServer;

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

public class ResetComputerTimerData
{
    public string ComputerId { get; set; } = "";
    public string AdminUsername { get; set; } = "";
}

public class AcknowledgeResetData
{
    public string ComputerId { get; set; } = "";
}
