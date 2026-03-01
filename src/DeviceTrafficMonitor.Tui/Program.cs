using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Storage;
using DeviceTrafficMonitor.Tui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Terminal.Gui;

// Build configuration from appsettings.json
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

// Bind config sections (same pattern as Server/Program.cs)
var storageConfig = config.GetSection("Storage").Get<StorageConfig>() ?? new StorageConfig();
var recorderConfig = config.GetSection("Recorder").Get<RecorderConfig>() ?? new RecorderConfig();

// Bootstrap storage
var bootstrapper = new StorageBootstrapper(storageConfig);
await bootstrapper.EnsureCreatedAsync();

// Create store
var store = new SqliteTrafficStore(storageConfig, bootstrapper.ConnectionString);
await store.InitializeAsync(CancellationToken.None);

// Create registry and load config
var registry = new DeviceRegistry();
registry.LoadFromConfig(config);

// Create engine
var loggerFactory = LoggerFactory.Create(b => { });
var factory = new StdioMcpClientFactory();
var engine = new RecorderEngine(
    registry, factory, store,
    loggerFactory.CreateLogger<RecorderEngine>(),
    recorderConfig.DefaultPollDurationSeconds,
    recorderConfig.DrainTimeoutSeconds);

// Auto-start if configured
if (recorderConfig.AutoStart)
{
    await engine.StartAsync(null, CancellationToken.None);
}

// Launch TUI
Application.Init();
try
{
    var mainWindow = new MainWindow(engine, store, registry);
    Application.Run(mainWindow);
}
finally
{
    Application.Shutdown();
    // Graceful shutdown: stop all devices
    if (engine.State == RecorderState.Running)
    {
        await engine.StopAsync(null, flush: true, CancellationToken.None);
    }
    await store.FlushAsync(CancellationToken.None);
}
