using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server.Engine;
using DeviceTrafficMonitor.Server.Storage;

namespace DeviceTrafficMonitor.Server.Tests;

public class StorageBootstrapTests : IAsyncLifetime
{
    private string _tempDir = null!;
    private StorageBootstrapper _bootstrapper = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dtm-test-{Guid.NewGuid():N}");
        var config = new StorageConfig { DataDirectory = _tempDir };
        _bootstrapper = new StorageBootstrapper(config);
        await _bootstrapper.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public void DatabaseFileIsCreated()
    {
        Assert.True(File.Exists(_bootstrapper.DatabasePath));
    }

    [Fact]
    public void ConnectionStringIsSet()
    {
        Assert.False(string.IsNullOrEmpty(_bootstrapper.ConnectionString));
    }
}

public class SqliteTrafficStoreTests : IAsyncLifetime
{
    private string _tempDir = null!;
    private SqliteTrafficStore _store = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dtm-test-{Guid.NewGuid():N}");
        var config = new StorageConfig { DataDirectory = _tempDir, WriteBufferSize = 1 };
        var bootstrapper = new StorageBootstrapper(config);
        await bootstrapper.EnsureCreatedAsync();
        _store = new SqliteTrafficStore(config, bootstrapper.ConnectionString);
        await _store.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteAndQueryLines()
    {
        var lines = new List<ConsoleLine>
        {
            new()
            {
                Timestamp = DateTimeOffset.UtcNow,
                Device = "test-device",
                Content = "[INFO] test message",
                ContentHash = "abc123",
                Severity = "info",
                Sequence = 1
            }
        };

        await _store.WriteAsync(lines, CancellationToken.None);
        await _store.FlushAsync(CancellationToken.None);

        var (results, count) = await _store.QueryAsync(device: "test-device", startTime: null, endTime: null, contains: null, regex: null, severity: null, direction: null, limit: 100, offset: 0, ct: CancellationToken.None);
        Assert.Single(results);
        Assert.Equal(1, count);
        Assert.Equal("[INFO] test message", results[0].Content);
    }

    [Fact]
    public async Task DeduplicatesLines()
    {
        var line = new ConsoleLine
        {
            Timestamp = DateTimeOffset.UtcNow,
            Device = "test-device",
            Content = "duplicate line",
            ContentHash = "dedup-hash-001",
            Sequence = 1
        };

        await _store.WriteAsync([line], CancellationToken.None);
        await _store.FlushAsync(CancellationToken.None);
        await _store.WriteAsync([line], CancellationToken.None);
        await _store.FlushAsync(CancellationToken.None);

        var (results, count) = await _store.QueryAsync(device: "test-device", startTime: null, endTime: null, contains: null, regex: null, severity: null, direction: null, limit: 100, offset: 0, ct: CancellationToken.None);
        Assert.Single(results);
    }

    [Fact]
    public async Task SummarizeTraffic()
    {
        var lines = new List<ConsoleLine>
        {
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "ok", ContentHash = "h1", Severity = "info", Sequence = 1 },
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "bad", ContentHash = "h2", Severity = "error", Sequence = 2 },
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "warn", ContentHash = "h3", Severity = "warn", Sequence = 3 },
        };

        await _store.WriteAsync(lines, CancellationToken.None);
        await _store.FlushAsync(CancellationToken.None);

        var summaries = await _store.SummarizeAsync(device: null, startTime: null, endTime: null, ct: CancellationToken.None);
        Assert.Single(summaries);
        Assert.Equal(3, summaries[0].LineCount);
        Assert.Equal(1, summaries[0].ErrorCount);
        Assert.Equal(1, summaries[0].WarnCount);
    }

    [Fact]
    public async Task SearchTraffic()
    {
        var lines = new List<ConsoleLine>
        {
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "line 1", ContentHash = "s1", Sequence = 1 },
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "ERROR found here", ContentHash = "s2", Sequence = 2 },
            new() { Timestamp = DateTimeOffset.UtcNow, Device = "dev1", Content = "line 3", ContentHash = "s3", Sequence = 3 },
        };

        await _store.WriteAsync(lines, CancellationToken.None);
        await _store.FlushAsync(CancellationToken.None);

        var (matches, total) = await _store.SearchAsync("ERROR", device: null, startTime: null, endTime: null, contextLines: 3, limit: 20, ct: CancellationToken.None);
        Assert.Single(matches);
        Assert.Contains("ERROR", matches[0].MatchLine.Content);
    }
}

public class LineParserTests
{
    [Fact]
    public void ParsesMultipleLines()
    {
        var parser = new LineParser();
        var results = parser.Parse("dev1", ["line1\nline2\nline3"]);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void DetectsSeverity()
    {
        var parser = new LineParser();
        var results = parser.Parse("dev1", [
            "[ERROR] bad thing",
            "[WARN] careful",
            "[INFO] all good",
            "[DEBUG] details",
            "no severity here"
        ]);

        Assert.Equal("error", results[0].Severity);
        Assert.Equal("warn", results[1].Severity);
        Assert.Equal("info", results[2].Severity);
        Assert.Equal("debug", results[3].Severity);
        Assert.Null(results[4].Severity);
    }

    [Fact]
    public void AssignsSequenceNumbers()
    {
        var parser = new LineParser();
        var results = parser.Parse("dev1", ["a", "b", "c"]);
        Assert.Equal(1, results[0].Sequence);
        Assert.Equal(2, results[1].Sequence);
        Assert.Equal(3, results[2].Sequence);
    }

    [Fact]
    public void ComputesUniqueHashes()
    {
        var parser = new LineParser();
        var results = parser.Parse("dev1", ["line1", "line2"]);
        Assert.NotEqual(results[0].ContentHash, results[1].ContentHash);
    }

    [Fact]
    public void SkipsEmptyLines()
    {
        var parser = new LineParser();
        var results = parser.Parse("dev1", ["line1\n\n\nline2"]);
        Assert.Equal(2, results.Count);
    }
}

public class DeviceRegistryTests
{
    [Fact]
    public void AddAndRetrieveDevice()
    {
        var registry = new DeviceRegistry();
        var config = new DeviceConfig
        {
            Id = "dev1",
            DisplayName = "Device 1",
            McpServerCommand = "/usr/bin/echo"
        };

        registry.Add(config, "runtime");
        Assert.True(registry.Exists("dev1"));
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void RejectsDuplicateId()
    {
        var registry = new DeviceRegistry();
        var config = new DeviceConfig
        {
            Id = "dev1",
            DisplayName = "Device 1",
            McpServerCommand = "/usr/bin/echo"
        };

        registry.Add(config, "runtime");
        Assert.Throws<InvalidOperationException>(() => registry.Add(config, "runtime"));
    }

    [Fact]
    public void RemoveDevice()
    {
        var registry = new DeviceRegistry();
        var config = new DeviceConfig
        {
            Id = "dev1",
            DisplayName = "Device 1",
            McpServerCommand = "/usr/bin/echo"
        };

        registry.Add(config, "runtime");
        Assert.True(registry.Remove("dev1"));
        Assert.False(registry.Exists("dev1"));
    }

    [Fact]
    public void GetRegistrationReturnsSource()
    {
        var registry = new DeviceRegistry();
        var config = new DeviceConfig
        {
            Id = "dev1",
            DisplayName = "Device 1",
            McpServerCommand = "/usr/bin/echo"
        };

        registry.Add(config, "runtime");
        var reg = registry.GetRegistration("dev1");
        Assert.Equal("runtime", reg.Source);
    }
}
