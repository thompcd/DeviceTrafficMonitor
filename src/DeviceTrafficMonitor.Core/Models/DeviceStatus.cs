namespace DeviceTrafficMonitor.Core.Models;

public record DeviceStatus
{
    public required string Device { get; init; }
    public required string DisplayName { get; init; }
    public required string State { get; init; }
    public bool Recording { get; init; }
    public DateTimeOffset? LastLineAt { get; init; }
    public long LinesRecorded { get; init; }
    public TimeSpan? Uptime { get; init; }
    public string? ErrorMessage { get; init; }
}
