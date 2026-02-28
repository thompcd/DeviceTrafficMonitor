namespace DeviceTrafficMonitor.Core.Models;

public record StartResult(
    bool Started,
    string[] DevicesStarted,
    string[] AlreadyRunning,
    Dictionary<string, string> Errors);

public record StopResult(
    bool Stopped,
    string[] DevicesStopped,
    bool DrainCompleted,
    long LinesFlushed);

public record RegisterResult(
    bool Registered,
    string DeviceId,
    bool Recording,
    string Message);

public record RemoveResult(
    bool Removed,
    bool WasRecording,
    string Message);

public record SearchMatch
{
    public required ConsoleLine MatchLine { get; init; }
    public IReadOnlyList<ConsoleLine> BeforeLines { get; init; } = [];
    public IReadOnlyList<ConsoleLine> AfterLines { get; init; } = [];
}

public record TrafficSummary
{
    public required string Device { get; init; }
    public long LineCount { get; init; }
    public long ErrorCount { get; init; }
    public long WarnCount { get; init; }
    public DateTimeOffset? FirstLineAt { get; init; }
    public DateTimeOffset? LastLineAt { get; init; }
    public IReadOnlyList<string> SampleErrors { get; init; } = [];
}

public record RecorderStatusSnapshot
{
    public RecorderState State { get; init; }
    public TimeSpan? Uptime { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public long TotalLines { get; init; }
    public int ActivePollers { get; init; }
    public IReadOnlyList<DevicePollerStatus> Devices { get; init; } = [];
}

public record DevicePollerStatus
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public bool Polling { get; init; }
    public int PollDuration { get; init; }
    public long LinesCount { get; init; }
    public DateTimeOffset? LastPollAt { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveErrors { get; init; }
}
