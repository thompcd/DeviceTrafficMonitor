using Microsoft.Data.Sqlite;

namespace DeviceTrafficMonitor.Server.Storage.Migrations;

public static class V1_InitialSchema
{
    public const int Version = 1;

    public static void Apply(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS console_lines (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp     TEXT    NOT NULL,
                device        TEXT    NOT NULL,
                direction     TEXT,
                severity      TEXT,
                content       TEXT    NOT NULL,
                content_hash  TEXT    NOT NULL,
                sequence      INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_lines_device_time ON console_lines (device, timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_lines_time ON console_lines (timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_lines_severity ON console_lines (severity, timestamp DESC) WHERE severity IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_lines_dedup ON console_lines (content_hash);

            CREATE TABLE IF NOT EXISTS device_status (
                device         TEXT PRIMARY KEY,
                state          TEXT NOT NULL,
                error_message  TEXT,
                lines_recorded INTEGER NOT NULL DEFAULT 0,
                last_line_at   TEXT,
                updated_at     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS recorder_events (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp       TEXT NOT NULL,
                event           TEXT NOT NULL,
                device          TEXT,
                detail          TEXT,
                config_snapshot TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_events_time ON recorder_events (timestamp DESC);

            CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);
            INSERT INTO schema_info (version) VALUES (1);
            """;
        cmd.ExecuteNonQuery();
    }
}
