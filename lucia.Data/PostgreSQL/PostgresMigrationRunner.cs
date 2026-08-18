using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// Creates PostgreSQL tables on startup across all three databases and tracks schema versions.
/// Runs migrations on luciaconfig, luciatraces, and luciatasks databases independently.
/// </summary>
public sealed partial class PostgresMigrationRunner : IHostedService
{
    internal const long AdvisoryLockKey = 0x4C554349414D4947;

    private const int ConfigSchemaVersion = 2;
    private const int TracesSchemaVersion = 2;
    private const int TasksSchemaVersion = 2;
    private const string TraceSearchIndex = "idx_command_traces_clean_text_trgm";
    private const string TaskSearchIndex = "idx_archived_tasks_data_trgm";
    private static readonly TimeSpan s_defaultLockTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgresConnectionFactory _configFactory;
    private readonly PostgresConnectionFactory _tracesFactory;
    private readonly PostgresConnectionFactory _tasksFactory;
    private readonly ILogger<PostgresMigrationRunner> _logger;
    private readonly TimeSpan _lockTimeout;

    public PostgresMigrationRunner(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory configFactory,
        [FromKeyedServices(PostgresDbNames.Traces)] PostgresConnectionFactory tracesFactory,
        [FromKeyedServices(PostgresDbNames.Tasks)] PostgresConnectionFactory tasksFactory,
        ILogger<PostgresMigrationRunner> logger)
        : this(configFactory, tracesFactory, tasksFactory, logger, s_defaultLockTimeout)
    {
    }

    internal PostgresMigrationRunner(
        PostgresConnectionFactory configFactory,
        PostgresConnectionFactory tracesFactory,
        PostgresConnectionFactory tasksFactory,
        ILogger<PostgresMigrationRunner> logger,
        TimeSpan lockTimeout)
    {
        if (lockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        }

        _configFactory = configFactory;
        _tracesFactory = tracesFactory;
        _tasksFactory = tasksFactory;
        _logger = logger;
        _lockTimeout = lockTimeout;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var config = await _configFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var traces = await _tracesFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tasks = await _tasksFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var configLocked = false;

        try
        {
            await AcquireLockAsync(config, "config", cancellationToken).ConfigureAwait(false);
            configLocked = true;

            var configState = await InspectVersionStateAsync(config, cancellationToken).ConfigureAwait(false);
            var tracesState = await InspectVersionStateAsync(traces, cancellationToken).ConfigureAwait(false);
            var tasksState = await InspectVersionStateAsync(tasks, cancellationToken).ConfigureAwait(false);
            var traceIndexValid = await IsTraceSearchIndexValidAsync(traces, cancellationToken).ConfigureAwait(false);
            var taskIndexValid = await IsTaskSearchIndexValidAsync(tasks, cancellationToken).ConfigureAwait(false);

            ThrowForFutureVersion("config", configState.Version, ConfigSchemaVersion);
            ThrowForFutureVersion("traces", tracesState.Version, TracesSchemaVersion);
            ThrowForFutureVersion("tasks", tasksState.Version, TasksSchemaVersion);

            if (tracesState.Version == TracesSchemaVersion && !traceIndexValid)
            {
                await DemoteVersionAsync(traces, "traces", cancellationToken).ConfigureAwait(false);
            }

            if (tasksState.Version == TasksSchemaVersion && !taskIndexValid)
            {
                await DemoteVersionAsync(tasks, "tasks", cancellationToken).ConfigureAwait(false);
            }

            await MigrateDatabaseAsync(
                config,
                "config",
                ConfigSchemaVersion,
                [ApplyConfigV1Async, ApplyConfigV2Async],
                cancellationToken).ConfigureAwait(false);
            await MigrateDatabaseAsync(
                traces,
                "traces",
                1,
                [ApplyTracesV1Async],
                cancellationToken).ConfigureAwait(false);
            await EnsureTraceSearchIndexAsync(traces, cancellationToken).ConfigureAwait(false);
            await MigrateDatabaseAsync(
                tasks,
                "tasks",
                1,
                [ApplyTasksV1Async],
                cancellationToken).ConfigureAwait(false);
            await EnsureTaskSearchIndexAsync(tasks, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (configLocked)
            {
                await ReleaseLockAsync(config).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateDatabaseAsync(
        NpgsqlConnection connection,
        string dbLabel,
        int targetVersion,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task>[] migrations,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureSchemaVersionTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var currentVersion = await GetSchemaVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            if (currentVersion < targetVersion)
            {
                LogRunningMigrations(_logger, dbLabel, currentVersion, targetVersion);

                for (var v = currentVersion; v < targetVersion && v < migrations.Length; v++)
                {
                    await migrations[v](connection, transaction, cancellationToken).ConfigureAwait(false);
                }

                await SetSchemaVersionAsync(connection, transaction, targetVersion, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                LogMigrationCompleted(_logger, dbLabel, targetVersion);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                LogSchemaCurrent(_logger, dbLabel, currentVersion);
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            LogMigrationFailed(_logger, dbLabel, ex);
            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Running PostgreSQL [{DbLabel}] migrations from version {CurrentVersion} to {TargetVersion}...")]
    private static partial void LogRunningMigrations(ILogger logger, string dbLabel, int currentVersion, int targetVersion);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "PostgreSQL [{DbLabel}] schema migrated to version {Version}.")]
    private static partial void LogMigrationCompleted(ILogger logger, string dbLabel, int version);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "PostgreSQL [{DbLabel}] schema is up to date at version {Version}.")]
    private static partial void LogSchemaCurrent(ILogger logger, string dbLabel, int version);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "PostgreSQL [{DbLabel}] migration failed.")]
    private static partial void LogMigrationFailed(ILogger logger, string dbLabel, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "PostgreSQL [{DbLabel}] schema version demoted to 1 because its published v2 search index is invalid.")]
    private static partial void LogSchemaDemoted(ILogger logger, string dbLabel);

    private async Task AcquireLockAsync(
        NpgsqlConnection connection,
        string dbLabel,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_lockTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            while (true)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_catalog.pg_try_advisory_lock(@key);";
                command.Parameters.AddWithValue("key", AdvisoryLockKey);
                if ((bool)(await command.ExecuteScalarAsync(linked.Token).ConfigureAwait(false))!)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for the PostgreSQL [{dbLabel}] migration advisory lock.");
        }
    }

    private static async Task ReleaseLockAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key);";
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<(bool Exists, int Version)> InspectVersionStateAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var relationCommand = connection.CreateCommand();
        relationCommand.CommandText = """
            SELECT c.relkind
            FROM pg_catalog.pg_class AS c
            INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = 'schema_version';
            """;
        var relationKind = await relationCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (relationKind is null)
        {
            return (false, 0);
        }

        if ((char)relationKind != 'r' || !await IsVersionTableShapeValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "public.schema_version must be an ordinary table with exactly id integer primary key constrained to 1, version integer, and applied_at timestamptz.");
        }

        return (true, await GetSchemaVersionAsync(connection, null, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<bool> IsVersionTableShapeValidAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT count(*) = 3
                 FROM pg_catalog.pg_attribute
                 WHERE attrelid = 'public.schema_version'::regclass
                   AND attnum > 0 AND NOT attisdropped)
                AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute
                    WHERE attrelid = 'public.schema_version'::regclass
                      AND attname = 'id' AND atttypid = 'integer'::regtype
                      AND attnotnull AND attgenerated = '' AND attidentity = '')
                AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute
                    WHERE attrelid = 'public.schema_version'::regclass
                      AND attname = 'version' AND atttypid = 'integer'::regtype
                      AND attnotnull AND attgenerated = '' AND attidentity = '')
                AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute
                    WHERE attrelid = 'public.schema_version'::regclass
                      AND attname = 'applied_at' AND atttypid = 'timestamptz'::regtype
                      AND attnotnull AND attgenerated = '' AND attidentity = '')
                AND EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_constraint
                    WHERE conrelid = 'public.schema_version'::regclass
                      AND contype = 'p'
                      AND conkey = ARRAY[(
                          SELECT attnum FROM pg_catalog.pg_attribute
                          WHERE attrelid = 'public.schema_version'::regclass AND attname = 'id'
                      )]::smallint[])
                AND EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_constraint
                    WHERE conrelid = 'public.schema_version'::regclass
                      AND contype = 'c'
                      AND pg_catalog.pg_get_constraintdef(oid, true) IN ('CHECK (id = 1)', 'CHECK ((id = 1))'))
                AND EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_attrdef AS d
                    INNER JOIN pg_catalog.pg_attribute AS a
                        ON a.attrelid = d.adrelid AND a.attnum = d.adnum
                    WHERE d.adrelid = 'public.schema_version'::regclass
                      AND a.attname = 'applied_at'
                      AND pg_catalog.pg_get_expr(d.adbin, d.adrelid) IN ('CURRENT_TIMESTAMP', 'now()')
                );
            """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static void ThrowForFutureVersion(string dbLabel, int currentVersion, int targetVersion)
    {
        if (currentVersion > targetVersion)
        {
            throw new InvalidOperationException(
                $"PostgreSQL [{dbLabel}] schema version {currentVersion} is newer than supported version {targetVersion}.");
        }
    }

    private async Task DemoteVersionAsync(
        NpgsqlConnection connection,
        string dbLabel,
        CancellationToken cancellationToken)
    {
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE public.schema_version
                SET version = 1, applied_at = CURRENT_TIMESTAMP
                WHERE id = 1 AND version = 2;
                """;
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException($"PostgreSQL [{dbLabel}] schema version changed during preflight.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (await GetSchemaVersionAsync(connection, null, cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"PostgreSQL [{dbLabel}] schema demotion could not be verified.");
        }

        LogSchemaDemoted(_logger, dbLabel);
    }

    private static async Task EnsureSchemaVersionTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT version FROM public.schema_version WHERE id = 1;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task SetSchemaVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int version, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO public.schema_version (id, version) VALUES (1, @version)
            ON CONFLICT (id) DO UPDATE SET version = EXCLUDED.version, applied_at = CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTraceSearchIndexAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await GetSchemaVersionAsync(connection, null, cancellationToken).ConfigureAwait(false) == TracesSchemaVersion
            && await IsTraceSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await EnsureTrigramExtensionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!await IsTraceSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            await ExecuteOutsideTransactionAsync(
                connection,
                $"DROP INDEX CONCURRENTLY IF EXISTS public.{TraceSearchIndex};",
                cancellationToken).ConfigureAwait(false);
            await ExecuteOutsideTransactionAsync(
                connection,
                $"""
                CREATE INDEX CONCURRENTLY {TraceSearchIndex}
                ON public.command_traces USING gin (clean_text public.gin_trgm_ops);
                """,
                cancellationToken).ConfigureAwait(false);
        }

        if (!await IsTraceSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"PostgreSQL [traces] search index public.{TraceSearchIndex} is invalid.");
        }

        await PublishVersionAsync(connection, TracesSchemaVersion, cancellationToken).ConfigureAwait(false);
        LogMigrationCompleted(_logger, "traces", TracesSchemaVersion);
    }

    private async Task EnsureTaskSearchIndexAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await GetSchemaVersionAsync(connection, null, cancellationToken).ConfigureAwait(false) == TasksSchemaVersion
            && await IsTaskSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await EnsureTrigramExtensionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!await IsTaskSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            await ExecuteOutsideTransactionAsync(
                connection,
                $"DROP INDEX CONCURRENTLY IF EXISTS public.{TaskSearchIndex};",
                cancellationToken).ConfigureAwait(false);
            await ExecuteOutsideTransactionAsync(
                connection,
                $"""
                CREATE INDEX CONCURRENTLY {TaskSearchIndex}
                ON public.archived_tasks USING gin ((data::text) public.gin_trgm_ops);
                """,
                cancellationToken).ConfigureAwait(false);
        }

        if (!await IsTaskSearchIndexValidAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"PostgreSQL [tasks] search index public.{TaskSearchIndex} is invalid.");
        }

        await PublishVersionAsync(connection, TasksSchemaVersion, cancellationToken).ConfigureAwait(false);
        LogMigrationCompleted(_logger, "tasks", TasksSchemaVersion);
    }

    private static async Task EnsureTrigramExtensionAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteOutsideTransactionAsync(
            connection,
            "CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteOutsideTransactionAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PublishVersionAsync(
        NpgsqlConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await SetSchemaVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsTraceSearchIndexValidAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class AS index_relation
                INNER JOIN pg_catalog.pg_namespace AS index_namespace
                    ON index_namespace.oid = index_relation.relnamespace
                INNER JOIN pg_catalog.pg_index AS index_metadata
                    ON index_metadata.indexrelid = index_relation.oid
                INNER JOIN pg_catalog.pg_class AS table_relation
                    ON table_relation.oid = index_metadata.indrelid
                INNER JOIN pg_catalog.pg_namespace AS table_namespace
                    ON table_namespace.oid = table_relation.relnamespace
                INNER JOIN pg_catalog.pg_am AS access_method
                    ON access_method.oid = index_relation.relam
                INNER JOIN pg_catalog.pg_opclass AS operator_class
                    ON operator_class.oid = index_metadata.indclass[0]
                INNER JOIN pg_catalog.pg_namespace AS operator_namespace
                    ON operator_namespace.oid = operator_class.opcnamespace
                WHERE index_namespace.nspname = 'public'
                  AND index_relation.relname = 'idx_command_traces_clean_text_trgm'
                  AND table_namespace.nspname = 'public'
                  AND table_relation.relname = 'command_traces'
                  AND index_metadata.indisvalid
                  AND index_metadata.indisready
                  AND NOT index_metadata.indisunique
                  AND index_metadata.indnatts = 1
                  AND index_metadata.indnkeyatts = 1
                  AND index_metadata.indpred IS NULL
                  AND index_metadata.indexprs IS NULL
                  AND access_method.amname = 'gin'
                  AND operator_namespace.nspname = 'public'
                  AND operator_class.opcname = 'gin_trgm_ops'
                  AND index_metadata.indkey[0] = (
                      SELECT attribute.attnum
                      FROM pg_catalog.pg_attribute AS attribute
                      WHERE attribute.attrelid = table_relation.oid
                        AND attribute.attname = 'clean_text'
                        AND NOT attribute.attisdropped
                  )
            );
            """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<bool> IsTaskSearchIndexValidAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class AS index_relation
                INNER JOIN pg_catalog.pg_namespace AS index_namespace
                    ON index_namespace.oid = index_relation.relnamespace
                INNER JOIN pg_catalog.pg_index AS index_metadata
                    ON index_metadata.indexrelid = index_relation.oid
                INNER JOIN pg_catalog.pg_class AS table_relation
                    ON table_relation.oid = index_metadata.indrelid
                INNER JOIN pg_catalog.pg_namespace AS table_namespace
                    ON table_namespace.oid = table_relation.relnamespace
                INNER JOIN pg_catalog.pg_am AS access_method
                    ON access_method.oid = index_relation.relam
                INNER JOIN pg_catalog.pg_opclass AS operator_class
                    ON operator_class.oid = index_metadata.indclass[0]
                INNER JOIN pg_catalog.pg_namespace AS operator_namespace
                    ON operator_namespace.oid = operator_class.opcnamespace
                WHERE index_namespace.nspname = 'public'
                  AND index_relation.relname = 'idx_archived_tasks_data_trgm'
                  AND table_namespace.nspname = 'public'
                  AND table_relation.relname = 'archived_tasks'
                  AND index_metadata.indisvalid
                  AND index_metadata.indisready
                  AND NOT index_metadata.indisunique
                  AND index_metadata.indnatts = 1
                  AND index_metadata.indnkeyatts = 1
                  AND index_metadata.indpred IS NULL
                  AND index_metadata.indexprs IS NOT NULL
                  AND index_metadata.indkey[0] = 0
                  AND access_method.amname = 'gin'
                  AND operator_namespace.nspname = 'public'
                  AND operator_class.opcname = 'gin_trgm_ops'
                  AND pg_catalog.pg_get_expr(index_metadata.indexprs, index_metadata.indrelid, false) = '(data)::text'
            );
            """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    // ── luciaconfig database migrations ──────────────────────────────────────

    private static async Task ApplyConfigV1Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive BOOLEAN NOT NULL DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS public.api_keys (
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
            CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON public.api_keys(key_hash);
            CREATE INDEX IF NOT EXISTS idx_api_keys_revoked ON public.api_keys(is_revoked);

            CREATE TABLE IF NOT EXISTS public.model_providers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.agent_definitions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.mcp_tool_servers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.response_templates (
                id TEXT PRIMARY KEY,
                skill_id TEXT NOT NULL,
                action TEXT NOT NULL,
                data JSONB NOT NULL,
                UNIQUE(skill_id, action)
            );

            CREATE TABLE IF NOT EXISTS public.presence_sensor_mappings (
                id TEXT PRIMARY KEY,
                area_id TEXT,
                is_user_override BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_presence_area ON public.presence_sensor_mappings(area_id);

            CREATE TABLE IF NOT EXISTS public.presence_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.plugin_repositories (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.installed_plugins (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.voice_transcripts (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TIMESTAMPTZ NOT NULL,
                speaker_id TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_transcripts_session ON public.voice_transcripts(session_id);
            CREATE INDEX IF NOT EXISTS idx_transcripts_time ON public.voice_transcripts(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_transcripts_speaker ON public.voice_transcripts(speaker_id);

            CREATE TABLE IF NOT EXISTS public.speaker_profiles (
                id TEXT PRIMARY KEY,
                is_provisional BOOLEAN NOT NULL DEFAULT TRUE,
                last_seen_at TIMESTAMPTZ,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_profiles_provisional ON public.speaker_profiles(is_provisional, last_seen_at);

            CREATE TABLE IF NOT EXISTS public.model_preferences (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyConfigV2Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.user_memories (
                user_id TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ,
                PRIMARY KEY (user_id, key)
            );
            CREATE INDEX IF NOT EXISTS idx_user_memories_user_id ON public.user_memories(user_id);
            CREATE INDEX IF NOT EXISTS idx_user_memories_expires_at ON public.user_memories(expires_at);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── luciatraces database migrations ──────────────────────────────────────

    private static async Task ApplyTracesV1Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.conversation_traces (
                id TEXT PRIMARY KEY,
                session_id TEXT,
                timestamp TIMESTAMPTZ NOT NULL,
                user_input TEXT,
                label_status TEXT,
                is_errored BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON public.conversation_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_traces_session ON public.conversation_traces(session_id);
            CREATE INDEX IF NOT EXISTS idx_traces_label ON public.conversation_traces(label_status);

            CREATE TABLE IF NOT EXISTS public.dataset_exports (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.command_traces (
                id TEXT PRIMARY KEY,
                timestamp TIMESTAMPTZ NOT NULL,
                clean_text TEXT NOT NULL,
                outcome TEXT NOT NULL,
                skill_id TEXT,
                confidence DOUBLE PRECISION NOT NULL DEFAULT 0,
                total_duration_ms DOUBLE PRECISION NOT NULL DEFAULT 0,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_command_traces_timestamp ON public.command_traces(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_command_traces_outcome ON public.command_traces(outcome);
            CREATE INDEX IF NOT EXISTS idx_command_traces_skill ON public.command_traces(skill_id);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── luciatasks database migrations ───────────────────────────────────────

    private static async Task ApplyTasksV1Async(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.scheduled_tasks (
                id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                fire_at TIMESTAMPTZ,
                task_type TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_scheduled_status ON public.scheduled_tasks(status, fire_at);

            CREATE TABLE IF NOT EXISTS public.alarm_clocks (
                id TEXT PRIMARY KEY,
                is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                next_fire_at TIMESTAMPTZ,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_alarms_enabled ON public.alarm_clocks(is_enabled, next_fire_at);

            CREATE TABLE IF NOT EXISTS public.alarm_sounds (
                id TEXT PRIMARY KEY,
                is_default BOOLEAN NOT NULL DEFAULT FALSE,
                data JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.archived_tasks (
                id TEXT PRIMARY KEY,
                archived_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                final_state TEXT,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_archived_at ON public.archived_tasks(archived_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
