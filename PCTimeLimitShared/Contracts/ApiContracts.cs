namespace PCTimeLimitShared.Contracts;

public static class ApiHeaders
{
    public const string OpsKey = "X-Ops-Key";
}

public sealed class RegisterAdminRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class TokenResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? AdminCode { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }
}

public sealed class RegisterChildRequest
{
    public string ComputerId { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string AdminCode { get; set; } = string.Empty;
}

public sealed class RegisterChildResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public TimeSpan DailyLimit { get; set; }
    public string AllowedUsageJson { get; set; } = string.Empty;
}

public sealed class UpdateStatusRequest
{
    public bool IsOnline { get; set; }
}

public sealed class ComputerStateResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public TimeSpan DailyLimit { get; set; }
    public bool PendingReset { get; set; }
    public bool PendingForceLockout { get; set; }
    public string AllowedUsageJson { get; set; } = string.Empty;
}

public sealed class ComputerDto
{
    public string ComputerId { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public TimeSpan DailyTimeLimit { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsOnline { get; set; }
    public bool PendingReset { get; set; }
    public bool PendingForceLockout { get; set; }
    public string AllowedUsageJson { get; set; } = string.Empty;
}

public sealed class ComputersResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ComputerDto> Computers { get; set; } = new();
}

public sealed class SetTimeLimitRequest
{
    public TimeSpan DailyTimeLimit { get; set; }
}

public sealed class SetAllowedUsageRequest
{
    public string AllowedUsageJson { get; set; } = string.Empty;
}

public sealed class QueueActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class OpsCreateAdminRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class OpsStatusResponse
{
    public int AdminCount { get; set; }
    public int ComputerCount { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public sealed class OpsUsersResponse
{
    public List<OpsUserDto> Users { get; set; } = new();
}

public sealed class OpsUserDto
{
    public string Username { get; set; } = string.Empty;
    public string AdminCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class OpsComputersResponse
{
    public List<ComputerDto> Computers { get; set; } = new();
}
