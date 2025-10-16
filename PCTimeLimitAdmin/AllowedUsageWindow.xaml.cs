using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PCTimeLimitAdmin
{
    public partial class AllowedUsageWindow : Window
    {
        private readonly Dictionary<string, List<(TimeSpan start, TimeSpan end)>> _model = new(StringComparer.OrdinalIgnoreCase)
        {
            { "monday", new() },
            { "tuesday", new() },
            { "wednesday", new() },
            { "thursday", new() },
            { "friday", new() },
            { "saturday", new() },
            { "sunday", new() },
        };

        public string? ResultJson { get; private set; }

        public AllowedUsageWindow(string? existingJson)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                TryLoad(existingJson);
            }
            BuildUi();
        }

        private void TryLoad(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var day in _model.Keys.ToList())
                {
                    if (!root.TryGetProperty(day, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    var list = new List<(TimeSpan, TimeSpan)>();
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;
                        if (!el.TryGetProperty("start", out var sEl) || !el.TryGetProperty("end", out var eEl)) continue;
                        var sStr = sEl.GetString();
                        var eStr = eEl.GetString();
                        if (TimeSpan.TryParse(sStr, out var sTs) && TimeSpan.TryParse(eStr, out var eTs))
                        {
                            list.Add((sTs, eTs));
                        }
                    }
                    _model[day] = list;
                }
            }
            catch { }
        }

        private void BuildUi()
        {
            DaysPanel.Children.Clear();
            foreach (var kv in _model)
            {
                var dayName = char.ToUpper(kv.Key[0]) + kv.Key.Substring(1);
                var dayBlock = new GroupBox { Header = dayName, Margin = new Thickness(0, 0, 0, 10) };
                var stack = new StackPanel { Margin = new Thickness(8) };

                var itemsPanel = new StackPanel { Orientation = Orientation.Vertical };
                foreach (var (start, end) in kv.Value)
                {
                    itemsPanel.Children.Add(CreateRangeRow(kv.Key, start, end));
                }

                var addBtn = new Button { Content = "Add Range", Width = 100, Height = 24, Margin = new Thickness(0, 5, 0, 0) };
                addBtn.Click += (s, e) =>
                {
                    itemsPanel.Children.Add(CreateRangeRow(kv.Key, TimeSpan.FromHours(8), TimeSpan.FromHours(15)));
                };

                stack.Children.Add(itemsPanel);
                stack.Children.Add(addBtn);
                dayBlock.Content = stack;
                DaysPanel.Children.Add(dayBlock);
            }
        }

        private UIElement CreateRangeRow(string dayKey, TimeSpan start, TimeSpan end)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var startBox = new TextBox { Width = 60, Text = start.ToString("hh\\:mm"), Margin = new Thickness(0, 0, 6, 0) };
            var sep = new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var endBox = new TextBox { Width = 60, Text = end.ToString("hh\\:mm"), Margin = new Thickness(0, 0, 6, 0) };
            var removeBtn = new Button { Content = "Remove", Width = 70, Height = 22 };

            removeBtn.Click += (s, e) =>
            {
                if (row.Parent is Panel p)
                {
                    p.Children.Remove(row);
                }
            };

            row.Children.Add(startBox);
            row.Children.Add(sep);
            row.Children.Add(endBox);
            row.Children.Add(removeBtn);
            return row;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in DaysPanel.Children.OfType<GroupBox>())
            {
                var key = group.Header!.ToString()!.ToLowerInvariant();
                var ranges = new List<Dictionary<string, string>>();
                if (group.Content is StackPanel sp)
                {
                    var itemsPanel = sp.Children.OfType<StackPanel>().FirstOrDefault();
                    if (itemsPanel != null)
                    {
                        foreach (var row in itemsPanel.Children.OfType<StackPanel>())
                        {
                            var boxes = row.Children.OfType<TextBox>().ToList();
                            if (boxes.Count >= 2)
                            {
                                var s = boxes[0].Text?.Trim();
                                var eText = boxes[1].Text?.Trim();
                                if (TimeSpan.TryParse(s, out var sTs) && TimeSpan.TryParse(eText, out var eTs))
                                {
                                    ranges.Add(new Dictionary<string, string>
                                    {
                                        { "start", sTs.ToString("hh\\:mm") },
                                        { "end", eTs.ToString("hh\\:mm") }
                                    });
                                }
                            }
                        }
                    }
                }
                result[key] = ranges;
            }

            ResultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            DialogResult = true;
            Close();
        }
    }
}


