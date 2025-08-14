using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PCTimeLimit;

public partial class StatsWindow : Window
{
	private readonly UsageTracker _tracker;
    private string _currentKey = string.Empty;

	public StatsWindow(UsageTracker tracker)
	{
		InitializeComponent();
		_tracker = tracker;
		DatePicker.SelectedDate = DateTime.Today;
        _currentKey = DateTime.Today.ToString("yyyy-MM-dd");
        _tracker.UsageUpdated += OnUsageUpdated;
		RefreshList(DateTime.Today);
	}

	private void RefreshList(DateTime date)
	{
		var data = _tracker.GetUsageForDate(date)
			.OrderByDescending(kv => kv.Value)
			.Select(kv => new Row { App = kv.Key, Time = Format(kv.Value) })
			.ToList();
		UsageList.ItemsSource = data;
	}

	private static string Format(TimeSpan ts)
	{
		return ts.ToString();
	}

	private void OnDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		if (DatePicker.SelectedDate is DateTime date)
		{
            _currentKey = date.ToString("yyyy-MM-dd");
			RefreshList(date);
		}
	}

	private void OnTodayClick(object sender, RoutedEventArgs e)
	{
		DatePicker.SelectedDate = DateTime.Today;
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
        _tracker.UsageUpdated -= OnUsageUpdated;
		Close();
	}

    private void OnUsageUpdated(string dateKey)
    {
        if (dateKey == _currentKey)
        {
            Dispatcher.Invoke(() => RefreshList(DateTime.Parse(dateKey)));
        }
    }

	private sealed class Row
	{
		public string App { get; init; } = string.Empty;
		public string Time { get; init; } = string.Empty;
	}
}


