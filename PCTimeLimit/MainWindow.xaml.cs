using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCTimeLimit;

public partial class MainWindow : Window, INotifyPropertyChanged
{
	private readonly DispatcherTimer _uiTimer;
	private readonly TimeManager _timeManager;

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainWindow()
	{
		InitializeComponent();
		_timeManager = new TimeManager();
		_timeManager.Load();

		_uiTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1)
		};
		_uiTimer.Tick += (_, _) => UpdateUi();
		_uiTimer.Start();

		CompositionTarget_Rendering();
		UpdateUi();

        PreventClosing();
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
			var cp = new SettingsWindow(_timeManager);
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
	public DateTime DateUtc { get; set; } = DateTime.UtcNow.Date;
	public TimeSpan RemainingForDate { get; set; } = TimeSpan.FromHours(1);
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

	public void TickOneSecond()
	{
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
		var today = DateTime.UtcNow.Date;
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

	[DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetWindowTextLength(IntPtr hWnd);
	[DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);
	[DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr pMemory);

	private enum WTS_INFO_CLASS
	{
		WTSSessionId = 4,
		WTSConnectState = 8
	}

	private static string GetForegroundWindowTitle()
	{
		var handle = GetForegroundWindow();
		if (handle == IntPtr.Zero) return string.Empty;
		int length = GetWindowTextLength(handle);
		var sb = new System.Text.StringBuilder(length + 1);
		_ = GetWindowText(handle, sb, sb.Capacity);
		return sb.ToString();
	}

	private static bool IsWorkstationLocked()
	{
		// Simpler heuristic: when there's no foreground window title, consider locked or secure desktop
		var title = GetForegroundWindowTitle();
		return string.IsNullOrWhiteSpace(title);
	}
}


