namespace DeviceTrafficMonitor.Core.Interfaces;

public interface IDevicePoller
{
    string DeviceId { get; }
    bool IsPolling { get; }
    Task RunAsync(CancellationToken ct);
    Task StopAsync(bool flush);
}
