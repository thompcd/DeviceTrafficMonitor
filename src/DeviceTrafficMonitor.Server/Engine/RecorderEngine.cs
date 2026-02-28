using System.Collections.Concurrent;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace DeviceTrafficMonitor.Server.Engine;

public class RecorderEngine : IRecorderEngine, IAsyncDisposable
{
    private readonly DeviceRegistry _registry;
    private readonly IMcpClientFactory _clientFactory;
    private readonly ITrafficStore _store;
    private readonly ILogger<RecorderEngine> _logger;
    private readonly ConcurrentDictionary<string, DevicePollerWorker> _pollers = new();
    private readonly int _defaultPollDuration;
    private readonly int _drainTimeoutSeconds;
    private CancellationTokenSource? _cts;
    private DateTimeOffset? _startedAt;

    public RecorderState State { get; private set; } = RecorderState.Stopped;
    public int DefaultPollDuration => _defaultPollDuration;

    public RecorderEngine(
        DeviceRegistry registry,
        IMcpClientFactory clientFactory,
        ITrafficStore store,
        ILogger<RecorderEngine> logger,
        int defaultPollDurationSeconds = 2,
        int drainTimeoutSeconds = 10)
    {
        _registry = registry;
        _clientFactory = clientFactory;
        _store = store;
        _logger = logger;
        _defaultPollDuration = defaultPollDurationSeconds;
        _drainTimeoutSeconds = drainTimeoutSeconds;
    }

    public async Task<StartResult> StartAsync(string[]? deviceIds, CancellationToken ct)
    {
        var started = new List<string>();
        var alreadyRunning = new List<string>();
        var errors = new Dictionary<string, string>();

        if (State == RecorderState.Running && deviceIds is null)
        {
            return new StartResult(true, [], _pollers.Keys.ToArray(), errors);
        }

        _cts ??= new CancellationTokenSource();
        State = RecorderState.Running;
        _startedAt ??= DateTimeOffset.UtcNow;

        var devices = deviceIds is not null
            ? _registry.GetByIds(deviceIds)
            : _registry.GetAll();

        foreach (var device in devices)
        {
            if (_pollers.ContainsKey(device.Id))
            {
                alreadyRunning.Add(device.Id);
                continue;
            }

            try
            {
                var mcpClient = await _clientFactory.CreateAsync(device, ct);
                var worker = new DevicePollerWorker(
                    device.Id,
                    device.DisplayName,
                    mcpClient,
                    new LineParser(),
                    _store,
                    _logger,
                    TimeSpan.FromSeconds(device.PollDurationSeconds > 0 ? device.PollDurationSeconds : _defaultPollDuration));

                if (_pollers.TryAdd(device.Id, worker))
                {
                    _ = worker.RunAsync(_cts.Token);
                    started.Add(device.Id);
                }
            }
            catch (Exception ex)
            {
                errors[device.Id] = ex.Message;
                _logger.LogError(ex, "Failed to start poller for device {DeviceId}", device.Id);
            }
        }

        await _store.WriteEventAsync("recorder_started", null,
            $"Started: [{string.Join(", ", started)}], AlreadyRunning: [{string.Join(", ", alreadyRunning)}]", ct);

        return new StartResult(true, started.ToArray(), alreadyRunning.ToArray(), errors);
    }

    public async Task<StopResult> StopAsync(string[]? deviceIds, bool flush, CancellationToken ct)
    {
        State = RecorderState.Stopping;
        long linesFlushed = 0;

        var targetIds = deviceIds ?? _pollers.Keys.ToArray();
        var stopped = new List<string>();

        foreach (var id in targetIds)
        {
            if (_pollers.TryRemove(id, out var worker))
            {
                linesFlushed += worker.LinesCount;
                await worker.StopAsync(flush);
                await worker.DisposeAsync();
                stopped.Add(id);
            }
        }

        if (_pollers.IsEmpty)
        {
            if (_cts is not null)
            {
                await _cts.CancelAsync();
                _cts.Dispose();
                _cts = null;
            }
            State = RecorderState.Stopped;
            _startedAt = null;
        }
        else
        {
            State = RecorderState.Running;
        }

        await _store.WriteEventAsync("recorder_stopped", null,
            $"Stopped: [{string.Join(", ", stopped)}]", ct);

        return new StopResult(true, stopped.ToArray(), true, linesFlushed);
    }

    public RegisterResult Register(DeviceConfig config)
    {
        _registry.Add(config, "runtime");

        var recording = false;
        if (State == RecorderState.Running && config.AutoStart)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartAsync([config.Id], CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-start device {DeviceId}", config.Id);
                }
            });
            recording = true;
        }

        return new RegisterResult(true, config.Id, recording,
            recording ? "Registered and recording started" : "Registered");
    }

    public async Task<RemoveResult> RemoveAsync(string deviceId, bool force, CancellationToken ct)
    {
        var wasRecording = _pollers.ContainsKey(deviceId);

        if (wasRecording && !force)
        {
            return new RemoveResult(false, true,
                $"Device '{deviceId}' is currently recording. Use force=true to remove.");
        }

        if (wasRecording && _pollers.TryRemove(deviceId, out var worker))
        {
            await worker.StopAsync(flush: true);
            await worker.DisposeAsync();
        }

        _registry.Remove(deviceId);

        await _store.WriteEventAsync("device_removed", deviceId,
            $"force={force}, wasRecording={wasRecording}", ct);

        return new RemoveResult(true, wasRecording, "Device removed");
    }

    public RecorderStatusSnapshot GetStatus()
    {
        var deviceStatuses = new List<DevicePollerStatus>();
        foreach (var (id, worker) in _pollers)
        {
            DeviceConfig? config = null;
            try { config = _registry.GetRegistration(id).Config; } catch { }

            deviceStatuses.Add(new DevicePollerStatus
            {
                Id = id,
                DisplayName = config?.DisplayName ?? id,
                Polling = worker.IsPolling,
                PollDuration = config?.PollDurationSeconds ?? _defaultPollDuration,
                LinesCount = worker.LinesCount,
                LastPollAt = worker.LastPollAt,
                LastError = worker.LastError,
                ConsecutiveErrors = worker.ConsecutiveErrors
            });
        }

        return new RecorderStatusSnapshot
        {
            State = State,
            Uptime = _startedAt.HasValue ? DateTimeOffset.UtcNow - _startedAt.Value : null,
            StartedAt = _startedAt,
            TotalLines = _pollers.Values.Sum(p => p.LinesCount),
            ActivePollers = _pollers.Count(p => p.Value.IsPolling),
            Devices = deviceStatuses
        };
    }

    public IReadOnlyList<DeviceRegistration> GetDevices() => _registry.GetAllRegistrations();

    public async ValueTask DisposeAsync()
    {
        if (State != RecorderState.Stopped)
        {
            await StopAsync(null, flush: true, CancellationToken.None);
        }
        GC.SuppressFinalize(this);
    }
}
