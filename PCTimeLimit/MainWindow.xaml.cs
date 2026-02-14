using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.Text;
using System.Threading.Tasks;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimit;

public partial class MainWindow : Window, INotifyPropertyChanged
{
	private DispatcherTimer _uiTimer;
	private TimeManager _timeManager;
	private UsageTracker _usageTracker;
	private string? _adminCode;
	private string? _computerId;
	private ClientService? _clientService;
	private TimeSpan? _serverDailyLimitPending;
	private string? _serverAllowedUsagePending;
	private DispatcherTimer? _syncTimer;
	private bool _syncInProgress;
	private DispatcherTimer? _reconnectTimer;

	public event PropertyChangedEventHandler? PropertyChanged;

	TimesUpWindow? timesUpWindow;

	public MainWindow()
	{
		InitializeComponent();
		Loaded += async (_, _) => await InitializeAsync();
	}

	private async Task InitializeAsync()
	{
		try
		{
			// Try to load saved Admin Code and auto-continue
			var adminCode = await AdminCodeManager.LoadAdminCodeAsync();
			if (!string.IsNullOrWhiteSpace(adminCode) && adminCode.Length == 6)
			{
				await InitializeAppAsync(adminCode);
			}
			else
			{
				// Show login dialog to get Admin Code
				var loginDialog = new LoginDialog();
				var loginResult = loginDialog.ShowDialog();
				
				if (loginResult == true && loginDialog.IsAuthenticated && !string.IsNullOrWhiteSpace(loginDialog.AdminCode))
				{
					// Save the admin code
					if (await AdminCodeManager.SaveAdminCodeAsync(loginDialog.AdminCode))
					{
						// Initialize time manager and register with server
						await InitializeAppAsync(loginDialog.AdminCode);
					}
					else
					{
						MessageBox.Show("Failed to save admin code. Please check application permissions.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
						Application.Current.Shutdown();
						return;
					}
				}
				else
				{
					// User cancelled or authentication failed, close the app
					Application.Current.Shutdown();
					return;
				}
			}
			
			_timeManager = new TimeManager();
			_timeManager.Load();
			// If server provided a daily limit during registration, apply it now
			if (_serverDailyLimitPending.HasValue)
			{
				_timeManager.UpdateDailyLimit(_serverDailyLimitPending.Value);
				_serverDailyLimitPending = null;
			}
			// If server provided allowed usage during registration, apply it now
			if (!string.IsNullOrWhiteSpace(_serverAllowedUsagePending))
			{
				_timeManager.UpdateAllowedUsage(_serverAllowedUsagePending);
				_serverAllowedUsagePending = null;
			}

			_usageTracker = new UsageTracker();
			_usageTracker.Load();
			_usageTracker.Start();

			_uiTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1)
			};
			_uiTimer.Tick += (_, _) => UpdateUi();
			_uiTimer.Start();

			CompositionTarget_Rendering();
			UpdateUi();

			PreventClosing();
			SetRunOnStartup(true);
			// Start periodic sync with server to reflect admin changes
			StartSyncTimer();
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Failed to initialize application: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Application.Current.Shutdown();
		}
    }

	private async Task InitializeAppAsync(string adminCode)
	{
		try
		{
			// Check and handle firewall before attempting connection
			await CheckAndHandleFirewallAsync();
			
			// Generate unique computer ID
			var computerId = Environment.MachineName + "_" + Environment.UserName;
			var computerName = Environment.MachineName;
			
			// Connect to server and register
			var clientService = new ClientService();
			if (await clientService.ConnectAsync())
			{
				var regResult = await clientService.RegisterComputerAsync(computerId, computerName, adminCode);
				if (regResult.Success)
				{
					// Store admin code for future use
					_adminCode = adminCode;
					_computerId = computerId;
					_clientService = clientService;
					
					// Save computer ID to settings
					var settings = new ClientSettings { ComputerId = computerId };
					await SaveClientSettingsAsync(settings);
					
					// Capture server-provided settings (applied after TimeManager is created)
					_serverDailyLimitPending = regResult.DailyLimit;
					_serverAllowedUsagePending = regResult.AllowedUsageJson;
					
					// Start periodic status updates
					StartStatusUpdates();
				}
				else
				{
					MessageBox.Show("Failed to register computer with server. Please check the admin code.", "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Error);
					Application.Current.Shutdown();
					return;
				}
			}
			else
			{
				// Offline mode: proceed to timer and retry connecting every minute
				_adminCode = adminCode;
				_computerId = computerId;
				
				// Save computer ID to settings
				var settings = new ClientSettings { ComputerId = computerId };
				await SaveClientSettingsAsync(settings);
				
				StartReconnectTimer(computerId, computerName, adminCode);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Error initializing app: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Application.Current.Shutdown();
			return;
		}
	}

	private void StartReconnectTimer(string computerId, string computerName, string adminCode)
	{
		_reconnectTimer?.Stop();
		_reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
		_reconnectTimer.Tick += async (_, _) =>
		{
			await TryConnectAndRegisterAsync(computerId, computerName, adminCode);
		};
		_reconnectTimer.Start();
	}

	private async Task TryConnectAndRegisterAsync(string computerId, string computerName, string adminCode)
	{
		try
		{
			if (_clientService?.IsConnected == true) return;
			
			// Check and handle firewall before reconnection attempt
			await CheckAndHandleFirewallAsync();
			
			var client = new ClientService();
			if (!await client.ConnectAsync()) return;
			var reg = await client.RegisterComputerAsync(computerId, computerName, adminCode);
			if (!reg.Success) return;

			_clientService = client;
			// Persist (already saved on startup, but keep it idempotent)
			await SaveClientSettingsAsync(new ClientSettings { ComputerId = computerId });
			// If server provided a daily limit, apply now if _timeManager exists
			_serverDailyLimitPending = reg.DailyLimit;
			if (_timeManager != null && _serverDailyLimitPending.HasValue)
			{
				_timeManager.UpdateDailyLimit(_serverDailyLimitPending.Value);
				_serverDailyLimitPending = null;
			}
			// Start background tasks now that we're connected
			StartStatusUpdates();
			StartSyncTimer();
			_reconnectTimer?.Stop();
		}
		catch { }
	}

	private void StartStatusUpdates()
	{
		var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
		statusTimer.Tick += async (_, _) =>
		{
			if (_clientService?.IsConnected == true)
			{
				await _clientService.UpdateStatusAsync(_computerId!, true);
			}
		};
		statusTimer.Start();
	}

	private static async Task<ClientSettings?> LoadClientSettingsAsync()
	{
		try
		{
			AppStorage.EnsureFolder();
			if (File.Exists(AppStorage.ClientFilePath))
			{
				var json = await File.ReadAllTextAsync(AppStorage.ClientFilePath);
				return JsonSerializer.Deserialize<ClientSettings>(json);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Error loading client settings: {ex.Message}");
		}
		return null;
	}

	private static async Task<bool> SaveClientSettingsAsync(ClientSettings settings)
	{
		const int maxRetries = 3;
		int attempt = 0;
		
		while (attempt < maxRetries)
		{
			try
			{
				// Ensure the directory exists
				AppStorage.EnsureFolder();
				
				// Write to a temporary file first
				var tempFile = Path.Combine(AppStorage.AppFolder, Path.GetRandomFileName());
				var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
				
				// Write to temp file asynchronously
				await File.WriteAllTextAsync(tempFile, json);
				
				// If we get here, write was successful, now replace the original file
				if (File.Exists(AppStorage.ClientFilePath))
				{
					File.Replace(tempFile, AppStorage.ClientFilePath, null);
				}
				else
				{
					File.Move(tempFile, AppStorage.ClientFilePath);
				}
				
				// If we get here, everything worked
				return true;
			}
			catch (UnauthorizedAccessException ex)
			{
				attempt++;
				if (attempt >= maxRetries)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to save client settings after {maxRetries} attempts: {ex.Message}");
					return false;
				}
				await Task.Delay(100);
			}
			catch (IOException ex)
			{
				attempt++;
				if (attempt >= maxRetries)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to save client settings after {maxRetries} attempts: {ex.Message}");
					return false;
				}
				await Task.Delay(100);
			}
			catch (Exception ex) when (attempt < maxRetries - 1)
			{
				attempt++;
				await Task.Delay(100);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to save client settings: {ex.Message}");
				return false;
			}
		}
		return false;
	}

	private void StartSyncTimer()
	{
		_syncTimer?.Stop();
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
		_syncTimer.Tick += async (_, _) =>
		{
			await SyncDailyLimitFromServerAsync("poll");
		};
		_syncTimer.Start();
		// Also do an initial sync shortly after startup
		_ = SyncDailyLimitFromServerAsync("startup");
	}

	private void UpdateUi()
	{
		RemainingTimeText.Text = _timeManager.Remaining.ToString();
        // Close lockout while within allowed windows as well
        if(_timeManager.Remaining > TimeSpan.Zero || _timeManager.IsWithinAllowedWindow(DateTime.Now))
		{
			if(timesUpWindow != null)
			{
				timesUpWindow.ForceClose();
				timesUpWindow = null;
			}
		}
	}

	// Drive countdown independent of UI render
	private void CompositionTarget_Rendering()
	{
		var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		timer.Tick += (_, _) =>
		{
            _timeManager.TickOneSecond();
            // If we're within an allowed window, never trigger lockout or decrement
            if (_timeManager.IsWithinAllowedWindow(DateTime.Now))
            {
                UpdateUi();
                return;
            }
            if (_timeManager.Remaining <= TimeSpan.Zero)
			{
				_timeManager.Remaining = TimeSpan.Zero;
				UpdateUi();
				ShowLockout();
				// When time is over, check if admin updated the limit and apply
				_ = SyncDailyLimitFromServerAsync("timerEnded");
			}
		};
		timer.Start();
	}

	private void ShowLockout()
	{
		if(timesUpWindow == null)
		{
            timesUpWindow = new TimesUpWindow();
            timesUpWindow.Show();
        }
	}

	public void SetRunOnStartup(bool enable)
	{
		const string runKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
		using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(runKeyPath, true);
		if (key is null) return;

		const string valueName = "PCTimeLimit";
		if (enable)
		{
			var exePath = GetExecutablePath();
			if (!string.IsNullOrWhiteSpace(exePath))
			{
				key.SetValue(valueName, $"\"{exePath}\"");
			}
		}
		else
		{
			key.DeleteValue(valueName, false);
		}
	}

	public bool IsRunOnStartupEnabled()
	{
		const string runKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
		using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false);
		if (key is null) return false;
		var value = key.GetValue("PCTimeLimit") as string;
		return !string.IsNullOrWhiteSpace(value);
	}

	private static string GetExecutablePath()
	{
		try
		{
			return Process.GetCurrentProcess().MainModule?.FileName
					?? System.Reflection.Assembly.GetEntryAssembly()?.Location
					?? Environment.ProcessPath
					?? string.Empty;
		}
		catch
		{
			return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
		}
	}

    private void PreventClosing()
    {
        this.Closing += (s, e) =>
        {
            e.Cancel = true;
        };
        this.Loaded += (s, e) =>
        {
            this.Activate();
            this.Focus();
        };
    }

    private async Task SyncDailyLimitFromServerAsync(string reason)
    {
        if (_syncInProgress) return;
        if (_clientService?.IsConnected != true || string.IsNullOrWhiteSpace(_adminCode) || string.IsNullOrWhiteSpace(_computerId)) return;
        try
        {
            _syncInProgress = true;
            var state = await _clientService.GetComputerStateAsync(_adminCode!, _computerId!);
            if (state != null)
            {
                if (state.DailyLimit.HasValue && state.DailyLimit.Value > TimeSpan.Zero && state.DailyLimit.Value != _timeManager.DailyLimit)
                {
                    _timeManager.UpdateDailyLimit(state.DailyLimit.Value);
                    UpdateUi();
                }
                if (!string.IsNullOrWhiteSpace(state.AllowedUsageJson))
                {
                    _timeManager.UpdateAllowedUsage(state.AllowedUsageJson);
                }
                if (state.PendingReset)
                {
                    // Reset remaining to the daily limit immediately
                    _timeManager.UpdateDailyLimit(_timeManager.DailyLimit);
                    UpdateUi();
                    // Acknowledge to server so it clears the queue
                    _ = _clientService.AcknowledgeResetAsync(_computerId!);
                }
                if (state.PendingForceLockout)
                {
                    // Force immediate lockout without changing daily limit
                    _timeManager.Remaining = TimeSpan.Zero;
                    UpdateUi();
                    ShowLockout();
                    // Acknowledge to server so it clears the queue
                    _ = _clientService.AcknowledgeForceLockoutAsync(_computerId!);
                }
            }
        }
        catch { }
        finally { _syncInProgress = false; }
    }

	private async Task CheckAndHandleFirewallAsync()
	{
		await Task.CompletedTask;
	}
}

public sealed class ClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private string? _deviceToken;

    public ClientService()
    {
        _apiBaseUrl = ClientApiConfig.GetApiBaseUrl();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_apiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public bool IsConnected { get; private set; }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("health/live");
            IsConnected = response.IsSuccessStatusCode;
            return IsConnected;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    public sealed class RegisterComputerResult
    {
        public bool Success { get; set; }
        public TimeSpan DailyLimit { get; set; }
        public string? AllowedUsageJson { get; set; }
    }

    public async Task<RegisterComputerResult> RegisterComputerAsync(string computerId, string computerName, string adminCode)
    {
        if (!IsConnected)
        {
            return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
        }

        _deviceToken = await LoadDeviceTokenAsync(computerId);
        var existingState = await GetComputerStateInternalAsync();
        if (existingState is not null)
        {
            return new RegisterComputerResult
            {
                Success = true,
                DailyLimit = existingState.DailyLimit ?? TimeSpan.Zero,
                AllowedUsageJson = existingState.AllowedUsageJson
            };
        }

        try
        {
            var request = new RegisterChildRequest
            {
                ComputerId = computerId,
                ComputerName = computerName,
                AdminCode = adminCode
            };

            var response = await SendAsync<RegisterChildRequest, RegisterChildResponse>(HttpMethod.Post, "api/v1/child/register", request, includeDeviceAuth: false);
            if (response is null || !response.Success || string.IsNullOrWhiteSpace(response.DeviceToken))
            {
                return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
            }

            _deviceToken = response.DeviceToken;
            await SaveDeviceTokenAsync(computerId, response.DeviceToken);

            return new RegisterComputerResult
            {
                Success = true,
                DailyLimit = response.DailyLimit,
                AllowedUsageJson = response.AllowedUsageJson
            };
        }
        catch
        {
            return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
        }
    }

    public async Task<bool> UpdateStatusAsync(string computerId, bool isOnline)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_deviceToken))
        {
            return false;
        }

        var response = await SendAsync<UpdateStatusRequest, QueueActionResponse>(
            HttpMethod.Post,
            "api/v1/child/status",
            new UpdateStatusRequest { IsOnline = isOnline },
            includeDeviceAuth: true);

        return response?.Success == true;
    }

    public sealed class ComputerState
    {
        public TimeSpan? DailyLimit { get; set; }
        public bool PendingReset { get; set; }
        public bool PendingForceLockout { get; set; }
        public string? AllowedUsageJson { get; set; }
    }

    public async Task<ComputerState?> GetComputerStateAsync(string adminCode, string computerId)
    {
        if (string.IsNullOrWhiteSpace(_deviceToken))
        {
            _deviceToken = await LoadDeviceTokenAsync(computerId);
        }

        return await GetComputerStateInternalAsync();
    }

    public async Task<bool> AcknowledgeResetAsync(string computerId)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_deviceToken))
        {
            return false;
        }

        var response = await SendAsync<object, QueueActionResponse>(HttpMethod.Post, "api/v1/child/ack-reset", null, includeDeviceAuth: true);
        return response?.Success == true;
    }

    public async Task<bool> AcknowledgeForceLockoutAsync(string computerId)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_deviceToken))
        {
            return false;
        }

        var response = await SendAsync<object, QueueActionResponse>(HttpMethod.Post, "api/v1/child/ack-force-lockout", null, includeDeviceAuth: true);
        return response?.Success == true;
    }

    private async Task<ComputerState?> GetComputerStateInternalAsync()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_deviceToken))
        {
            return null;
        }

        var response = await SendAsync<object, ComputerStateResponse>(HttpMethod.Get, "api/v1/child/state", null, includeDeviceAuth: true);
        if (response?.Success != true)
        {
            return null;
        }

        return new ComputerState
        {
            DailyLimit = response.DailyLimit,
            PendingReset = response.PendingReset,
            PendingForceLockout = response.PendingForceLockout,
            AllowedUsageJson = response.AllowedUsageJson
        };
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest? payload, bool includeDeviceAuth)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (payload is not null)
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            if (includeDeviceAuth && !string.IsNullOrWhiteSpace(_deviceToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _deviceToken);
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static async Task SaveDeviceTokenAsync(string computerId, string token)
    {
        try
        {
            AppStorage.EnsureFolder();
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            var payload = new DeviceTokenRecord
            {
                ComputerId = computerId,
                TokenProtected = Convert.ToBase64String(encrypted)
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(AppStorage.DeviceTokenFilePath, json);
        }
        catch
        {
            // best effort
        }
    }

    private static async Task<string?> LoadDeviceTokenAsync(string computerId)
    {
        try
        {
            if (!File.Exists(AppStorage.DeviceTokenFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(AppStorage.DeviceTokenFilePath);
            var payload = JsonSerializer.Deserialize<DeviceTokenRecord>(json);
            if (payload is null || !string.Equals(payload.ComputerId, computerId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(payload.TokenProtected))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(payload.TokenProtected);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public void Disconnect()
    {
        _httpClient.Dispose();
        IsConnected = false;
        _deviceToken = null;
    }
}

public sealed class DeviceTokenRecord
{
    public string ComputerId { get; set; } = string.Empty;
    public string TokenProtected { get; set; } = string.Empty;
}

public static class ClientApiConfig
{
    private const string DefaultApiBaseUrl = "https://pctimelimit.example";

    public static string GetApiBaseUrl()
    {
        var env = Environment.GetEnvironmentVariable("PCTIMELIMIT_API_BASEURL");
        if (TryNormalize(env, out var fromEnv))
        {
            return fromEnv;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Api", out var api)
                    && api.TryGetProperty("BaseUrl", out var baseUrl)
                    && TryNormalize(baseUrl.GetString(), out var fromFile))
                {
                    return fromFile;
                }
            }
            catch
            {
                // use default
            }
        }

        return DefaultApiBaseUrl;
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
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
public sealed class AppStorage
{
	public static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCTimeLimit");
	public static string SettingsFilePath => Path.Combine(AppFolder, "settings.json");
	public static string UsageFilePath => Path.Combine(AppFolder, "usage.json");
	public static string ClientFilePath => Path.Combine(AppFolder, "client.json");
	public static string AdminCodeFilePath => Path.Combine(AppFolder, "admin_code.txt");
	public static string DeviceTokenFilePath => Path.Combine(AppFolder, "device_token.json");

	public static void EnsureFolder()
	{
		if (!Directory.Exists(AppFolder))
		{
			Directory.CreateDirectory(AppFolder);
		}
	}
}

public static class AdminCodeManager
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 100;

    public static async Task<bool> SaveAdminCodeAsync(string adminCode)
    {
        int attempt = 0;
        while (attempt < MaxRetries)
        {
            try
            {
                AppStorage.EnsureFolder();
                var tempFile = Path.Combine(AppStorage.AppFolder, Path.GetRandomFileName());
                await File.WriteAllTextAsync(tempFile, adminCode);
                
                if (File.Exists(AppStorage.AdminCodeFilePath))
                {
                    File.Replace(tempFile, AppStorage.AdminCodeFilePath, null);
                }
                else
                {
                    File.Move(tempFile, AppStorage.AdminCodeFilePath);
                }
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                attempt++;
                if (attempt >= MaxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save admin code after {MaxRetries} attempts: {ex.Message}");
                    return false;
                }
                await Task.Delay(RetryDelayMs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error saving admin code: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    public static async Task<string?> LoadAdminCodeAsync()
    {
        if (!File.Exists(AppStorage.AdminCodeFilePath))
            return null;

        try
        {
            var content = await File.ReadAllTextAsync(AppStorage.AdminCodeFilePath);
            return content.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading admin code: {ex.Message}");
            return null;
        }
    }
}

public sealed class ClientSettings
{
    public string ComputerId { get; set; } = string.Empty;
}

public sealed class AppSettings
{
	public TimeSpan DailyLimit { get; set; } = TimeSpan.FromHours(1);
	public string Password { get; set; } = "";
	public DateTime DateUtc { get; set; } = DateTime.Today;
	public TimeSpan RemainingForDate { get; set; } = TimeSpan.FromHours(1);
    public string AllowedUsageJson { get; set; } = "";
}

public sealed class UsageTracker
{
	private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
	private UsageData _data = new();

	public event Action<string>? UsageUpdated; // arg: dateKey (yyyy-MM-dd)

	public void Start()
	{
		_timer.Tick += (_, _) => Sample();
		_timer.Start();
	}

	public void Load()
	{
		AppStorage.EnsureFolder();
		if (File.Exists(AppStorage.UsageFilePath))
		{
			try
			{
				var json = File.ReadAllText(AppStorage.UsageFilePath);
				var loaded = JsonSerializer.Deserialize<UsageData>(json);
				if (loaded != null)
				{
					_data = loaded;
				}
			}
			catch { }
		}
	}

	public void Save()
	{
		AppStorage.EnsureFolder();
		var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(AppStorage.UsageFilePath, json);
	}

	public IReadOnlyDictionary<string, TimeSpan> GetUsageForDate(DateTime date)
	{
		var key = date.Date.ToString("yyyy-MM-dd");
		if (_data.Days.TryGetValue(key, out var perApp))
		{
			return perApp.ToDictionary(kv => kv.Key, kv => TimeSpan.FromMinutes(kv.Value));
		}
		return new Dictionary<string, TimeSpan>();
	}

	private void Sample()
	{
		if (TimeManager.IsWorkstationLocked()) return;
		var appId = GetForegroundAppIdentifier();
		if (string.IsNullOrWhiteSpace(appId)) return;
		if (string.Equals(appId, "Program Manager", StringComparison.OrdinalIgnoreCase)) return;

		var dayKey = DateTime.Today.ToString("yyyy-MM-dd");
		if (!_data.Days.TryGetValue(dayKey, out var perApp))
		{
			perApp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			_data.Days[dayKey] = perApp;
		}
		perApp.TryGetValue(appId, out var minutes);
		perApp[appId] = minutes + 1; // add one minute sample
		Save();
		UsageUpdated?.Invoke(dayKey);
	}

	private static string GetForegroundAppIdentifier()
	{
		var hwnd = TimeManager.GetForegroundWindow();
		if (hwnd == IntPtr.Zero) return string.Empty;
		uint pid;
		GetWindowThreadProcessId(hwnd, out pid);
		try
		{
			using var proc = Process.GetProcessById((int)pid);
			var name = proc.ProcessName;
			var title = TimeManager.GetForegroundWindowTitle();
			return string.IsNullOrWhiteSpace(title) ? name : $"{name} - {title}";
		}
		catch
		{
			return string.Empty;
		}
	}

	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}

public sealed class UsageData
{
	public Dictionary<string, Dictionary<string, int>> Days { get; set; } = new(); // dateKey -> appId -> minutes
}

public sealed class TimeManager
{
	private AppSettings _settings = new();
    private Dictionary<DayOfWeek, List<(TimeSpan start, TimeSpan end)>> _allowedWindows = new();

	public TimeSpan DailyLimit => _settings.DailyLimit;

	public TimeSpan Remaining
	{
		get => _settings.RemainingForDate;
		set { _settings.RemainingForDate = value; Save(); }
	}

	public void Load()
	{
		AppStorage.EnsureFolder();
		if (File.Exists(AppStorage.SettingsFilePath))
		{
			try
			{
				var json = File.ReadAllText(AppStorage.SettingsFilePath);
				var loaded = JsonSerializer.Deserialize<AppSettings>(json);
				if (loaded != null)
				{
					_settings = loaded;
				}
			}
			catch
			{
				// ignore
			}
		}

		EnsureDate();
        // Initialize allowed windows cache from stored JSON
        ApplyAllowedUsageJson(_settings.AllowedUsageJson);
	}

	public void Save()
	{
		AppStorage.EnsureFolder();
		var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(AppStorage.SettingsFilePath, json);
	}

	public bool VerifyPassword(string input) => string.Equals(input ?? string.Empty, _settings.Password ?? string.Empty, StringComparison.Ordinal);

	public void UpdateDailyLimit(TimeSpan newLimit)
	{
		_settings.DailyLimit = newLimit;
		EnsureDate(resetToDailyLimit: true);
		Save();
	}

	public void UpdatePassword(string newPassword)
	{
		_settings.Password = newPassword ?? string.Empty;
		Save();
	}

    public void UpdateAllowedUsage(string? allowedUsageJson)
    {
        _settings.AllowedUsageJson = allowedUsageJson ?? string.Empty;
        ApplyAllowedUsageJson(_settings.AllowedUsageJson);
        Save();
    }

    private void ApplyAllowedUsageJson(string? json)
    {
        _allowedWindows = new Dictionary<DayOfWeek, List<(TimeSpan start, TimeSpan end)>>();
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var kv in new[] { ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday), ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday), ("sunday", DayOfWeek.Sunday) })
            {
                if (!root.TryGetProperty(kv.Item1, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                var ranges = new List<(TimeSpan, TimeSpan)>();
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("start", out var sEl) || !el.TryGetProperty("end", out var eEl)) continue;
                    var sStr = sEl.GetString();
                    var eStr = eEl.GetString();
                    if (TimeSpan.TryParse(sStr, out var sTs) && TimeSpan.TryParse(eStr, out var eTs))
                    {
                        ranges.Add((sTs, eTs));
                    }
                }
                if (ranges.Count > 0)
                {
                    _allowedWindows[kv.Item2] = ranges;
                }
            }
        }
        catch { }
    }

	public string GetPassword()
	{
		return _settings.Password ?? string.Empty;
	}

	public void TickOneSecond()
	{
		// Ensure daily reset at local midnight; this also handles the case when the PC was off
		// because we compare the stored date against today's date on every tick and on Load().
		EnsureDate();
		// If we are within an allowed usage window, pause the timer entirely
		if (IsWithinAllowedWindow(DateTime.Now))
		{
			return;
		}
		if (_settings.RemainingForDate <= TimeSpan.Zero)
		{
			return;
		}

        if (ShouldDecrement())
		{
			_settings.RemainingForDate -= TimeSpan.FromSeconds(1);
			if (_settings.RemainingForDate < TimeSpan.Zero)
			{
				_settings.RemainingForDate = TimeSpan.Zero;
			}
			Save();
		}
	}

	private void EnsureDate(bool resetToDailyLimit = false)
	{
		var today = DateTime.Today;
		if (_settings.DateUtc != today)
		{
			_settings.DateUtc = today;
			_settings.RemainingForDate = _settings.DailyLimit;
			Save();
		}
		else if (resetToDailyLimit)
		{
			_settings.RemainingForDate = _settings.DailyLimit;
			Save();
		}
	}

    private bool ShouldDecrement()
	{
		// Conditions:
		// - Session must be unlocked
		// - Not on desktop shell window in foreground (i.e., some app is in foreground)

		if (IsWorkstationLocked())
		{
			return false;
		}

		var title = GetForegroundWindowTitle();
		if (string.IsNullOrWhiteSpace(title))
		{
			return false;
		}

		// If foreground title equals "Program Manager" (Explorer desktop), treat as desktop
		if (string.Equals(title.Trim(), "Program Manager", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

        // If current time is within allowed usage windows, do NOT decrement
        var now = DateTime.Now;
        if (IsWithinAllowedWindow(now))
        {
            return false;
        }

		return true;
	}

    public bool IsWithinAllowedWindow(DateTime localNow)
    {
        if (_allowedWindows == null || _allowedWindows.Count == 0) return false;
        var dow = localNow.DayOfWeek;
        if (!_allowedWindows.TryGetValue(dow, out var ranges) || ranges == null || ranges.Count == 0) return false;
        var timeOfDay = localNow.TimeOfDay;
        foreach (var (start, end) in ranges)
        {
            if (start <= end)
            {
                if (timeOfDay >= start && timeOfDay <= end) return true;
            }
            else
            {
                // Overnight window, e.g., 22:00-02:00
                if (timeOfDay >= start || timeOfDay <= end) return true;
            }
        }
        return false;
    }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetWindowTextLength(IntPtr hWnd);
	[DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);
	[DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr pMemory);

	private enum WTS_INFO_CLASS
	{
		WTSSessionId = 4,
		WTSConnectState = 8
	}

	internal static string GetForegroundWindowTitle()
	{
		var handle = GetForegroundWindow();
		if (handle == IntPtr.Zero) return string.Empty;
		int length = GetWindowTextLength(handle);
		var sb = new System.Text.StringBuilder(length + 1);
		_ = GetWindowText(handle, sb, sb.Capacity);
		return sb.ToString();
	}

    public static bool IsWorkstationLocked()
	{
		// Simpler heuristic: when there's no foreground window title, consider locked or secure desktop
		var title = GetForegroundWindowTitle();
		return string.IsNullOrWhiteSpace(title);
	}
}


