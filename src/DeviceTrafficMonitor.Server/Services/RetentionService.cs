using DeviceTrafficMonitor.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeviceTrafficMonitor.Server.Services;

public class RetentionService : BackgroundService
{
    private readonly ITrafficStore _store;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(ITrafficStore store, ILogger<RetentionService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run retention on startup
        await RunRetention(stoppingToken);

        // Then every hour
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunRetention(stoppingToken);
        }
    }

    private async Task RunRetention(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Running retention cleanup");
            await _store.RunRetentionAsync(ct);
            _logger.LogInformation("Retention cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention cleanup failed");
        }
    }
}
