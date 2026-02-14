namespace PCTimeLimitServer.Domain.Entities;

public sealed class DeviceCredential
{
    public Guid Id { get; set; }
    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
