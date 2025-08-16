using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PCTimeLimit;

public partial class MainWindow : Window, INotifyPropertyChanged
{
	private readonly DispatcherTimer _uiTimer;
	private readonly TimeManager _timeManager;
	private readonly UsageTracker _usageTracker;
	private string? _adminUsername;
	private string? _adminPassword;
	private string? _computerId;
	private ClientService? _clientService;
	private TimeSpan? _serverDailyLimitPending;
	private DispatcherTimer? _syncTimer;
	private bool _syncInProgress;

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainWindow()
	{
		InitializeComponent();
		
		// Show login dialog first
		var loginDialog = new LoginDialog();
		var loginResult = loginDialog.ShowDialog();
		
		if (loginResult == true && loginDialog.IsAuthenticated)
		{
			// Initialize time manager and register with server
			InitializeApp(loginDialog.AdminUsername!, loginDialog.AdminPassword!);
		}
		else
		{
			// User cancelled or authentication failed, close the app
			Application.Current.Shutdown();
			return;
		}
		
		_timeManager = new TimeManager();
		_timeManager.Load();
		// If server provided a daily limit during registration, apply it now
		if (_serverDailyLimitPending.HasValue)
		{
			_timeManager.UpdateDailyLimit(_serverDailyLimitPending.Value);
			_serverDailyLimitPending = null;
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

	private async void InitializeApp(string adminUsername, string adminPassword)
	{
		try
		{
			// Generate unique computer ID
			var computerId = Environment.MachineName + "_" + Environment.UserName;
			var computerName = Environment.MachineName;
			
			// Connect to server and register
			var clientService = new ClientService();
			if (await clientService.ConnectAsync())
			{
				var regResult = await clientService.RegisterComputerAsync(computerId, computerName, adminUsername, adminPassword);
				if (regResult.Success)
				{
					// Store admin credentials for future use
					_adminUsername = adminUsername;
					_adminPassword = adminPassword;
					_computerId = computerId;
					_clientService = clientService;
					// Capture server-provided daily limit (applied after TimeManager is created)
					_serverDailyLimitPending = regResult.DailyLimit;
					
					// Start periodic status updates
					StartStatusUpdates();
				}
				else
				{
					MessageBox.Show("Failed to register computer with server. Please check your credentials.", "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Error);
					Application.Current.Shutdown();
					return;
				}
			}
			else
			{
				MessageBox.Show("Failed to connect to server. Please ensure the server is running.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				Application.Current.Shutdown();
				return;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Error initializing app: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Application.Current.Shutdown();
			return;
		}
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

	private void StartSyncTimer()
	{
		_syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
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
	}

	private void OnOpenControlPanelClick(object sender, RoutedEventArgs e)
	{
		var pwd = PasswordBox.Password ?? string.Empty;
		if (_timeManager.VerifyPassword(pwd))
		{
			var cp = new SettingsWindow(_timeManager, _usageTracker);
			cp.Owner = this;
			cp.ShowDialog();
			UpdateUi();
		}
		else
		{
			MessageBox.Show(this, "Wrong password", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	// Drive countdown independent of UI render
	private void CompositionTarget_Rendering()
	{
		var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		timer.Tick += (_, _) =>
		{
			_timeManager.TickOneSecond();
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
		MessageBox.Show(this, "Time is up!", "PC Time Limit", MessageBoxButton.OK, MessageBoxImage.Stop);
		new TimesUpWindow().Show();
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
        if (_clientService?.IsConnected != true || string.IsNullOrWhiteSpace(_adminUsername) || string.IsNullOrWhiteSpace(_computerId)) return;
        try
        {
            _syncInProgress = true;
            var state = await _clientService.GetComputerStateAsync(_adminUsername!, _computerId!);
            if (state != null)
            {
                if (state.DailyLimit.HasValue && state.DailyLimit.Value > TimeSpan.Zero && state.DailyLimit.Value != _timeManager.DailyLimit)
                {
                    _timeManager.UpdateDailyLimit(state.DailyLimit.Value);
                    UpdateUi();
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
}

public sealed class ClientService
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly string _serverAddress = "127.0.0.1"; // Default to localhost
    private readonly int _serverPort = 8888;
    
    public bool IsConnected => _client?.Connected == true;
    
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_serverAddress, _serverPort);
            _stream = _client.GetStream();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public sealed class RegisterComputerResult
    {
        public bool Success { get; set; }
        public TimeSpan DailyLimit { get; set; }
    }

    public async Task<RegisterComputerResult> RegisterComputerAsync(string computerId, string computerName, string adminUsername, string adminPassword)
    {
        if (!IsConnected) return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
        
        try
        {
            var request = new
            {
                Type = 4, // RegisterComputer
                Data = new
                {
                    ComputerId = computerId,
                    ComputerName = computerName,
                    AdminUsername = adminUsername
                }
            };
            
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);
            
            // Read response
            var buffer = new byte[1024];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // Expected response schema from server:
            // { Type, Success, Data: { Success, Message, Computer: { DailyTimeLimit: "hh:mm:ss", ... } } }
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                var success = root.TryGetProperty("Success", out var topSuccess) && topSuccess.GetBoolean();
                TimeSpan limit = TimeSpan.Zero;
                if (root.TryGetProperty("Data", out var dataEl))
                {
                    if (dataEl.ValueKind == JsonValueKind.Object)
                    {
                        if (dataEl.TryGetProperty("Computer", out var compEl))
                        {
                            if (compEl.TryGetProperty("DailyTimeLimit", out var limitEl))
                            {
                                // TimeSpan serialized as string "hh:mm:ss"
                                var s = limitEl.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    TimeSpan.TryParse(s, out limit);
                                }
                            }
                        }
                    }
                }
                return new RegisterComputerResult { Success = success, DailyLimit = limit };
            }
            catch
            {
                return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
            }
        }
        catch
        {
            return new RegisterComputerResult { Success = false, DailyLimit = TimeSpan.Zero };
        }
    }
    
    public async Task<bool> UpdateStatusAsync(string computerId, bool isOnline)
    {
        if (!IsConnected) return false;
        
        try
        {
            var request = new
            {
                Type = 5, // UpdateComputerStatus
                Data = new
                {
                    ComputerId = computerId,
                    IsOnline = isOnline
                }
            };
            
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<TimeSpan?> GetDailyLimitAsync(string adminUsername, string computerId)
    {
        if (!IsConnected) return null;
        try
        {
            var request = new
            {
                Type = 7, // GetComputersForAdmin
                Data = new { AdminUsername = adminUsername }
            };

            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);

            var buffer = new byte[2048];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0) return null;
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Success", out var ok) || !ok.GetBoolean()) return null;
            if (!root.TryGetProperty("Data", out var dataEl)) return null;
            if (dataEl.ValueKind != JsonValueKind.Object) return null;
            if (!dataEl.TryGetProperty("Computers", out var compsEl)) return null;
            if (compsEl.ValueKind != JsonValueKind.Array) return null;

            foreach (var comp in compsEl.EnumerateArray())
            {
                var id = comp.TryGetProperty("ComputerId", out var idEl) ? idEl.GetString() : null;
                if (!string.Equals(id, computerId, StringComparison.OrdinalIgnoreCase)) continue;

                // DailyTimeLimit might be serialized as string or object; handle both
                if (comp.TryGetProperty("DailyTimeLimit", out var limitEl))
                {
                    if (limitEl.ValueKind == JsonValueKind.String)
                    {
                        var s = limitEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && TimeSpan.TryParse(s, out var ts))
                            return ts;
                    }
                    else if (limitEl.ValueKind == JsonValueKind.Number)
                    {
                        // If serialized as ticks or minutes, attempt ticks first
                        if (limitEl.TryGetInt64(out var ticks))
                            return TimeSpan.FromTicks(ticks);
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
    
    public sealed class ComputerState
    {
        public TimeSpan? DailyLimit { get; set; }
        public bool PendingReset { get; set; }
        public bool PendingForceLockout { get; set; }
    }

    public async Task<ComputerState?> GetComputerStateAsync(string adminUsername, string computerId)
    {
        if (!IsConnected) return null;
        try
        {
            var request = new
            {
                Type = 7, // GetComputersForAdmin
                Data = new { AdminUsername = adminUsername }
            };

            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);

            var buffer = new byte[4096];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0) return null;
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Success", out var ok) || !ok.GetBoolean()) return null;
            if (!root.TryGetProperty("Data", out var dataEl)) return null;
            if (dataEl.ValueKind != JsonValueKind.Object) return null;
            if (!dataEl.TryGetProperty("Computers", out var compsEl)) return null;
            if (compsEl.ValueKind != JsonValueKind.Array) return null;

            foreach (var comp in compsEl.EnumerateArray())
            {
                var id = comp.TryGetProperty("ComputerId", out var idEl) ? idEl.GetString() : null;
                if (!string.Equals(id, computerId, StringComparison.OrdinalIgnoreCase)) continue;

                var state = new ComputerState();
                if (comp.TryGetProperty("DailyTimeLimit", out var limitEl))
                {
                    if (limitEl.ValueKind == JsonValueKind.String)
                    {
                        var s = limitEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && TimeSpan.TryParse(s, out var ts))
                            state.DailyLimit = ts;
                    }
                    else if (limitEl.ValueKind == JsonValueKind.Number && limitEl.TryGetInt64(out var ticks))
                    {
                        state.DailyLimit = TimeSpan.FromTicks(ticks);
                    }
                }
                if (comp.TryGetProperty("PendingReset", out var prEl) && prEl.ValueKind == JsonValueKind.True)
                {
                    state.PendingReset = true;
                }
                else if (comp.TryGetProperty("PendingReset", out prEl) && prEl.ValueKind == JsonValueKind.False)
                {
                    state.PendingReset = false;
                }
                if (comp.TryGetProperty("PendingForceLockout", out var pflEl) && pflEl.ValueKind == JsonValueKind.True)
                {
                    state.PendingForceLockout = true;
                }
                else if (comp.TryGetProperty("PendingForceLockout", out pflEl) && pflEl.ValueKind == JsonValueKind.False)
                {
                    state.PendingForceLockout = false;
                }
                return state;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> AcknowledgeResetAsync(string computerId)
    {
        if (!IsConnected) return false;
        try
        {
            var request = new
            {
                Type = 9, // AcknowledgeReset
                Data = new { ComputerId = computerId }
            };
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);

            // best-effort; no need to block on response, but try to read small
            var buffer = new byte[256];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            return bytesRead > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AcknowledgeForceLockoutAsync(string computerId)
    {
        if (!IsConnected) return false;
        try
        {
            var request = new
            {
                Type = 11, // AcknowledgeForceLockout
                Data = new { ComputerId = computerId }
            };
            var json = JsonSerializer.Serialize(request);
            var data = Encoding.UTF8.GetBytes(json);
            await _stream!.WriteAsync(data, 0, data.Length);

            var buffer = new byte[256];
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            return bytesRead > 0;
        }
        catch
        {
            return false;
        }
    }
    
    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }
}

public sealed class AppStorage
{
	public static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCTimeLimit");
	public static string SettingsFilePath => Path.Combine(AppFolder, "settings.json");
	public static string UsageFilePath => Path.Combine(AppFolder, "usage.json");

	public static void EnsureFolder()
	{
		if (!Directory.Exists(AppFolder))
		{
			Directory.CreateDirectory(AppFolder);
		}
	}
}

public sealed class AppSettings
{
	public TimeSpan DailyLimit { get; set; } = TimeSpan.FromHours(1);
	public string Password { get; set; } = "";
	public DateTime DateUtc { get; set; } = DateTime.Today;
	public TimeSpan RemainingForDate { get; set; } = TimeSpan.FromHours(1);
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

	public string GetPassword()
	{
		return _settings.Password ?? string.Empty;
	}

	public void TickOneSecond()
	{
		// Ensure daily reset at local midnight; this also handles the case when the PC was off
		// because we compare the stored date against today's date on every tick and on Load().
		EnsureDate();
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

	private static bool ShouldDecrement()
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

		return true;
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


