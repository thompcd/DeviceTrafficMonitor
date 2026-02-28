using DeviceTrafficMonitor.Core.Models;

namespace DeviceTrafficMonitor.Core.Interfaces;

public interface ITrafficStore
{
    Task InitializeAsync(CancellationToken ct);
    Task WriteAsync(IReadOnlyList<ConsoleLine> lines, CancellationToken ct);

    Task<(IReadOnlyList<ConsoleLine> Lines, long TotalCount)> QueryAsync(
        string? device = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        string? contains = null,
        string? regex = null,
        string? severity = null,
        string? direction = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);

    Task<(IReadOnlyList<SearchMatch> Matches, long TotalMatches)> SearchAsync(
        string pattern,
        string? device = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int contextLines = 3,
        int limit = 20,
        CancellationToken ct = default);

    Task<IReadOnlyList<TrafficSummary>> SummarizeAsync(
        string? device = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken ct = default);

    Task WriteEventAsync(string eventType, string? device, string? detail, CancellationToken ct);
    Task WriteStatusAsync(DeviceStatus status, CancellationToken ct);
    Task RunRetentionAsync(CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
}
