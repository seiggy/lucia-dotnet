using lucia.Data.Sqlite;

using Microsoft.Data.Sqlite;

namespace lucia.Tests.Data;

/// <summary>
/// Verifies the V2 UTC-normalization migrations against rows containing non-UTC offsets.
/// Guards against the DateTimeStyles.RoundtripKind/ArgumentException regression.
/// </summary>
public sealed class SqliteMigrationRunnerV2Tests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteMigrationRunnerV2Tests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    // ── ApplyTracesV2 ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyTracesV2_NonUtcOffset_RewritesToCanonicalUtc()
    {
        CreateCommandTracesTable();

        // Insert a row whose timestamp carries +02:00 — the bug caused ArgumentException here.
        const string nonUtc = "2025-06-01T14:00:00.0000000+02:00"; // UTC equivalent: 12:00Z
        InsertCommandTrace("trace-1", nonUtc);

        // Act — must not throw
        SqliteMigrationRunner.ApplyTracesV2(_connection);

        // Assert — stored value is canonical UTC +00:00
        var stored = ReadCommandTraceTimestamp("trace-1");
        Assert.Equal("2025-06-01T12:00:00.0000000+00:00", stored);
    }

    [Fact]
    public void ApplyTracesV2_AlreadyUtc_RemainsStable()
    {
        CreateCommandTracesTable();

        const string alreadyUtc = "2025-06-01T12:00:00.0000000+00:00";
        InsertCommandTrace("trace-2", alreadyUtc);

        SqliteMigrationRunner.ApplyTracesV2(_connection);

        Assert.Equal(alreadyUtc, ReadCommandTraceTimestamp("trace-2"));
    }

    [Fact]
    public void ApplyTracesV2_EmptyTable_DoesNotThrow()
    {
        CreateCommandTracesTable();

        // Empty table — migration should be a no-op without throwing.
        SqliteMigrationRunner.ApplyTracesV2(_connection);
    }

    [Fact]
    public void ApplyTracesV2_MultipleRows_AllNormalized()
    {
        CreateCommandTracesTable();

        // Insert rows with varied offsets to exercise the keyset-paged batch path.
        InsertCommandTrace("trace-a", "2025-01-01T14:00:00.0000000+02:00"); // UTC: 12:00
        InsertCommandTrace("trace-b", "2025-01-01T09:00:00.0000000-05:00"); // UTC: 14:00
        InsertCommandTrace("trace-c", "2025-01-01T12:00:00.0000000+00:00"); // already canonical

        SqliteMigrationRunner.ApplyTracesV2(_connection);

        Assert.Equal("2025-01-01T12:00:00.0000000+00:00", ReadCommandTraceTimestamp("trace-a"));
        Assert.Equal("2025-01-01T14:00:00.0000000+00:00", ReadCommandTraceTimestamp("trace-b"));
        Assert.Equal("2025-01-01T12:00:00.0000000+00:00", ReadCommandTraceTimestamp("trace-c")); // unchanged
    }

    // ── ApplyTasksV2 ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyTasksV2_NonUtcOffset_RewritesToCanonicalUtc()
    {
        CreateScheduledTasksTable();

        const string nonUtc = "2025-06-01T09:00:00.0000000-05:00"; // UTC equivalent: 14:00Z
        InsertScheduledTask("task-1", nonUtc);

        SqliteMigrationRunner.ApplyTasksV2(_connection);

        var stored = ReadScheduledTaskFireAt("task-1");
        Assert.Equal("2025-06-01T14:00:00.0000000+00:00", stored);
    }

    [Fact]
    public void ApplyTasksV2_AlreadyUtc_RemainsStable()
    {
        CreateScheduledTasksTable();

        const string alreadyUtc = "2025-06-01T14:00:00.0000000+00:00";
        InsertScheduledTask("task-2", alreadyUtc);

        SqliteMigrationRunner.ApplyTasksV2(_connection);

        Assert.Equal(alreadyUtc, ReadScheduledTaskFireAt("task-2"));
    }

    [Fact]
    public void ApplyTasksV2_NullFireAt_IsSkipped()
    {
        CreateScheduledTasksTable();

        // Row with NULL fire_at must not cause any error.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO scheduled_tasks (id, status, fire_at, task_type, data) VALUES ('task-3', 'Pending', NULL, 'Timer', '{}');";
        cmd.ExecuteNonQuery();

        SqliteMigrationRunner.ApplyTasksV2(_connection);

        // fire_at should remain NULL
        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT fire_at FROM scheduled_tasks WHERE id = 'task-3';";
        Assert.True(check.ExecuteScalar() is DBNull or null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CreateCommandTracesTable()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS command_traces (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                clean_text TEXT NOT NULL DEFAULT '',
                outcome TEXT NOT NULL DEFAULT 'Unknown',
                skill_id TEXT,
                confidence REAL NOT NULL DEFAULT 0,
                total_duration_ms REAL NOT NULL DEFAULT 0,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void InsertCommandTrace(string id, string timestamp)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO command_traces (id, timestamp) VALUES (@id, @ts);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@ts", timestamp);
        cmd.ExecuteNonQuery();
    }

    private string ReadCommandTraceTimestamp(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT timestamp FROM command_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return (string)cmd.ExecuteScalar()!;
    }

    private void CreateScheduledTasksTable()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL DEFAULT 'Pending',
                fire_at TEXT,
                task_type TEXT,
                data TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void InsertScheduledTask(string id, string fireAt)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO scheduled_tasks (id, fire_at) VALUES (@id, @fireAt);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@fireAt", fireAt);
        cmd.ExecuteNonQuery();
    }

    private string ReadScheduledTaskFireAt(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT fire_at FROM scheduled_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return (string)cmd.ExecuteScalar()!;
    }

    public void Dispose() => _connection.Dispose();
}
