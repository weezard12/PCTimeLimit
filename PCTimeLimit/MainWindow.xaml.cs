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

namespace PCTimeLimit;

public partial class MainWindow : Window, INotifyPropertyChanged
{
	private readonly DispatcherTimer _uiTimer;
	private readonly TimeManager _timeManager;
	private readonly UsageTracker _usageTracker;

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainWindow()
	{
		InitializeComponent();
		_timeManager = new TimeManager();
		_timeManager.Load();

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

        //PreventClosing();
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
			}
		};
		timer.Start();
	}

	private void ShowLockout()
	{
		MessageBox.Show(this, "Time is up!", "PC Time Limit", MessageBoxButton.OK, MessageBoxImage.Stop);
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


