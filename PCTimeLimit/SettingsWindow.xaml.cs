using System;
using System.Windows;

namespace PCTimeLimit;

public partial class SettingsWindow : Window
{
	private readonly TimeManager _timeManager;
	private readonly UsageTracker _usageTracker;

	public SettingsWindow(TimeManager timeManager, UsageTracker usageTracker)
	{
		InitializeComponent();
		_timeManager = timeManager;
		_usageTracker = usageTracker;
		LoadCurrentValues();
	}

	private void LoadCurrentValues()
	{
		var limit = _timeManager is null ? TimeSpan.FromHours(1) : _timeManager.DailyLimit;
		HoursBox.Text = Math.Max(0, (int)limit.TotalHours).ToString();
		MinutesBox.Text = limit.Minutes.ToString();
		PasswordBox.Password = _timeManager.GetPassword();
	}

	private void OnSaveClick(object sender, RoutedEventArgs e)
	{
		if (!int.TryParse(HoursBox.Text, out var hours)) hours = 0;
		if (!int.TryParse(MinutesBox.Text, out var minutes)) minutes = 0;
		hours = Math.Max(0, Math.Min(23, hours));
		minutes = Math.Max(0, Math.Min(59, minutes));
		var newLimit = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
		_timeManager.UpdateDailyLimit(newLimit);

		var newPassword = (PasswordBox.Password ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(newPassword))
		{
			_timeManager.UpdatePassword(newPassword);
		}
		MessageBox.Show(this, "Saved.", "PC Time Limit", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void OnOpenStatsClick(object sender, RoutedEventArgs e)
	{
		var statsWindow = new StatsWindow(_usageTracker);
		statsWindow.Owner = this;
		statsWindow.ShowDialog();
	}
}


