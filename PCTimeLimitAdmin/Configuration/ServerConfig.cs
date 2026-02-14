using System.Text.Json;
using System.IO;

namespace PCTimeLimitAdmin.Configuration;

public static class ServerConfig
{
    private const string DefaultApiBaseUrl = "https://pctimelimit.example";

    public const int ConnectionTimeoutMs = 5000;
    public const int MinUsernameLength = 3;
    public const int MinPasswordLength = 6;
    public const int MaxUsernameLength = 50;
    public const int MaxPasswordLength = 100;

    public static string GetApiBaseUrl()
    {
        var overrideUrl = Environment.GetEnvironmentVariable("PCTIMELIMIT_API_BASEURL");
        if (TryNormalizeBaseUrl(overrideUrl, out var normalizedOverride))
        {
            return normalizedOverride;
        }

        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
                if (document.RootElement.TryGetProperty("Api", out var apiElement)
                    && apiElement.TryGetProperty("BaseUrl", out var baseUrlElement)
                    && TryNormalizeBaseUrl(baseUrlElement.GetString(), out var normalizedFromFile))
                {
                    return normalizedFromFile;
                }
            }
            catch
            {
                // Fall back to default.
            }
        }

        return DefaultApiBaseUrl;
    }

    private static bool TryNormalizeBaseUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = uri.ToString().TrimEnd('/');
        return true;
    }
}
