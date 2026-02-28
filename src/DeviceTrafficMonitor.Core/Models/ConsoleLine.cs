namespace DeviceTrafficMonitor.Core.Models;

public record ConsoleLine
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Device { get; init; }
    public string? Direction { get; init; }
    public string? Severity { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public int Sequence { get; init; }
}
