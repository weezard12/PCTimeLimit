using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCTimeLimitAdmin.Configuration;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimitAdmin.Services;

public sealed class TcpClientService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private string _accessToken = string.Empty;
    private string _refreshToken = string.Empty;
    private DateTime _accessTokenExpiresAtUtc;

    public TcpClientService()
    {
        ApiBaseUrl = ServerConfig.GetApiBaseUrl();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl + "/"),
            Timeout = TimeSpan.FromMilliseconds(ServerConfig.ConnectionTimeoutMs)
        };
    }

    public string ApiBaseUrl { get; }
    public string Username { get; private set; } = string.Empty;
    public string AdminCode { get; private set; } = string.Empty;

    public bool IsConnected => !string.IsNullOrWhiteSpace(_accessToken)
        && _accessTokenExpiresAtUtc > DateTime.UtcNow.AddSeconds(10);

    public async Task<bool> ConnectAsync()
    {
        if (IsConnected)
        {
            return true;
        }

        var savedSession = await AdminSessionStore.LoadAsync();
        if (savedSession is null)
        {
            return false;
        }

        var session = savedSession.Value;
        Username = session.Username;
        AdminCode = session.AdminCode;
        _refreshToken = session.RefreshToken;

        return await RefreshAccessTokenAsync();
    }

    public async Task<TokenResponse?> CreateAccountAsync(string username, string password, bool isAdmin = true)
    {
        var response = await SendAnonymousAsync<RegisterAdminRequest, TokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/register-admin",
            new RegisterAdminRequest { Username = username, Password = password });

        if (response?.Success == true)
        {
            await ApplySessionAsync(response, saveSession: true);
        }

        return response;
    }

    public async Task<TokenResponse?> LoginAsync(string username, string password)
    {
        var response = await SendAnonymousAsync<LoginRequest, TokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/login",
            new LoginRequest { Username = username, Password = password });

        if (response?.Success == true)
        {
            await ApplySessionAsync(response, saveSession: true);
        }

        return response;
    }

    public async Task<bool> SendHeartbeatAsync()
    {
        var response = await _httpClient.GetAsync("health/live");
        return response.IsSuccessStatusCode;
    }

    public async Task<ComputersResponse?> GetComputersForAdminAsync()
    {
        return await SendAuthorizedAsync<object, ComputersResponse>(HttpMethod.Get, "api/v1/admin/computers", null);
    }

    public async Task<QueueActionResponse?> SetComputerTimeLimitAsync(string computerId, TimeSpan dailyLimit)
    {
        return await SendAuthorizedAsync<SetTimeLimitRequest, QueueActionResponse>(
            HttpMethod.Put,
            $"api/v1/admin/computers/{Uri.EscapeDataString(computerId)}/time-limit",
            new SetTimeLimitRequest { DailyTimeLimit = dailyLimit });
    }

    public async Task<QueueActionResponse?> SetComputerAllowedUsageAsync(string computerId, string allowedUsageJson)
    {
        return await SendAuthorizedAsync<SetAllowedUsageRequest, QueueActionResponse>(
            HttpMethod.Put,
            $"api/v1/admin/computers/{Uri.EscapeDataString(computerId)}/allowed-usage",
            new SetAllowedUsageRequest { AllowedUsageJson = allowedUsageJson ?? string.Empty });
    }

    public async Task<QueueActionResponse?> ResetComputerTimerAsync(string computerId)
    {
        return await SendAuthorizedAsync<object, QueueActionResponse>(
            HttpMethod.Post,
            $"api/v1/admin/computers/{Uri.EscapeDataString(computerId)}/reset",
            null);
    }

    public async Task<QueueActionResponse?> ForceLockoutAsync(string computerId)
    {
        return await SendAuthorizedAsync<object, QueueActionResponse>(
            HttpMethod.Post,
            $"api/v1/admin/computers/{Uri.EscapeDataString(computerId)}/force-lockout",
            null);
    }

    public async Task LogoutAsync()
    {
        if (!string.IsNullOrWhiteSpace(_refreshToken))
        {
            await SendAnonymousAsync<LogoutRequest, object>(
                HttpMethod.Post,
                "api/v1/auth/logout",
                new LogoutRequest { RefreshToken = _refreshToken });
        }

        await AdminSessionStore.ClearAsync();
        ClearInMemoryTokens();
    }

    private async Task<bool> RefreshAccessTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_refreshToken))
        {
            return false;
        }

        var response = await SendAnonymousAsync<RefreshTokenRequest, TokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = _refreshToken });

        if (response?.Success != true)
        {
            await AdminSessionStore.ClearAsync();
            ClearInMemoryTokens();
            return false;
        }

        await ApplySessionAsync(response, saveSession: true);
        return true;
    }

    private async Task ApplySessionAsync(TokenResponse response, bool saveSession)
    {
        _accessToken = response.AccessToken;
        _refreshToken = response.RefreshToken;
        _accessTokenExpiresAtUtc = response.AccessTokenExpiresAtUtc;
        Username = response.Username;
        AdminCode = response.AdminCode ?? string.Empty;

        if (saveSession)
        {
            await AdminSessionStore.SaveAsync(Username, _refreshToken, AdminCode);
        }
    }

    private void ClearInMemoryTokens()
    {
        _accessToken = string.Empty;
        _refreshToken = string.Empty;
        _accessTokenExpiresAtUtc = DateTime.MinValue;
        Username = string.Empty;
        AdminCode = string.Empty;
    }

    private async Task<TResponse?> SendAuthorizedAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest? payload)
    {
        if (!IsConnected && !await RefreshAccessTokenAsync())
        {
            return default;
        }

        var response = await SendRequestAsync(method, url, payload, includeAuth: true);
        if (response is null)
        {
            return default;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (!await RefreshAccessTokenAsync())
            {
                return default;
            }

            response.Dispose();
            response = await SendRequestAsync(method, url, payload, includeAuth: true);
            if (response is null)
            {
                return default;
            }
        }

        return await DeserializeAsync<TResponse>(response);
    }

    private async Task<TResponse?> SendAnonymousAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest? payload)
    {
        var response = await SendRequestAsync(method, url, payload, includeAuth: false);
        if (response is null)
        {
            return default;
        }

        return await DeserializeAsync<TResponse>(response);
    }

    private async Task<HttpResponseMessage?> SendRequestAsync<TRequest>(HttpMethod method, string url, TRequest? payload, bool includeAuth)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (payload is not null)
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            if (includeAuth)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            return await _httpClient.SendAsync(request);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<TResponse?> DeserializeAsync<TResponse>(HttpResponseMessage response)
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync();

        if (!response.IsSuccessStatusCode)
        {
            var error = await TryReadMessageAsync(responseStream);
            if (typeof(TResponse) == typeof(TokenResponse))
            {
                return (TResponse?)(object?)new TokenResponse
                {
                    Success = false,
                    Message = error ?? response.ReasonPhrase ?? "Request failed"
                };
            }

            if (typeof(TResponse) == typeof(QueueActionResponse))
            {
                return (TResponse?)(object?)new QueueActionResponse
                {
                    Success = false,
                    Message = error ?? response.ReasonPhrase ?? "Request failed"
                };
            }

            if (typeof(TResponse) == typeof(ComputersResponse))
            {
                return (TResponse?)(object?)new ComputersResponse
                {
                    Success = false,
                    Message = error ?? response.ReasonPhrase ?? "Request failed"
                };
            }

            return default;
        }

        return await JsonSerializer.DeserializeAsync<TResponse>(responseStream, JsonOptions);
    }

    private static async Task<string?> TryReadMessageAsync(Stream stream)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }

            if (document.RootElement.TryGetProperty("title", out var title))
            {
                return title.GetString();
            }
        }
        catch
        {
            // ignore parse errors
        }

        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed class AdminSessionRecord
{
    public string Username { get; set; } = string.Empty;
    public string AdminCode { get; set; } = string.Empty;
    public string RefreshTokenProtected { get; set; } = string.Empty;
}

internal static class AdminSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SessionFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCTimeLimitAdmin");
    private static string SessionFilePath => Path.Combine(SessionFolder, "session.json");

    public static async Task SaveAsync(string username, string refreshToken, string adminCode)
    {
        Directory.CreateDirectory(SessionFolder);

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(refreshToken),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        var session = new AdminSessionRecord
        {
            Username = username,
            AdminCode = adminCode,
            RefreshTokenProtected = Convert.ToBase64String(protectedBytes)
        };

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(SessionFilePath, json);
    }

    public static async Task<(string Username, string RefreshToken, string AdminCode)?> LoadAsync()
    {
        if (!File.Exists(SessionFilePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SessionFilePath);
            var session = JsonSerializer.Deserialize<AdminSessionRecord>(json);
            if (session is null || string.IsNullOrWhiteSpace(session.RefreshTokenProtected))
            {
                return null;
            }

            var encrypted = Convert.FromBase64String(session.RefreshTokenProtected);
            var plainBytes = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            var refreshToken = Encoding.UTF8.GetString(plainBytes);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            return (session.Username, refreshToken, session.AdminCode);
        }
        catch
        {
            return null;
        }
    }

    public static Task ClearAsync()
    {
        if (File.Exists(SessionFilePath))
        {
            File.Delete(SessionFilePath);
        }

        return Task.CompletedTask;
    }
}
