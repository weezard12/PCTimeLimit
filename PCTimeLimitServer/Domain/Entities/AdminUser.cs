namespace PCTimeLimitServer.Domain.Entities;

public sealed class AdminUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string AdminCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public List<Computer> Computers { get; set; } = new();
    public List<RefreshToken> RefreshTokens { get; set; } = new();
}
