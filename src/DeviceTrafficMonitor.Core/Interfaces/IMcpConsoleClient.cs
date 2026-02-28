namespace DeviceTrafficMonitor.Core.Interfaces;

public interface IMcpConsoleClient : IAsyncDisposable
{
    Task<string[]> MonitorAsync(TimeSpan duration, CancellationToken ct);
}
