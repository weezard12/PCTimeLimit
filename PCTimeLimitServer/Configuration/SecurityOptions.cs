namespace PCTimeLimitServer.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string JwtIssuer { get; set; } = "pctimelimit-server";
    public string JwtAudience { get; set; } = "pctimelimit-clients";
    public string JwtSigningKey { get; set; } = "CHANGE_ME_WITH_A_LONG_RANDOM_SECRET_KEY";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public int DeviceTokenLifetimeDays { get; set; } = 365;
    public string OpsKey { get; set; } = "CHANGE_ME_OPS_KEY";
}
