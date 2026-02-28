using System.Text;
using System.Text.RegularExpressions;
using DeviceTrafficMonitor.Core.Interfaces;
using DeviceTrafficMonitor.Core.Models;
using Microsoft.Data.Sqlite;

namespace DeviceTrafficMonitor.Server.Storage;

public class SqliteTrafficStore : ITrafficStore, IAsyncDisposable
{
    private readonly StorageConfig _config;
    private readonly string _connectionString;
    private readonly Lock _writeLock = new();
    private readonly List<ConsoleLine> _writeBuffer = new();
    private readonly Timer _flushTimer;
    private SqliteConnection? _writeConnection;
    private bool _disposed;

    public SqliteTrafficStore(StorageConfig config, string connectionString)
    {
        _config = config;
        _connectionString = connectionString;
        _flushTimer = new Timer(_ => _ = FlushBufferAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        _writeConnection = new SqliteConnection(_connectionString);
        await _writeConnection.OpenAsync(ct);

        using var cmd = _writeConnection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=NORMAL;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task WriteAsync(IReadOnlyList<ConsoleLine> lines, CancellationToken ct)
    {
        lock (_writeLock)
        {
            _writeBuffer.AddRange(lines);
            if (_writeBuffer.Count >= _config.WriteBufferSize)
            {
                return FlushBufferAsync(ct);
            }
        }
        return Task.CompletedTask;
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        await FlushBufferAsync(ct);
    }

    private async Task FlushBufferAsync(CancellationToken ct = default)
    {
        List<ConsoleLine> toFlush;
        lock (_writeLock)
        {
            if (_writeBuffer.Count == 0) return;
            toFlush = new List<ConsoleLine>(_writeBuffer);
            _writeBuffer.Clear();
        }

        if (_writeConnection is null || _disposed) return;

        await using var transaction = await _writeConnection.BeginTransactionAsync(ct);

        foreach (var line in toFlush)
        {
            // Check for duplicate
            using var checkCmd = _writeConnection.CreateCommand();
            checkCmd.CommandText = "SELECT EXISTS(SELECT 1 FROM console_lines WHERE content_hash = @hash)";
            checkCmd.Parameters.AddWithValue("@hash", line.ContentHash);
            var exists = Convert.ToBoolean(await checkCmd.ExecuteScalarAsync(ct));
            if (exists) continue;

            using var insertCmd = _writeConnection.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO console_lines (timestamp, device, direction, severity, content, content_hash, sequence)
                VALUES (@timestamp, @device, @direction, @severity, @content, @content_hash, @sequence)
                """;
            insertCmd.Parameters.AddWithValue("@timestamp", line.Timestamp.ToString("o"));
            insertCmd.Parameters.AddWithValue("@device", line.Device);
            insertCmd.Parameters.AddWithValue("@direction", (object?)line.Direction ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@severity", (object?)line.Severity ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@content", line.Content);
            insertCmd.Parameters.AddWithValue("@content_hash", line.ContentHash);
            insertCmd.Parameters.AddWithValue("@sequence", line.Sequence);
            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<(IReadOnlyList<ConsoleLine> Lines, long TotalCount)> QueryAsync(
        string? device, DateTimeOffset? startTime, DateTimeOffset? endTime,
        string? contains, string? regex, string? severity, string? direction,
        int limit, int offset, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereClauses = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (device is not null)
        {
            whereClauses.Add("device = @device");
            parameters.Add(new SqliteParameter("@device", device));
        }
        if (startTime.HasValue)
        {
            whereClauses.Add("timestamp >= @startTime");
            parameters.Add(new SqliteParameter("@startTime", startTime.Value.ToString("o")));
        }
        if (endTime.HasValue)
        {
            whereClauses.Add("timestamp <= @endTime");
            parameters.Add(new SqliteParameter("@endTime", endTime.Value.ToString("o")));
        }
        if (contains is not null)
        {
            whereClauses.Add("content LIKE @contains");
            parameters.Add(new SqliteParameter("@contains", $"%{contains}%"));
        }
        if (severity is not null)
        {
            whereClauses.Add("severity = @severity");
            parameters.Add(new SqliteParameter("@severity", severity));
        }
        if (direction is not null)
        {
            whereClauses.Add("direction = @direction");
            parameters.Add(new SqliteParameter("@direction", direction));
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        // Get total count
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM console_lines {whereClause}";
        countCmd.Parameters.AddRange(parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray());
        var totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        // Get rows
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, timestamp, device, direction, severity, content, content_hash, sequence
            FROM console_lines {whereClause}
            ORDER BY timestamp DESC, sequence ASC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddRange(parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray());
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var lines = new List<ConsoleLine>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lines.Add(ReadConsoleLine(reader));
        }

        // Apply regex filter in-memory if specified (SQLite doesn't natively support regex)
        if (regex is not null)
        {
            var rx = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            lines = lines.Where(l => rx.IsMatch(l.Content)).ToList();
        }

        return (lines, totalCount);
    }

    public async Task<(IReadOnlyList<SearchMatch> Matches, long TotalMatches)> SearchAsync(
        string pattern, string? device, DateTimeOffset? startTime, DateTimeOffset? endTime,
        int contextLines, int limit, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereClauses = new List<string> { "content LIKE @pattern" };
        var parameters = new List<SqliteParameter>
        {
            new("@pattern", $"%{pattern}%")
        };

        if (device is not null)
        {
            whereClauses.Add("device = @device");
            parameters.Add(new SqliteParameter("@device", device));
        }
        if (startTime.HasValue)
        {
            whereClauses.Add("timestamp >= @startTime");
            parameters.Add(new SqliteParameter("@startTime", startTime.Value.ToString("o")));
        }
        if (endTime.HasValue)
        {
            whereClauses.Add("timestamp <= @endTime");
            parameters.Add(new SqliteParameter("@endTime", endTime.Value.ToString("o")));
        }

        var whereClause = string.Join(" AND ", whereClauses);

        // Get matching line IDs
        using var matchCmd = conn.CreateCommand();
        matchCmd.CommandText = $"SELECT id, device FROM console_lines WHERE {whereClause} ORDER BY timestamp DESC LIMIT @limit";
        matchCmd.Parameters.AddRange(parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray());
        matchCmd.Parameters.AddWithValue("@limit", limit);

        var matchIds = new List<(long Id, string Device)>();
        await using var matchReader = await matchCmd.ExecuteReaderAsync(ct);
        while (await matchReader.ReadAsync(ct))
        {
            matchIds.Add((matchReader.GetInt64(0), matchReader.GetString(1)));
        }

        // Count total
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM console_lines WHERE {whereClause}";
        countCmd.Parameters.AddRange(parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray());
        var totalMatches = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        // For each match, get context lines
        var matches = new List<SearchMatch>();
        foreach (var (matchId, matchDevice) in matchIds)
        {
            using var contextCmd = conn.CreateCommand();
            contextCmd.CommandText = """
                SELECT id, timestamp, device, direction, severity, content, content_hash, sequence
                FROM console_lines
                WHERE device = @device AND id BETWEEN @minId AND @maxId
                ORDER BY id ASC
                """;
            contextCmd.Parameters.AddWithValue("@device", matchDevice);
            contextCmd.Parameters.AddWithValue("@minId", matchId - contextLines);
            contextCmd.Parameters.AddWithValue("@maxId", matchId + contextLines);

            var contextResults = new List<ConsoleLine>();
            await using var contextReader = await contextCmd.ExecuteReaderAsync(ct);
            while (await contextReader.ReadAsync(ct))
            {
                contextResults.Add(ReadConsoleLine(contextReader));
            }

            var matchLine = contextResults.FirstOrDefault(l => l.Id == matchId);
            if (matchLine is null) continue;

            matches.Add(new SearchMatch
            {
                MatchLine = matchLine,
                BeforeLines = contextResults.Where(l => l.Id < matchId).ToList(),
                AfterLines = contextResults.Where(l => l.Id > matchId).ToList()
            });
        }

        return (matches, totalMatches);
    }

    public async Task<IReadOnlyList<TrafficSummary>> SummarizeAsync(
        string? device, DateTimeOffset? startTime, DateTimeOffset? endTime, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereClauses = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (device is not null)
        {
            whereClauses.Add("device = @device");
            parameters.Add(new SqliteParameter("@device", device));
        }
        if (startTime.HasValue)
        {
            whereClauses.Add("timestamp >= @startTime");
            parameters.Add(new SqliteParameter("@startTime", startTime.Value.ToString("o")));
        }
        if (endTime.HasValue)
        {
            whereClauses.Add("timestamp <= @endTime");
            parameters.Add(new SqliteParameter("@endTime", endTime.Value.ToString("o")));
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                device,
                COUNT(*) as line_count,
                SUM(CASE WHEN severity = 'error' THEN 1 ELSE 0 END) as error_count,
                SUM(CASE WHEN severity = 'warn' THEN 1 ELSE 0 END) as warn_count,
                MIN(timestamp) as first_line_at,
                MAX(timestamp) as last_line_at
            FROM console_lines {whereClause}
            GROUP BY device
            """;
        cmd.Parameters.AddRange(parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray());

        var summaries = new List<TrafficSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dev = reader.GetString(0);

            // Get sample errors for this device
            using var errCmd = conn.CreateCommand();
            errCmd.CommandText = $"""
                SELECT content FROM console_lines
                WHERE device = @device AND severity = 'error'
                    {(startTime.HasValue ? "AND timestamp >= @startTime" : "")}
                    {(endTime.HasValue ? "AND timestamp <= @endTime" : "")}
                ORDER BY timestamp DESC LIMIT 5
                """;
            errCmd.Parameters.AddWithValue("@device", dev);
            if (startTime.HasValue) errCmd.Parameters.AddWithValue("@startTime", startTime.Value.ToString("o"));
            if (endTime.HasValue) errCmd.Parameters.AddWithValue("@endTime", endTime.Value.ToString("o"));

            var sampleErrors = new List<string>();
            await using var errReader = await errCmd.ExecuteReaderAsync(ct);
            while (await errReader.ReadAsync(ct))
            {
                sampleErrors.Add(errReader.GetString(0));
            }

            summaries.Add(new TrafficSummary
            {
                Device = dev,
                LineCount = reader.GetInt64(1),
                ErrorCount = reader.GetInt64(2),
                WarnCount = reader.GetInt64(3),
                FirstLineAt = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                LastLineAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                SampleErrors = sampleErrors
            });
        }

        return summaries;
    }

    public async Task WriteEventAsync(string eventType, string? device, string? detail, CancellationToken ct)
    {
        if (_writeConnection is null) return;

        using var cmd = _writeConnection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO recorder_events (timestamp, event, device, detail)
            VALUES (@timestamp, @event, @device, @detail)
            """;
        cmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@event", eventType);
        cmd.Parameters.AddWithValue("@device", (object?)device ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@detail", (object?)detail ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task WriteStatusAsync(DeviceStatus status, CancellationToken ct)
    {
        if (_writeConnection is null) return;

        using var cmd = _writeConnection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO device_status (device, state, error_message, lines_recorded, last_line_at, updated_at)
            VALUES (@device, @state, @error_message, @lines_recorded, @last_line_at, @updated_at)
            """;
        cmd.Parameters.AddWithValue("@device", status.Device);
        cmd.Parameters.AddWithValue("@state", status.State);
        cmd.Parameters.AddWithValue("@error_message", (object?)status.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lines_recorded", status.LinesRecorded);
        cmd.Parameters.AddWithValue("@last_line_at", status.LastLineAt.HasValue ? status.LastLineAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RunRetentionAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays).ToString("o");

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM console_lines WHERE timestamp < @cutoff";
        deleteCmd.Parameters.AddWithValue("@cutoff", cutoff);
        await deleteCmd.ExecuteNonQueryAsync(ct);

        // Also clean old events
        using var eventCmd = conn.CreateCommand();
        eventCmd.CommandText = "DELETE FROM recorder_events WHERE timestamp < @cutoff";
        eventCmd.Parameters.AddWithValue("@cutoff", cutoff);
        await eventCmd.ExecuteNonQueryAsync(ct);

        // Check file size and vacuum if needed
        var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        if (dbPath is not null && File.Exists(dbPath))
        {
            var fileInfo = new FileInfo(dbPath);
            var maxBytes = (long)_config.MaxDatabaseSizeMb * 1024 * 1024;
            if (fileInfo.Length > maxBytes)
            {
                // Delete oldest 10%
                using var pruneCmd = conn.CreateCommand();
                pruneCmd.CommandText = """
                    DELETE FROM console_lines WHERE id IN (
                        SELECT id FROM console_lines ORDER BY timestamp ASC LIMIT (SELECT COUNT(*) / 10 FROM console_lines)
                    )
                    """;
                await pruneCmd.ExecuteNonQueryAsync(ct);

                using var vacuumCmd = conn.CreateCommand();
                vacuumCmd.CommandText = "VACUUM";
                await vacuumCmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static ConsoleLine ReadConsoleLine(SqliteDataReader reader)
    {
        return new ConsoleLine
        {
            Id = reader.GetInt64(0),
            Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
            Device = reader.GetString(2),
            Direction = reader.IsDBNull(3) ? null : reader.GetString(3),
            Severity = reader.IsDBNull(4) ? null : reader.GetString(4),
            Content = reader.GetString(5),
            ContentHash = reader.GetString(6),
            Sequence = reader.GetInt32(7)
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _flushTimer.DisposeAsync();
        await FlushBufferAsync();

        if (_writeConnection is not null)
        {
            await _writeConnection.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
