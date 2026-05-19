using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Sqlite;

/// <summary>
/// Creates SQLite tables on startup across all three databases and tracks schema versions.
/// Runs migrations on luciaconfig, luciatraces, and luciatasks databases independently.
/// </summary>
public sealed class SqliteMigrationRunner : IHostedService
{
    private const int ConfigSchemaVersion = 2;
    private const int TracesSchemaVersion = 1;
    private const int TasksSchemaVersion = 1;

    private readonly SqliteConnectionFactory _configFactory;
    private readonly SqliteConnectionFactory _tracesFactory;
    private readonly SqliteConnectionFactory _tasksFactory;
    private readonly ILogger<SqliteMigrationRunner> _logger;

    public SqliteMigrationRunner(
        [FromKeyedServices(SqliteDbNames.Config)] SqliteConnectionFactory configFactory,
        [FromKeyedServices(SqliteDbNames.Traces)] SqliteConnectionFactory tracesFactory,
        [FromKeyedServices(SqliteDbNames.Tasks)] SqliteConnectionFactory tasksFactory,
        ILogger<SqliteMigrationRunner> logger)
    {
        _configFactory = configFactory;
        _tracesFactory = tracesFactory;
        _tasksFactory = tasksFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        MigrateDatabase(_configFactory, "config", ConfigSchemaVersion, ApplyConfigV1, ApplyConfigV2);
        MigrateDatabase(_tracesFactory, "traces", TracesSchemaVersion, ApplyTracesV1);
        MigrateDatabase(_tasksFactory, "tasks", TasksSchemaVersion, ApplyTasksV1);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void MigrateDatabase(
        SqliteConnectionFactory factory,
        string dbLabel,
        int targetVersion,
        params ReadOnlySpan<Action<SqliteConnection>> migrations)
    {
        using var connection = factory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            EnsureSchemaVersionTable(connection);
            var currentVersion = GetSchemaVersion(connection);

            if (currentVersion < targetVersion)
            {
                _logger.LogInformation("Running SQLite [{DbLabel}] migrations from version {Current} to {Target}...",
                    dbLabel, currentVersion, targetVersion);

                for (var v = currentVersion; v < targetVersion && v < migrations.Length; v++)
                {
                    migrations[v](connection);
                }

                SetSchemaVersion(connection, targetVersion);
                transaction.Commit();
                _logger.LogInformation("SQLite [{DbLabel}] schema migrated to version {Version}.", dbLabel, targetVersion);
            }
            else
            {
                transaction.Commit();
                _logger.LogDebug("SQLite [{DbLabel}] schema is up to date at version {Version}.", dbLabel, currentVersion);
            }
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "SQLite [{DbLabel}] migration failed.", dbLabel);
            throw;
        }
    }

    private static void EnsureSchemaVersionTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static int GetSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version WHERE id = 1;";
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : 0;
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schema_version (id, version) VALUES (1, @version)
            ON CONFLICT(id) DO UPDATE SET version = @version, applied_at = datetime('now');
            """;
        cmd.Parameters.AddWithValue("@version", version);
        cmd.ExecuteNonQuery();
    }

    // ── luciaconfig database migrations ──────────────────────────────────────

    private static void ApplyConfigV1(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS api_keys (
                id TEXT PRIMARY KEY,
                key_hash TEXT NOT NULL UNIQUE,
                key_prefix TEXT NOT NULL,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                last_used_at TEXT,
                expires_at TEXT,
                is_revoked INTEGER NOT NULL DEFAULT 0,
                revoked_at TEXT,
                scopes TEXT NOT NULL DEFAULT '["*"]'
            );
            CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON api_keys(key_hash);
            CREATE INDEX IF NOT EXISTS idx_api_keys_revoked ON api_keys(is_revoked);

            CREATE TABLE IF NOT EXISTS model_providers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS agent_definitions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled INTEGER NOT NULL DEFAULT 1,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mcp_tool_servers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled INTEGER NOT NULL DEFAULT 1,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS response_templates (
                id TEXT PRIMARY KEY,
                skill_id TEXT NOT NULL,
                action TEXT NOT NULL,
                data TEXT NOT NULL,
                UNIQUE(skill_id, action)
            );

            CREATE TABLE IF NOT EXISTS presence_sensor_mappings (
                id TEXT PRIMARY KEY,
                area_id TEXT,
                is_user_override INTEGER NOT NULL DEFAULT 0,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_presence_area ON presence_sensor_mappings(area_id);

            CREATE TABLE IF NOT EXISTS presence_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS plugin_repositories (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS installed_plugins (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS voice_transcripts (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TEXT NOT NULL,
                speaker_id TEXT,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_transcripts_session ON voice_transcripts(session_id);
            CREATE INDEX IF NOT EXISTS idx_transcripts_time ON voice_transcripts(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_transcripts_speaker ON voice_transcripts(speaker_id);

            CREATE TABLE IF NOT EXISTS speaker_profiles (
                id TEXT PRIMARY KEY,
                is_provisional INTEGER NOT NULL DEFAULT 1,
                last_seen_at TEXT,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_profiles_provisional ON speaker_profiles(is_provisional, last_seen_at);

            CREATE TABLE IF NOT EXISTS model_preferences (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void ApplyConfigV2(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS user_memories (
                user_id TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT,
                PRIMARY KEY(user_id, key)
            );
            CREATE INDEX IF NOT EXISTS idx_user_memories_user_id ON user_memories(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_memories_expires_at ON user_memories(expires_at);
            """;
        cmd.ExecuteNonQuery();
    }

    // ── luciatraces database migrations ──────────────────────────────────────

    private static void ApplyTracesV1(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS conversation_traces (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TEXT NOT NULL,
                user_input TEXT,
                label_status TEXT,
                is_errored INTEGER NOT NULL DEFAULT 0,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON conversation_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_traces_session ON conversation_traces(session_id);
            CREATE INDEX IF NOT EXISTS idx_traces_label ON conversation_traces(label_status);

            CREATE TABLE IF NOT EXISTS dataset_exports (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS command_traces (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                clean_text TEXT NOT NULL,
                outcome TEXT NOT NULL,
                skill_id TEXT,
                confidence REAL NOT NULL DEFAULT 0,
                total_duration_ms REAL NOT NULL DEFAULT 0,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_command_traces_timestamp ON command_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_command_traces_outcome ON command_traces(outcome);
            CREATE INDEX IF NOT EXISTS idx_command_traces_skill ON command_traces(skill_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ── luciatasks database migrations ───────────────────────────────────────

    private static void ApplyTasksV1(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                fire_at TEXT,
                task_type TEXT,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_scheduled_status ON scheduled_tasks(status, fire_at);

            CREATE TABLE IF NOT EXISTS alarm_clocks (
                id TEXT PRIMARY KEY,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                next_fire_at TEXT,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_alarms_enabled ON alarm_clocks(is_enabled, next_fire_at);

            CREATE TABLE IF NOT EXISTS alarm_sounds (
                id TEXT PRIMARY KEY,
                is_default INTEGER NOT NULL DEFAULT 0,
                data TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS archived_tasks (
                id TEXT PRIMARY KEY,
                archived_at TEXT NOT NULL DEFAULT (datetime('now')),
                final_state TEXT,
                data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_archived_at ON archived_tasks(archived_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }
}

