namespace DeviceTrafficMonitor.Core.Models;

public record StorageConfig
{
    public string? DataDirectory { get; init; }
    public int RetentionDays { get; init; } = 7;
    public int MaxDatabaseSizeMb { get; init; } = 500;
    public int WriteBufferSize { get; init; } = 50;
}
