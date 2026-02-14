using PCTimeLimitServer.Domain.Entities;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimitServer.Api;

public static class MappingExtensions
{
    public static ComputerDto ToDto(this Computer computer)
    {
        return new ComputerDto
        {
            ComputerId = computer.ExternalId,
            ComputerName = computer.ComputerName,
            AdminUsername = computer.AdminUser.Username,
            DailyTimeLimit = TimeSpan.FromSeconds(computer.DailyTimeLimitSeconds),
            RegisteredAt = computer.RegisteredAtUtc,
            LastSeen = computer.LastSeenUtc,
            IsOnline = computer.IsOnline,
            PendingReset = computer.PendingReset,
            PendingForceLockout = computer.PendingForceLockout,
            AllowedUsageSchedule = computer.ToAllowedUsageScheduleDto()
        };
    }

    public static AllowedUsageScheduleDto ToAllowedUsageScheduleDto(this Computer computer)
    {
        return AllowedUsageScheduleService.GetScheduleForComputer(computer);
    }
}
