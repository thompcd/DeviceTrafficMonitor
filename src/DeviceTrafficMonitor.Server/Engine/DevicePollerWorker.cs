using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace DeviceTrafficMonitor.Server.Engine;

public class DevicePollerWorker : IDevicePoller, IAsyncDisposable
{
    private readonly IMcpConsoleClient _mcpClient;
    private readonly LineParser _lineParser;
    private readonly ITrafficStore _store;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollDuration;
    private readonly string _displayName;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public string DeviceId { get; }
    public bool IsPolling { get; private set; }
    public long LinesCount { get; private set; }
    public DateTimeOffset? LastPollAt { get; private set; }
    public string? LastError { get; private set; }
    public int ConsecutiveErrors { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }

    public DevicePollerWorker(
        string deviceId,
        string displayName,
        IMcpConsoleClient mcpClient,
        LineParser lineParser,
        ITrafficStore store,
        ILogger logger,
        TimeSpan pollDuration)
    {
        DeviceId = deviceId;
        _displayName = displayName;
        _mcpClient = mcpClient;
        _lineParser = lineParser;
        _store = store;
        _logger = logger;
        _pollDuration = pollDuration;
    }

    public Task RunAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StartedAt = DateTimeOffset.UtcNow;
        IsPolling = true;
        _runTask = PollLoopAsync(_cts.Token);
        return _runTask;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Polling started for device {DeviceId}", DeviceId);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var lines = await _mcpClient.MonitorAsync(_pollDuration, ct);
                var records = _lineParser.Parse(DeviceId, lines);

                if (records.Count > 0)
                {
                    await _store.WriteAsync(records, ct);
                    LinesCount += records.Count;
                }

                LastPollAt = DateTimeOffset.UtcNow;
                ConsecutiveErrors = 0;
                LastError = null;

                await _store.WriteStatusAsync(new DeviceStatus
                {
                    Device = DeviceId,
                    DisplayName = _displayName,
                    State = "connected",
                    Recording = true,
                    LastLineAt = records.Count > 0 ? records[^1].Timestamp : LastPollAt,
                    LinesRecorded = LinesCount,
                    Uptime = DateTimeOffset.UtcNow - StartedAt
                }, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ConsecutiveErrors++;
                LastError = ex.Message;
                _logger.LogWarning(ex, "Poll error for device {DeviceId}, consecutive errors: {Count}", DeviceId, ConsecutiveErrors);

                await _store.WriteStatusAsync(new DeviceStatus
                {
                    Device = DeviceId,
                    DisplayName = _displayName,
                    State = "error",
                    Recording = true,
                    LinesRecorded = LinesCount,
                    ErrorMessage = ex.Message,
                    Uptime = DateTimeOffset.UtcNow - StartedAt
                }, CancellationToken.None);

                var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, ConsecutiveErrors)));
                try
                {
                    await Task.Delay(backoff, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        IsPolling = false;
        _logger.LogInformation("Polling stopped for device {DeviceId}", DeviceId);
    }

    public async Task StopAsync(bool flush)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();

            if (_runTask is not null)
            {
                var drainTimeout = Task.Delay(TimeSpan.FromSeconds(10));
                await Task.WhenAny(_runTask, drainTimeout);
            }
        }

        if (flush)
        {
            await _store.FlushAsync(CancellationToken.None);
        }

        IsPolling = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(flush: true);
        await _mcpClient.DisposeAsync();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
