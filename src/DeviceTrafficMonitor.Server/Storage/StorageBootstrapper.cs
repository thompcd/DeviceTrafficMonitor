using DeviceTrafficMonitor.Core.Models;
using DeviceTrafficMonitor.Server.Storage.Migrations;
using Microsoft.Data.Sqlite;

namespace DeviceTrafficMonitor.Server.Storage;

public class StorageBootstrapper
{
    private readonly StorageConfig _config;

    public StorageBootstrapper(StorageConfig config)
    {
        _config = config;
    }

    public string ConnectionString { get; private set; } = string.Empty;
    public string DatabasePath { get; private set; } = string.Empty;

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        var dataDir = ResolveDataDirectory();
        Directory.CreateDirectory(dataDir);

        DatabasePath = Path.Combine(dataDir, "traffic.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);

        SetPragmas(connection);
        ApplyMigrations(connection);
    }

    private string ResolveDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_config.DataDirectory))
            return _config.DataDirectory;

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "device-traffic-monitor");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "device-traffic-monitor");
        }

        // Linux
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(xdgData))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            xdgData = Path.Combine(home, ".local", "share");
        }
        return Path.Combine(xdgData, "device-traffic-monitor");
    }

    private static void SetPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=NORMAL;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void ApplyMigrations(SqliteConnection connection)
    {
        var currentVersion = GetCurrentVersion(connection);

        if (currentVersion < V1_InitialSchema.Version)
        {
            V1_InitialSchema.Apply(connection);
        }
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_info'";
        var result = cmd.ExecuteScalar();
        if (result is null)
            return 0;

        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_info";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
