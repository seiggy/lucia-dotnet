using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Creates PostgreSQL tables on startup and tracks schema versions for future migrations.
/// </summary>
public sealed partial class PostgresMigrationRunner : IHostedService
{
    private const int CurrentSchemaVersion = 2;

    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresMigrationRunner> _logger;

    public PostgresMigrationRunner(
        PostgresConnectionFactory connectionFactory,
        ILogger<PostgresMigrationRunner> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureSchemaVersionTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var currentVersion = await GetSchemaVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            if (currentVersion < CurrentSchemaVersion)
            {
                LogRunningMigrations(_logger, currentVersion, CurrentSchemaVersion);

                if (currentVersion < 1)
                {
                    await ApplyVersion1Async(connection, transaction, cancellationToken).ConfigureAwait(false);
                }

                if (currentVersion < 2)
                {
                    await ApplyVersion2Async(connection, transaction, cancellationToken).ConfigureAwait(false);
                }

                await SetSchemaVersionAsync(connection, transaction, CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                LogMigrationCompleted(_logger, CurrentSchemaVersion);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                LogSchemaCurrent(_logger, currentVersion);
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            LogMigrationFailed(_logger, ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Running PostgreSQL migrations from version {CurrentVersion} to {TargetVersion}...")]
    private static partial void LogRunningMigrations(ILogger logger, int currentVersion, int targetVersion);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "PostgreSQL schema migrated to version {Version}.")]
    private static partial void LogMigrationCompleted(ILogger logger, int version);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "PostgreSQL schema is up to date at version {Version}.")]
    private static partial void LogSchemaCurrent(ILogger logger, int version);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "PostgreSQL migration failed.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    private static async Task EnsureSchemaVersionTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT version FROM schema_version WHERE id = 1;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task SetSchemaVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int version, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO schema_version (id, version) VALUES (1, @version)
            ON CONFLICT (id) DO UPDATE SET version = EXCLUDED.version, applied_at = CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("version", version);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersion1Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive BOOLEAN NOT NULL DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS api_keys (
                id TEXT PRIMARY KEY,
                key_hash TEXT NOT NULL UNIQUE,
                key_prefix TEXT NOT NULL,
                name TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_used_at TIMESTAMPTZ,
                expires_at TIMESTAMPTZ,
                is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
                revoked_at TIMESTAMPTZ,
                scopes JSONB NOT NULL DEFAULT '["*"]'::jsonb
            );
            CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON api_keys(key_hash);
            CREATE INDEX IF NOT EXISTS idx_api_keys_revoked ON api_keys(is_revoked);

            CREATE TABLE IF NOT EXISTS model_providers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS agent_definitions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mcp_tool_servers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS response_templates (
                id TEXT PRIMARY KEY,
                skill_id TEXT NOT NULL,
                action TEXT NOT NULL,
                data JSONB NOT NULL,
                UNIQUE(skill_id, action)
            );

            CREATE TABLE IF NOT EXISTS presence_sensor_mappings (
                id TEXT PRIMARY KEY,
                area_id TEXT,
                is_user_override BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_presence_area ON presence_sensor_mappings(area_id);

            CREATE TABLE IF NOT EXISTS presence_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS plugin_repositories (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS installed_plugins (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS conversation_traces (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TIMESTAMPTZ NOT NULL,
                user_input TEXT,
                label_status TEXT,
                is_errored BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON conversation_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_traces_session ON conversation_traces(session_id);
            CREATE INDEX IF NOT EXISTS idx_traces_label ON conversation_traces(label_status);

            CREATE TABLE IF NOT EXISTS dataset_exports (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS scheduled_tasks (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                fire_at TIMESTAMPTZ,
                task_type TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_scheduled_status ON scheduled_tasks(status, fire_at);

            CREATE TABLE IF NOT EXISTS alarm_clocks (
                id TEXT PRIMARY KEY,
                is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                next_fire_at TIMESTAMPTZ,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_alarms_enabled ON alarm_clocks(is_enabled, next_fire_at);

            CREATE TABLE IF NOT EXISTS alarm_sounds (
                id TEXT PRIMARY KEY,
                is_default BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS archived_tasks (
                id TEXT PRIMARY KEY,
                archived_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                final_state TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_archived_at ON archived_tasks(archived_at DESC);

            CREATE TABLE IF NOT EXISTS voice_transcripts (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TIMESTAMPTZ NOT NULL,
                speaker_id TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_transcripts_session ON voice_transcripts(session_id);
            CREATE INDEX IF NOT EXISTS idx_transcripts_time ON voice_transcripts(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_transcripts_speaker ON voice_transcripts(speaker_id);

            CREATE TABLE IF NOT EXISTS speaker_profiles (
                id TEXT PRIMARY KEY,
                is_provisional BOOLEAN NOT NULL DEFAULT TRUE,
                last_seen_at TIMESTAMPTZ,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_profiles_provisional ON speaker_profiles(is_provisional, last_seen_at);

            CREATE TABLE IF NOT EXISTS model_preferences (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS command_traces (
                id TEXT PRIMARY KEY,
                timestamp TIMESTAMPTZ NOT NULL,
                clean_text TEXT NOT NULL,
                outcome TEXT NOT NULL,
                skill_id TEXT,
                confidence DOUBLE PRECISION NOT NULL DEFAULT 0,
                total_duration_ms DOUBLE PRECISION NOT NULL DEFAULT 0,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_command_traces_timestamp ON command_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_command_traces_outcome ON command_traces(outcome);
            CREATE INDEX IF NOT EXISTS idx_command_traces_skill ON command_traces(skill_id);
            """;

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersion2Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS user_memories (
                user_id TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ,
                PRIMARY KEY (user_id, key)
            );
            CREATE INDEX IF NOT EXISTS idx_user_memories_user_id ON user_memories(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_memories_expires_at ON user_memories(expires_at);
            """;

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
