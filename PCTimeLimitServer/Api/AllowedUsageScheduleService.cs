using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PCTimeLimitServer.Domain.Entities;
using PCTimeLimitServer.Infrastructure;
using PCTimeLimitShared.Contracts;
using PCTimeLimitShared.Scheduling;

namespace PCTimeLimitServer.Api;

public static class AllowedUsageScheduleService
{
    private static readonly Dictionary<string, Weekday> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sunday"] = Weekday.Sunday,
        ["monday"] = Weekday.Monday,
        ["tuesday"] = Weekday.Tuesday,
        ["wednesday"] = Weekday.Wednesday,
        ["thursday"] = Weekday.Thursday,
        ["friday"] = Weekday.Friday,
        ["saturday"] = Weekday.Saturday
    };

    public static AllowedUsageScheduleDto GetScheduleForComputer(Computer computer)
    {
        if (computer.AllowedUsageRanges.Count > 0)
        {
            return AllowedUsageScheduleUtility.CreateCanonicalSchedule(
                computer.AllowedUsageRanges
                    .OrderBy(x => x.DayOfWeek)
                    .ThenBy(x => x.Order)
                    .Select(x => new AllowedUsageRangeDto
                    {
                        Day = (Weekday)x.DayOfWeek,
                        StartMinute = x.StartMinute,
                        EndMinute = x.EndMinute
                    }),
                GetUpdatedAtUtc(computer));
        }

        // Transitional fallback for legacy JSON-only rows.
        var legacyRanges = ParseLegacyJson(computer.AllowedUsageJson);
        return AllowedUsageScheduleUtility.CreateCanonicalSchedule(legacyRanges, GetUpdatedAtUtc(computer));
    }

    public static (AllowedUsageScheduleDto? Schedule, List<string> Errors) ValidateAndCanonicalize(IEnumerable<AllowedUsageRangeDto>? ranges)
    {
        var validation = AllowedUsageScheduleUtility.ValidateRawRanges(ranges);
        if (!validation.IsValid)
        {
            return (null, validation.Errors);
        }

        var canonical = AllowedUsageScheduleUtility.CreateCanonicalSchedule(ranges);
        var byDayCounts = canonical.Ranges.GroupBy(x => x.Day).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (day, count) in byDayCounts)
        {
            if (count > AllowedUsageScheduleUtility.MaxMergedRangesPerDay)
            {
                validation.Errors.Add($"Day {day} exceeds max merged range count of {AllowedUsageScheduleUtility.MaxMergedRangesPerDay}.");
            }
        }

        if (!validation.IsValid)
        {
            return (null, validation.Errors);
        }

        return (canonical, validation.Errors);
    }

    public static async Task ApplyScheduleAsync(PCTimeLimitDbContext dbContext, Computer computer, AllowedUsageScheduleDto schedule, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ComputerAllowedUsageRanges
            .Where(x => x.ComputerId == computer.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            dbContext.ComputerAllowedUsageRanges.RemoveRange(existing);
        }

        var rows = schedule.Ranges
            .OrderBy(x => x.Day)
            .ThenBy(x => x.StartMinute)
            .Select((range, index) => new ComputerAllowedUsageRange
            {
                Id = Guid.NewGuid(),
                ComputerId = computer.Id,
                DayOfWeek = (int)range.Day,
                StartMinute = range.StartMinute,
                EndMinute = range.EndMinute,
                Order = index
            });

        dbContext.ComputerAllowedUsageRanges.AddRange(rows);
        computer.AllowedUsageUpdatedAtUtc = schedule.UpdatedAtUtc == default ? DateTime.UtcNow : schedule.UpdatedAtUtc;
    }

    public static async Task<int> BackfillLegacySchedulesAsync(PCTimeLimitDbContext dbContext, ILogger logger, CancellationToken cancellationToken)
    {
        var computers = await dbContext.Computers
            .Include(x => x.AllowedUsageRanges)
            .Where(x => x.AllowedUsageRanges.Count == 0 && !string.IsNullOrWhiteSpace(x.AllowedUsageJson))
            .ToListAsync(cancellationToken);

        var migrated = 0;
        foreach (var computer in computers)
        {
            var parsed = ParseLegacyJson(computer.AllowedUsageJson);
            if (parsed.Count == 0)
            {
                continue;
            }

            var (schedule, errors) = ValidateAndCanonicalize(parsed);
            if (schedule is null)
            {
                logger.LogWarning("Legacy schedule backfill failed for computer {ComputerId}: {Errors}", computer.ExternalId, string.Join("; ", errors));
                continue;
            }

            schedule.UpdatedAtUtc = DateTime.UtcNow;
            await ApplyScheduleAsync(dbContext, computer, schedule, cancellationToken);
            migrated++;
        }

        if (migrated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return migrated;
    }

    public static bool AreEquivalent(AllowedUsageScheduleDto left, AllowedUsageScheduleDto right)
    {
        var leftCanonical = AllowedUsageScheduleUtility.Canonicalize(left.Ranges)
            .OrderBy(x => x.Day)
            .ThenBy(x => x.StartMinute)
            .ThenBy(x => x.EndMinute)
            .ToList();

        var rightCanonical = AllowedUsageScheduleUtility.Canonicalize(right.Ranges)
            .OrderBy(x => x.Day)
            .ThenBy(x => x.StartMinute)
            .ThenBy(x => x.EndMinute)
            .ToList();

        if (leftCanonical.Count != rightCanonical.Count)
        {
            return false;
        }

        for (var i = 0; i < leftCanonical.Count; i++)
        {
            var a = leftCanonical[i];
            var b = rightCanonical[i];
            if (a.Day != b.Day || a.StartMinute != b.StartMinute || a.EndMinute != b.EndMinute)
            {
                return false;
            }
        }

        return true;
    }

    private static DateTime GetUpdatedAtUtc(Computer computer)
    {
        return computer.AllowedUsageUpdatedAtUtc == default
            ? DateTime.UtcNow
            : DateTime.SpecifyKind(computer.AllowedUsageUpdatedAtUtc, DateTimeKind.Utc);
    }

    private static List<AllowedUsageRangeDto> ParseLegacyJson(string? legacyJson)
    {
        var ranges = new List<AllowedUsageRangeDto>();
        if (string.IsNullOrWhiteSpace(legacyJson))
        {
            return ranges;
        }

        try
        {
            using var document = JsonDocument.Parse(legacyJson);
            var root = document.RootElement;

            foreach (var property in root.EnumerateObject())
            {
                if (!DayMap.TryGetValue(property.Name, out var day))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("start", out var startProp) || !item.TryGetProperty("end", out var endProp))
                    {
                        continue;
                    }

                    if (!TimeSpan.TryParse(startProp.GetString(), out var start) || !TimeSpan.TryParse(endProp.GetString(), out var end))
                    {
                        continue;
                    }

                    ranges.Add(new AllowedUsageRangeDto
                    {
                        Day = day,
                        StartMinute = (int)start.TotalMinutes,
                        EndMinute = (int)end.TotalMinutes
                    });
                }
            }
        }
        catch
        {
            // Ignore malformed legacy JSON.
        }

        return ranges;
    }
}
