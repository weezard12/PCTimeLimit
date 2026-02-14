using Microsoft.EntityFrameworkCore;
using PCTimeLimitServer.Domain.Entities;
using PCTimeLimitServer.Infrastructure;
using PCTimeLimitServer.Infrastructure.Security;

namespace PCTimeLimitServer.Api;

public sealed class DeviceAuthResult
{
    public bool IsAuthenticated { get; init; }
    public DeviceCredential? DeviceCredential { get; init; }
    public Computer? Computer { get; init; }
}

public static class DeviceAuthenticator
{
    public static async Task<DeviceAuthResult> AuthenticateAsync(HttpContext httpContext, PCTimeLimitDbContext dbContext, CancellationToken cancellationToken)
    {
        var token = ExtractBearerToken(httpContext.Request.Headers.Authorization.ToString());
        if (string.IsNullOrWhiteSpace(token))
        {
            return new DeviceAuthResult { IsAuthenticated = false };
        }

        var tokenHash = TokenUtility.HashToken(token);
        var now = DateTime.UtcNow;

        var credential = await dbContext.DeviceCredentials
            .Include(x => x.Computer)
            .ThenInclude(x => x.AdminUser)
            .Include(x => x.Computer)
            .ThenInclude(x => x.AllowedUsageRanges)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (credential is null || credential.RevokedAtUtc is not null || credential.ExpiresAtUtc <= now)
        {
            return new DeviceAuthResult { IsAuthenticated = false };
        }

        return new DeviceAuthResult
        {
            IsAuthenticated = true,
            DeviceCredential = credential,
            Computer = credential.Computer
        };
    }

    private static string? ExtractBearerToken(string authorizationHeader)
    {
        const string bearerPrefix = "Bearer ";
        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader[bearerPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
