namespace DeviceTrafficMonitor.Server;

public record RecorderConfig
{
    public bool AutoStart { get; init; }
    public int DrainTimeoutSeconds { get; init; } = 10;
    public int DefaultPollDurationSeconds { get; init; } = 2;
}
