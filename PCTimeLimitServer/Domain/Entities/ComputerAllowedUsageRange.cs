namespace PCTimeLimitServer.Domain.Entities;

public sealed class ComputerAllowedUsageRange
{
    public Guid Id { get; set; }
    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;
    public int DayOfWeek { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public int Order { get; set; }
}
