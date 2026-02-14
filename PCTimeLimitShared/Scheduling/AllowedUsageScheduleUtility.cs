using PCTimeLimitShared.Contracts;

namespace PCTimeLimitShared.Scheduling;

public sealed class ScheduleValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
}

public static class AllowedUsageScheduleUtility
{
    public const int StepMinutes = 5;
    public const int MinutesPerDay = 24 * 60;
    public const int MaxMergedRangesPerDay = 24;

    public static ScheduleValidationResult ValidateRawRanges(IEnumerable<AllowedUsageRangeDto>? ranges)
    {
        var result = new ScheduleValidationResult();
        if (ranges is null)
        {
            return result;
        }

        var i = 0;
        foreach (var range in ranges)
        {
            i++;
            if (range is null)
            {
                result.Errors.Add($"Range {i}: Range value is required.");
                continue;
            }

            if (range.StartMinute < 0 || range.StartMinute >= MinutesPerDay)
            {
                result.Errors.Add($"Range {i}: StartMinute must be between 0 and {MinutesPerDay - StepMinutes}.");
            }
            if (range.EndMinute <= 0 || range.EndMinute > MinutesPerDay)
            {
                result.Errors.Add($"Range {i}: EndMinute must be between {StepMinutes} and {MinutesPerDay}.");
            }
            if (range.StartMinute >= range.EndMinute)
            {
                result.Errors.Add($"Range {i}: StartMinute must be lower than EndMinute.");
            }
            if (range.StartMinute % StepMinutes != 0 || range.EndMinute % StepMinutes != 0)
            {
                result.Errors.Add($"Range {i}: StartMinute and EndMinute must be in {StepMinutes}-minute increments.");
            }
        }

        return result;
    }

    public static List<AllowedUsageRangeDto> Canonicalize(IEnumerable<AllowedUsageRangeDto>? ranges)
    {
        if (ranges is null)
        {
            return new List<AllowedUsageRangeDto>();
        }

        var byDay = ranges
            .Where(r => r is not null)
            .GroupBy(r => r.Day)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.StartMinute).ThenBy(x => x.EndMinute).ToList());

        var result = new List<AllowedUsageRangeDto>();

        foreach (Weekday day in Enum.GetValues(typeof(Weekday)))
        {
            if (!byDay.TryGetValue(day, out var dayRanges) || dayRanges.Count == 0)
            {
                continue;
            }

            var merged = new List<AllowedUsageRangeDto>
            {
                new()
                {
                    Day = day,
                    StartMinute = dayRanges[0].StartMinute,
                    EndMinute = dayRanges[0].EndMinute
                }
            };

            for (var i = 1; i < dayRanges.Count; i++)
            {
                var current = dayRanges[i];
                var last = merged[^1];

                if (current.StartMinute <= last.EndMinute)
                {
                    if (current.EndMinute > last.EndMinute)
                    {
                        last.EndMinute = current.EndMinute;
                    }
                    continue;
                }

                merged.Add(new AllowedUsageRangeDto
                {
                    Day = day,
                    StartMinute = current.StartMinute,
                    EndMinute = current.EndMinute
                });
            }

            result.AddRange(merged);
        }

        return result;
    }

    public static AllowedUsageScheduleDto CreateCanonicalSchedule(IEnumerable<AllowedUsageRangeDto>? ranges, DateTime? updatedAtUtc = null)
    {
        return new AllowedUsageScheduleDto
        {
            Ranges = Canonicalize(ranges),
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow
        };
    }

    public static bool IsInAllowedWindow(AllowedUsageScheduleDto? schedule, DateTime localNow)
    {
        if (schedule is null || schedule.Ranges.Count == 0)
        {
            return false;
        }

        var day = (Weekday)localNow.DayOfWeek;
        var minute = (int)localNow.TimeOfDay.TotalMinutes;

        foreach (var range in schedule.Ranges)
        {
            if (range.Day != day)
            {
                continue;
            }

            // Half-open interval [start, end)
            if (minute >= range.StartMinute && minute < range.EndMinute)
            {
                return true;
            }
        }

        return false;
    }

    public static string FormatMinuteOfDay(int minute)
    {
        minute = Math.Clamp(minute, 0, MinutesPerDay);
        var ts = TimeSpan.FromMinutes(minute);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
    }
}
