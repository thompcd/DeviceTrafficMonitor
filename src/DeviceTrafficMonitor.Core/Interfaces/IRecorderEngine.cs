using DeviceTrafficMonitor.Core.Models;

namespace DeviceTrafficMonitor.Core.Interfaces;

public interface IRecorderEngine
{
    RecorderState State { get; }
    int DefaultPollDuration { get; }
    Task<StartResult> StartAsync(string[]? deviceIds, CancellationToken ct);
    Task<StopResult> StopAsync(string[]? deviceIds, bool flush, CancellationToken ct);
    RegisterResult Register(DeviceConfig config);
    Task<RemoveResult> RemoveAsync(string deviceId, bool force, CancellationToken ct);
    RecorderStatusSnapshot GetStatus();
    IReadOnlyList<DeviceRegistration> GetDevices();
}
