using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCTimeLimitShared.Contracts;
using PCTimeLimitShared.Scheduling;

namespace PCTimeLimitAdmin
{
    public partial class AllowedUsageWindow : Window
    {
        private static readonly IReadOnlyList<Weekday> OrderedDays = new[]
        {
            Weekday.Monday,
            Weekday.Tuesday,
            Weekday.Wednesday,
            Weekday.Thursday,
            Weekday.Friday,
            Weekday.Saturday,
            Weekday.Sunday
        };

        private readonly List<TimeOption> _startOptions = CreateTimeOptions(0, AllowedUsageScheduleUtility.MinutesPerDay - AllowedUsageScheduleUtility.StepMinutes);
        private readonly List<TimeOption> _endOptions = CreateTimeOptions(AllowedUsageScheduleUtility.StepMinutes, AllowedUsageScheduleUtility.MinutesPerDay);
        private readonly Dictionary<Weekday, DayEditorState> _days = new();

        private List<AllowedUsageRangeDto> _currentCanonical = new();

        public IReadOnlyList<AllowedUsageRangeDto> ResultRanges { get; private set; } = Array.Empty<AllowedUsageRangeDto>();

        public AllowedUsageWindow(AllowedUsageScheduleDto? existingSchedule)
        {
            InitializeComponent();

            var initialRanges = AllowedUsageScheduleUtility.Canonicalize(existingSchedule?.Ranges)
                .OrderBy(r => r.Day)
                .ThenBy(r => r.StartMinute)
                .ToList();

            BuildUi(initialRanges);
            Revalidate();
        }

        private void BuildUi(IReadOnlyList<AllowedUsageRangeDto> initialRanges)
        {
            DaysPanel.Children.Clear();
            _days.Clear();

            foreach (var day in OrderedDays)
            {
                var rowsPanel = new StackPanel { Orientation = Orientation.Vertical };

                var dayState = new DayEditorState
                {
                    Day = day,
                    RowsPanel = rowsPanel,
                    PreviewText = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DarkSlateGray, TextWrapping = TextWrapping.Wrap }
                };

                var group = new GroupBox
                {
                    Header = day.ToString(),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var stack = new StackPanel { Margin = new Thickness(8) };

                var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

                var addButton = new Button { Content = "Add Range", Width = 90, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
                addButton.Click += (_, _) =>
                {
                    AddRangeRow(dayState, 8 * 60, 9 * 60);
                    Revalidate();
                };

                var clearButton = new Button { Content = "Clear Day", Width = 90, Height = 24 };
                clearButton.Click += (_, _) =>
                {
                    foreach (var row in dayState.Rows.ToList())
                    {
                        dayState.RowsPanel.Children.Remove(row.Container);
                    }

                    dayState.Rows.Clear();
                    Revalidate();
                };

                actionsPanel.Children.Add(addButton);
                actionsPanel.Children.Add(clearButton);

                stack.Children.Add(rowsPanel);
                stack.Children.Add(actionsPanel);
                stack.Children.Add(new TextBlock { Text = "Merged Preview:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
                stack.Children.Add(dayState.PreviewText);
                group.Content = stack;

                DaysPanel.Children.Add(group);
                _days[day] = dayState;

                foreach (var range in initialRanges.Where(r => r.Day == day))
                {
                    AddRangeRow(dayState, range.StartMinute, range.EndMinute);
                }
            }
        }

        private void AddRangeRow(DayEditorState dayState, int startMinute, int endMinute)
        {
            var rowContainer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 2) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var startCombo = new ComboBox
            {
                Width = 90,
                Margin = new Thickness(0, 0, 6, 0),
                ItemsSource = _startOptions,
                DisplayMemberPath = nameof(TimeOption.Label)
            };

            var separator = new TextBlock
            {
                Text = "-",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var endCombo = new ComboBox
            {
                Width = 90,
                Margin = new Thickness(0, 0, 6, 0),
                ItemsSource = _endOptions,
                DisplayMemberPath = nameof(TimeOption.Label)
            };

            var removeButton = new Button
            {
                Content = "Remove",
                Width = 70,
                Height = 22
            };

            var errorText = new TextBlock
            {
                Foreground = Brushes.DarkRed,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var rowState = new RangeRowState
            {
                Day = dayState.Day,
                Container = rowContainer,
                StartCombo = startCombo,
                EndCombo = endCombo,
                ErrorText = errorText
            };

            startCombo.SelectionChanged += (_, _) => Revalidate();
            endCombo.SelectionChanged += (_, _) => Revalidate();
            removeButton.Click += (_, _) =>
            {
                dayState.RowsPanel.Children.Remove(rowContainer);
                dayState.Rows.Remove(rowState);
                Revalidate();
            };

            row.Children.Add(startCombo);
            row.Children.Add(separator);
            row.Children.Add(endCombo);
            row.Children.Add(removeButton);

            rowContainer.Children.Add(row);
            rowContainer.Children.Add(errorText);

            dayState.RowsPanel.Children.Add(rowContainer);
            dayState.Rows.Add(rowState);

            startCombo.SelectedItem = _startOptions.FirstOrDefault(x => x.Minute == startMinute) ?? _startOptions[0];
            endCombo.SelectedItem = _endOptions.FirstOrDefault(x => x.Minute == endMinute) ?? _endOptions[0];
        }

        private void Revalidate()
        {
            var rowErrors = new List<string>();
            var rawRanges = new List<AllowedUsageRangeDto>();

            foreach (var dayState in _days.Values)
            {
                foreach (var row in dayState.Rows)
                {
                    row.ErrorText.Visibility = Visibility.Collapsed;
                    row.ErrorText.Text = string.Empty;

                    var start = (row.StartCombo.SelectedItem as TimeOption)?.Minute;
                    var end = (row.EndCombo.SelectedItem as TimeOption)?.Minute;

                    if (start is null || end is null)
                    {
                        ShowRowError(row, "Select both start and end times.");
                        rowErrors.Add($"{row.Day}: Select both start and end times.");
                        continue;
                    }

                    if (start.Value >= end.Value)
                    {
                        ShowRowError(row, "Start must be earlier than end.");
                        rowErrors.Add($"{row.Day}: Start must be earlier than end.");
                        continue;
                    }

                    rawRanges.Add(new AllowedUsageRangeDto
                    {
                        Day = row.Day,
                        StartMinute = start.Value,
                        EndMinute = end.Value
                    });
                }
            }

            var validationErrors = new List<string>(rowErrors);
            var ruleValidation = AllowedUsageScheduleUtility.ValidateRawRanges(rawRanges);
            validationErrors.AddRange(ruleValidation.Errors);

            var canonical = AllowedUsageScheduleUtility.Canonicalize(rawRanges);
            var dayCounts = canonical
                .GroupBy(x => x.Day)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var (day, count) in dayCounts)
            {
                if (count > AllowedUsageScheduleUtility.MaxMergedRangesPerDay)
                {
                    validationErrors.Add($"{day}: Maximum {AllowedUsageScheduleUtility.MaxMergedRangesPerDay} merged ranges per day exceeded.");
                }
            }

            _currentCanonical = canonical;
            UpdateMergedPreview(canonical);

            if (validationErrors.Count > 0)
            {
                ValidationSummaryText.Foreground = Brushes.DarkRed;
                ValidationSummaryText.Text = string.Join(Environment.NewLine, validationErrors.Distinct());
                SaveButton.IsEnabled = false;
                return;
            }

            ValidationSummaryText.Foreground = Brushes.DarkGreen;
            ValidationSummaryText.Text = canonical.Count == 0
                ? "No exclusions configured. Timer always counts."
                : "Schedule is valid.";
            SaveButton.IsEnabled = true;
        }

        private void UpdateMergedPreview(IReadOnlyList<AllowedUsageRangeDto> canonical)
        {
            foreach (var day in OrderedDays)
            {
                if (!_days.TryGetValue(day, out var dayState))
                {
                    continue;
                }

                var lines = canonical
                    .Where(x => x.Day == day)
                    .OrderBy(x => x.StartMinute)
                    .Select(x => $"{AllowedUsageScheduleUtility.FormatMinuteOfDay(x.StartMinute)} - {AllowedUsageScheduleUtility.FormatMinuteOfDay(x.EndMinute)}")
                    .ToList();

                dayState.PreviewText.Text = lines.Count == 0 ? "(none)" : string.Join(", ", lines);
            }
        }

        private static void ShowRowError(RangeRowState row, string message)
        {
            row.ErrorText.Text = message;
            row.ErrorText.Visibility = Visibility.Visible;
        }

        private static List<TimeOption> CreateTimeOptions(int startInclusive, int endInclusive)
        {
            var items = new List<TimeOption>();
            for (var minute = startInclusive; minute <= endInclusive; minute += AllowedUsageScheduleUtility.StepMinutes)
            {
                items.Add(new TimeOption(minute));
            }

            return items;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveButton.IsEnabled)
            {
                return;
            }

            ResultRanges = _currentCanonical
                .OrderBy(x => x.Day)
                .ThenBy(x => x.StartMinute)
                .Select(x => new AllowedUsageRangeDto
                {
                    Day = x.Day,
                    StartMinute = x.StartMinute,
                    EndMinute = x.EndMinute
                })
                .ToList();

            DialogResult = true;
            Close();
        }

        private sealed class DayEditorState
        {
            public Weekday Day { get; init; }
            public StackPanel RowsPanel { get; init; } = null!;
            public TextBlock PreviewText { get; init; } = null!;
            public List<RangeRowState> Rows { get; } = new();
        }

        private sealed class RangeRowState
        {
            public Weekday Day { get; init; }
            public StackPanel Container { get; init; } = null!;
            public ComboBox StartCombo { get; init; } = null!;
            public ComboBox EndCombo { get; init; } = null!;
            public TextBlock ErrorText { get; init; } = null!;
        }

        private sealed class TimeOption
        {
            public TimeOption(int minute)
            {
                Minute = minute;
                Label = AllowedUsageScheduleUtility.FormatMinuteOfDay(minute);
            }

            public int Minute { get; }
            public string Label { get; }
        }
    }
}
