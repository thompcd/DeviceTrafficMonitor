namespace DeviceTrafficMonitor.Core.Models;

public record RecorderEvent
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Event { get; init; }
    public string? Device { get; init; }
    public string? Detail { get; init; }
    public string? ConfigSnapshot { get; init; }
}
