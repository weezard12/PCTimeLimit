using System;
using System.Windows;

namespace PCTimeLimit;

public partial class SettingsWindow : Window
{
	private readonly TimeManager _timeManager;

	public SettingsWindow(TimeManager timeManager)
	{
		InitializeComponent();
		_timeManager = timeManager;
		LoadCurrentValues();
	}

	private void LoadCurrentValues()
	{
		var limit = _timeManager is null ? TimeSpan.FromHours(1) : _timeManager.DailyLimit;
		HoursBox.Text = Math.Max(0, (int)limit.TotalHours).ToString();
		MinutesBox.Text = limit.Minutes.ToString();
	}

	private void OnSaveClick(object sender, RoutedEventArgs e)
	{
		if (!int.TryParse(HoursBox.Text, out var hours)) hours = 0;
		if (!int.TryParse(MinutesBox.Text, out var minutes)) minutes = 0;
		hours = Math.Max(0, Math.Min(23, hours));
		minutes = Math.Max(0, Math.Min(59, minutes));
		var newLimit = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
		_timeManager.UpdateDailyLimit(newLimit);

		_timeManager.UpdatePassword(PasswordBox.Password ?? string.Empty);
		MessageBox.Show(this, "Saved.", "PC Time Limit", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}
}


