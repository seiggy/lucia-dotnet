using System.Text.Json;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace lucia.Tests.Data;

public sealed class PostgresMigrationRunnerTests(PostgresMigrationFixture fixture)
    : IClassFixture<PostgresMigrationFixture>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task FreshDatabases_CreateVersionedTrigramIndexes()
    {
        await using var databases = await fixture.CreateDatabasesAsync();

        await CreateRunner(databases).StartAsync(CancellationToken.None);

        Assert.Equal(2, await VersionAsync(databases.Config));
        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(2, await VersionAsync(databases.Tasks));
        Assert.True(await IndexIsValidAsync(databases.Traces, "idx_command_traces_clean_text_trgm"));
        Assert.True(await IndexIsValidAsync(databases.Tasks, "idx_archived_tasks_data_trgm"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VersionOneDatabases_UpgradeToVersionTwo()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await SetVersionAsync(databases.Traces, 1);
        await SetVersionAsync(databases.Tasks, 1);
        await DropIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm");
        await DropIndexAsync(databases.Tasks, "idx_archived_tasks_data_trgm");

        await CreateRunner(databases).StartAsync(CancellationToken.None);

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(2, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CurrentDatabases_AreIdempotent()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var runner = CreateRunner(databases);
        await runner.StartAsync(CancellationToken.None);
        var traceOid = await IndexOidAsync(databases.Traces, "idx_command_traces_clean_text_trgm");
        var taskOid = await IndexOidAsync(databases.Tasks, "idx_archived_tasks_data_trgm");

        await runner.StartAsync(CancellationToken.None);

        Assert.Equal(traceOid, await IndexOidAsync(databases.Traces, "idx_command_traces_clean_text_trgm"));
        Assert.Equal(taskOid, await IndexOidAsync(databases.Tasks, "idx_archived_tasks_data_trgm"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentRunners_SerializeWholeMigration()
    {
        await using var databases = await fixture.CreateDatabasesAsync();

        await Task.WhenAll(
            CreateRunner(databases).StartAsync(CancellationToken.None),
            CreateRunner(databases).StartAsync(CancellationToken.None));

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(2, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MissingCreatePrivilege_DemotesBeforeIndexRepairFails()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await BreakIndexAsync(databases.Tasks, "idx_archived_tasks_data_trgm", "archived_at");
        var limitedTasks = await CreateLimitedFactoryAsync(databases.Tasks);
        await using (limitedTasks)
        {
            var runner = CreateRunner(databases.Config, databases.Traces, limitedTasks);

            await Assert.ThrowsAnyAsync<PostgresException>(
                () => runner.StartAsync(CancellationToken.None));
        }

        Assert.Equal(1, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FailedRepair_RestartCompletesFromDemotedVersion()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await BreakIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm", "timestamp");
        var limitedTraces = await CreateLimitedFactoryAsync(databases.Traces);
        await using (limitedTraces)
        {
            await Assert.ThrowsAnyAsync<PostgresException>(
                () => CreateRunner(databases.Config, limitedTraces, databases.Tasks)
                    .StartAsync(CancellationToken.None));
        }
        Assert.Equal(1, await VersionAsync(databases.Traces));

        await CreateRunner(databases).StartAsync(CancellationToken.None);

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.True(await IndexIsValidAsync(databases.Traces, "idx_command_traces_clean_text_trgm"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PartialRepair_RestartCompletesRemainingSubsystem()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await BreakIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm", "timestamp");
        await BreakIndexAsync(databases.Tasks, "idx_archived_tasks_data_trgm", "archived_at");
        var limitedTasks = await CreateLimitedFactoryAsync(databases.Tasks);
        await using (limitedTasks)
        {
            await Assert.ThrowsAnyAsync<PostgresException>(
                () => CreateRunner(databases.Config, databases.Traces, limitedTasks)
                    .StartAsync(CancellationToken.None));
        }

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(1, await VersionAsync(databases.Tasks));

        await CreateRunner(databases).StartAsync(CancellationToken.None);
        Assert.Equal(2, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FutureVersion_FailsWithoutMutation()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await SetVersionAsync(databases.Tasks, 3);
        await BreakIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm", "timestamp");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRunner(databases).StartAsync(CancellationToken.None));

        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(3, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WrongIndexDefinition_IsRebuiltExactly()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await BreakIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm", "timestamp");

        await CreateRunner(databases).StartAsync(CancellationToken.None);

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Contains(
            "USING gin (clean_text gin_trgm_ops)",
            await IndexDefinitionAsync(databases.Traces, "idx_command_traces_clean_text_trgm"),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InternalLockDeadline_ThrowsTimeoutException()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await using var blocker = await databases.Config.CreateConnectionAsync();
        await AdvisoryLockAsync(blocker);

        await Assert.ThrowsAsync<TimeoutException>(
            () => CreateRunner(databases, TimeSpan.FromMilliseconds(200))
                .StartAsync(CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CallerCancellation_RemainsOperationCanceledException()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await using var blocker = await databases.Config.CreateConnectionAsync();
        await AdvisoryLockAsync(blocker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var exception = await Record.ExceptionAsync(
            () => CreateRunner(databases, TimeSpan.FromSeconds(10)).StartAsync(cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.IsNotType<TimeoutException>(exception);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommandSearch_UsesProductionTrigramIndexNaturally()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await SeedCommandTracesAsync(databases.Traces);

        var plan = await ExplainAsync(
            databases.Traces,
            "SELECT * FROM public.command_traces WHERE clean_text ILIKE '%' || @search || '%';",
            "needle-129");

        Assert.True(PlanUsesIndex(plan, "idx_command_traces_clean_text_trgm"), plan.RootElement.ToString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ArchiveSearch_UsesProductionTrigramIndexNaturally()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await SeedArchivedTasksAsync(databases.Tasks);

        var plan = await ExplainAsync(
            databases.Tasks,
            "SELECT * FROM public.archived_tasks WHERE data::text ILIKE '%' || @search || '%';",
            "needle-129");

        Assert.True(PlanUsesIndex(plan, "idx_archived_tasks_data_trgm"), plan.RootElement.ToString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BrokenPublishedSubsystems_AreAllDemotedBeforeEarlyConfigDdlFailure()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await BreakIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm", "timestamp");
        await BreakIndexAsync(databases.Tasks, "idx_archived_tasks_data_trgm", "archived_at");
        await SetVersionAsync(databases.Config, 0);
        await ExecuteAsync(
            databases.Config,
            """
            CREATE FUNCTION public.block_config_ddl() RETURNS event_trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'blocked config DDL';
            END;
            $$;
            CREATE EVENT TRIGGER block_config_ddl
            ON ddl_command_start WHEN TAG IN ('CREATE TABLE')
            EXECUTE FUNCTION public.block_config_ddl();
            """);

        await Assert.ThrowsAnyAsync<PostgresException>(
            () => CreateRunner(databases).StartAsync(CancellationToken.None));

        Assert.Equal(1, await VersionAsync(databases.Traces));
        Assert.Equal(1, await VersionAsync(databases.Tasks));

        await ExecuteAsync(
            databases.Config,
            "DROP EVENT TRIGGER block_config_ddl; DROP FUNCTION public.block_config_ddl();");
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(2, await VersionAsync(databases.Tasks));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SearchPathShadow_DoesNotRedirectVersionReadsOrWrites()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await CreateRunner(databases).StartAsync(CancellationToken.None);
        await ExecuteAsync(
            databases.Traces,
            """
            CREATE SCHEMA evil;
            CREATE TABLE evil.schema_version (id integer PRIMARY KEY, version integer NOT NULL, applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            INSERT INTO evil.schema_version (id, version) VALUES (1, 77);
            """);
        await SetVersionAsync(databases.Traces, 1);
        await DropIndexAsync(databases.Traces, "idx_command_traces_clean_text_trgm");
        var shadowedTraces = WithSearchPath(databases.Traces, "evil,public");
        await using (shadowedTraces)
        {
            await CreateRunner(databases.Config, shadowedTraces, databases.Tasks)
                .StartAsync(CancellationToken.None);
        }

        Assert.Equal(2, await VersionAsync(databases.Traces));
        Assert.Equal(77, await ScalarAsync<int>(databases.Traces, "SELECT version FROM evil.schema_version WHERE id = 1;"));
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("CREATE VIEW public.schema_version AS SELECT 1::integer AS id, 0::integer AS version, CURRENT_TIMESTAMP AS applied_at;")]
    [InlineData("CREATE TABLE public.schema_version (id text PRIMARY KEY, version integer NOT NULL, applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);")]
    [InlineData("CREATE TABLE public.schema_version (id integer PRIMARY KEY, version bigint NOT NULL, applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);")]
    public async Task MalformedPublicVersionRelation_FailsBeforeMigrationDdl(string malformedDdl)
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        await ExecuteAsync(databases.Config, malformedDdl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRunner(databases).StartAsync(CancellationToken.None));

        Assert.Contains("public.schema_version", exception.Message, StringComparison.Ordinal);
        Assert.False(await RelationExistsAsync(databases.Config, "public.configuration"));
        Assert.False(await RelationExistsAsync(databases.Traces, "public.schema_version"));
        Assert.False(await RelationExistsAsync(databases.Tasks, "public.schema_version"));
    }

    private static PostgresMigrationRunner CreateRunner(
        PostgresTestDatabases databases,
        TimeSpan? lockTimeout = null)
        => CreateRunner(databases.Config, databases.Traces, databases.Tasks, lockTimeout);

    private static PostgresMigrationRunner CreateRunner(
        PostgresConnectionFactory config,
        PostgresConnectionFactory traces,
        PostgresConnectionFactory tasks,
        TimeSpan? lockTimeout = null)
        => new(config, traces, tasks, NullLogger<PostgresMigrationRunner>.Instance, lockTimeout ?? TimeSpan.FromSeconds(10));

    private static async Task<int> VersionAsync(PostgresConnectionFactory factory)
        => await ScalarAsync<int>(factory, "SELECT version FROM public.schema_version WHERE id = 1;");

    private static Task SetVersionAsync(PostgresConnectionFactory factory, int version)
        => ExecuteAsync(factory, $"UPDATE public.schema_version SET version = {version} WHERE id = 1;");

    private static Task DropIndexAsync(PostgresConnectionFactory factory, string indexName)
        => ExecuteAsync(factory, $"DROP INDEX public.{indexName};");

    private static async Task BreakIndexAsync(PostgresConnectionFactory factory, string indexName, string column)
    {
        await DropIndexAsync(factory, indexName);
        await ExecuteAsync(factory, $"CREATE INDEX {indexName} ON public.{(indexName.Contains("command", StringComparison.Ordinal) ? "command_traces" : "archived_tasks")} ({column});");
    }

    private static Task<bool> IndexIsValidAsync(PostgresConnectionFactory factory, string indexName)
        => ScalarAsync<bool>(
            factory,
            $"SELECT indisvalid AND indisready FROM pg_catalog.pg_index WHERE indexrelid = 'public.{indexName}'::regclass;");

    private static Task<uint> IndexOidAsync(PostgresConnectionFactory factory, string indexName)
        => ScalarAsync<uint>(factory, $"SELECT 'public.{indexName}'::regclass::oid;");

    private static Task<string> IndexDefinitionAsync(PostgresConnectionFactory factory, string indexName)
        => ScalarAsync<string>(factory, $"SELECT pg_catalog.pg_get_indexdef('public.{indexName}'::regclass);");

    private static async Task AdvisoryLockAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_advisory_lock(@key);";
        command.Parameters.AddWithValue("key", PostgresMigrationRunner.AdvisoryLockKey);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresConnectionFactory> CreateLimitedFactoryAsync(PostgresConnectionFactory factory)
    {
        var role = $"limited_{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        var factoryBuilder = new NpgsqlConnectionStringBuilder(factory.ConnectionString);
        await ExecuteAsync(
            factory,
            $"""
            CREATE ROLE {role} LOGIN PASSWORD '{password}';
            GRANT CONNECT ON DATABASE "{factoryBuilder.Database}" TO {role};
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT SELECT, UPDATE ON public.schema_version TO {role};
            """);
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            Username = role,
            Password = password,
            Pooling = false,
        };
        return new(builder.ConnectionString);
    }

    private static PostgresConnectionFactory WithSearchPath(PostgresConnectionFactory factory, string searchPath)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            SearchPath = searchPath,
            Pooling = false,
        };
        return new(builder.ConnectionString);
    }

    private static async Task SeedCommandTracesAsync(PostgresConnectionFactory factory)
    {
        await ExecuteAsync(
            factory,
            """
            INSERT INTO public.command_traces (id, timestamp, clean_text, outcome, data)
            SELECT i::text, CURRENT_TIMESTAMP, CASE WHEN i = 200000 THEN 'needle-129' ELSE md5(i::text) END, 'ok', '{}'::jsonb
            FROM generate_series(1, 200000) AS i;
            ANALYZE public.command_traces;
            """);
    }

    private static async Task SeedArchivedTasksAsync(PostgresConnectionFactory factory)
    {
        await ExecuteAsync(
            factory,
            """
            INSERT INTO public.archived_tasks (id, data)
            SELECT i::text, jsonb_build_object('value', CASE WHEN i = 200000 THEN 'needle-129' ELSE md5(i::text) END)
            FROM generate_series(1, 200000) AS i;
            ANALYZE public.archived_tasks;
            """);
    }

    private static async Task<JsonDocument> ExplainAsync(
        PostgresConnectionFactory factory,
        string query,
        string search)
    {
        await using var connection = await factory.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN (FORMAT JSON) {query}";
        command.Parameters.AddWithValue("search", search);
        var json = (string)(await command.ExecuteScalarAsync())!;
        return JsonDocument.Parse(json);
    }

    private static bool PlanUsesIndex(JsonDocument plan, string indexName)
    {
        return FindIndex(plan.RootElement);

        bool FindIndex(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Index Name", out var name) && name.GetString() == indexName)
                {
                    return true;
                }

                return element.EnumerateObject().Any(property => FindIndex(property.Value));
            }

            return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(FindIndex);
        }
    }

    private static async Task<bool> RelationExistsAsync(PostgresConnectionFactory factory, string relation)
        => await ScalarAsync<bool>(factory, $"SELECT pg_catalog.to_regclass('{relation}') IS NOT NULL;");

    private static async Task<T> ScalarAsync<T>(PostgresConnectionFactory factory, string sql)
    {
        await using var connection = await factory.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(PostgresConnectionFactory factory, string sql)
    {
        await using var connection = await factory.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
