namespace DeviceTrafficMonitor.Core.Models;

public record DeviceConfig
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string McpServerCommand { get; init; }
    public string[] McpServerArgs { get; init; } = [];
    public int PollDurationSeconds { get; init; } = 2;
    public Dictionary<string, string> Tags { get; init; } = new();
    public bool AutoStart { get; init; } = true;
}
