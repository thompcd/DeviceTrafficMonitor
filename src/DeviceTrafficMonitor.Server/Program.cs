using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Services;
using DeviceTrafficMonitor.Server.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Log to stderr — stdout is the MCP wire channel
builder.Logging.AddConsole(opts =>
{
    opts.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Bind config sections
var storageConfig = builder.Configuration.GetSection("Storage").Get<StorageConfig>() ?? new StorageConfig();
var recorderConfig = builder.Configuration.GetSection("Recorder").Get<RecorderConfig>() ?? new RecorderConfig();

// Bootstrap storage
var bootstrapper = new StorageBootstrapper(storageConfig);
await bootstrapper.EnsureCreatedAsync();

// Register DI services
builder.Services.AddSingleton(storageConfig);
builder.Services.AddSingleton(recorderConfig);

builder.Services.AddSingleton<ITrafficStore>(sp =>
{
    var store = new SqliteTrafficStore(storageConfig, bootstrapper.ConnectionString);
    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    return store;
});

var registry = new DeviceRegistry();
registry.LoadFromConfig(builder.Configuration);
builder.Services.AddSingleton(registry);

builder.Services.AddSingleton<IMcpClientFactory, StdioMcpClientFactory>();

builder.Services.AddSingleton<IRecorderEngine>(sp =>
{
    var store = sp.GetRequiredService<ITrafficStore>();
    var factory = sp.GetRequiredService<IMcpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<RecorderEngine>>();
    return new RecorderEngine(
        registry, factory, store, logger,
        recorderConfig.DefaultPollDurationSeconds,
        recorderConfig.DrainTimeoutSeconds);
});

// Register MCP server with stdio transport and auto-discover tools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Register retention background service
builder.Services.AddHostedService<RetentionService>();

await builder.Build().RunAsync();
