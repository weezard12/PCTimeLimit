using System;

namespace PCTimeLimitServer;


public class RegisterComputerData
{
    public string ComputerId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string AdminUsername { get; set; } = "";
    public string? AdminCode { get; set; }
}

public class GetComputersForAdminData
{
    public string AdminUsername { get; set; } = "";
    public string? AdminCode { get; set; }
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

public class ForceLockoutData
{
    public string ComputerId { get; set; } = "";
    public string AdminUsername { get; set; } = "";
}

public class AcknowledgeForceLockoutData
{
    public string ComputerId { get; set; } = "";
}

public class SetComputerAllowedUsageData
{
    public string ComputerId { get; set; } = "";
    public string AllowedUsageJson { get; set; } = "";
    public string AdminUsername { get; set; } = "";
}
