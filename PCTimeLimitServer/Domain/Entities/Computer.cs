namespace PCTimeLimitServer.Domain.Entities;

public sealed class Computer
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public Guid AdminUserId { get; set; }
    public AdminUser AdminUser { get; set; } = null!;
    public int DailyTimeLimitSeconds { get; set; } = (int)TimeSpan.FromHours(1).TotalSeconds;
    public DateTime RegisteredAtUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsOnline { get; set; }
    public bool PendingReset { get; set; }
    public bool PendingForceLockout { get; set; }
    public string AllowedUsageJson { get; set; } = string.Empty;

    public DeviceCredential? DeviceCredential { get; set; }
}
