using DotNet.Testcontainers.Builders;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Data.PostgreSQL;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace lucia.Tests.Data;

public sealed class PostgresAgentDefinitionRepositoryTests
{
    private const ushort PostgresPort = 5432;

    [Fact, Trait("Category", "Integration")]
    public async Task UpsertAgentDefinitionAsync_DelayedWriterAdvancesMicrosecondMarkerPastCommittedWinner()
    {
        await using var container = new ContainerBuilder("postgres:17-alpine")
            .WithEnvironment("POSTGRES_DB", "luciaconfig")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithPortBinding(PostgresPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted("pg_isready", "-U", "postgres", "-d", "luciaconfig"))
            .Build();
        await container.StartAsync();

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(PostgresPort),
            Database = "luciaconfig",
            Username = "postgres",
            Password = "postgres",
            ApplicationName = "agent-definition-repository-test",
            SslMode = SslMode.Disable,
        }.ConnectionString;

        await using var connectionFactory = new PostgresConnectionFactory(connectionString);
        await using (var connection = await connectionFactory.CreateConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE agent_definitions (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    data JSONB NOT NULL
                );

                CREATE FUNCTION block_delayed_writer() RETURNS trigger AS $$
                BEGIN
                    IF NEW.data->>'modelConnectionName' = 'writer-a' THEN
                        PERFORM pg_advisory_xact_lock(225);
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER delay_writer_a
                BEFORE INSERT ON agent_definitions
                FOR EACH ROW EXECUTE FUNCTION block_delayed_writer();
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new PostgresAgentDefinitionRepository(connectionFactory);
        var initial = CreateDefinition("initial");
        await repository.UpsertAgentDefinitionAsync(initial);

        await using var blocker = await connectionFactory.CreateConnectionAsync();
        await using var observer = await connectionFactory.CreateConnectionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.CommandText = "SELECT pg_advisory_lock(225);";
            await command.ExecuteNonQueryAsync();
        }

        var delayedWriter = CreateDefinition("writer-a");
        var committedWriter = CreateDefinition("writer-b");
        var delayedWrite = repository.UpsertAgentDefinitionAsync(delayedWriter);
        using var notificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await WaitForDelayedWriterAsync(observer, notificationTimeout.Token);

        await repository.UpsertAgentDefinitionAsync(committedWriter);

        await using (var command = blocker.CreateCommand())
        {
            command.CommandText = "SELECT pg_advisory_unlock(225);";
            await command.ExecuteNonQueryAsync();
        }
        await delayedWrite;

        var persistedWinner = await repository.GetAgentDefinitionAsync(initial.Id);
        Assert.NotNull(persistedWinner);
        Assert.Equal("writer-a", persistedWinner.ModelConnectionName);
        Assert.True(delayedWriter.UpdatedAt > committedWriter.UpdatedAt);
        Assert.Equal(delayedWriter.UpdatedAt, persistedWinner.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, persistedWinner.UpdatedAt.Kind);
        Assert.Equal(0, persistedWinner.UpdatedAt.Ticks % 10);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UpsertAgentDefinitionAsync_ConflictingInsertAborts_AssignsMarkerAfterWaitAndBecomesVisible()
    {
        await using var container = new ContainerBuilder("postgres:17-alpine")
            .WithEnvironment("POSTGRES_DB", "luciaconfig")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithPortBinding(PostgresPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted("pg_isready", "-U", "postgres", "-d", "luciaconfig"))
            .Build();
        await container.StartAsync();

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(PostgresPort),
            Database = "luciaconfig",
            Username = "postgres",
            Password = "postgres",
            ApplicationName = "agent-definition-repository-test",
            SslMode = SslMode.Disable,
        }.ConnectionString;

        await using var connectionFactory = new PostgresConnectionFactory(connectionString);
        await using var blocker = await connectionFactory.CreateConnectionAsync();
        await using var observer = await connectionFactory.CreateConnectionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE agent_definitions (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    data JSONB NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = blockerTransaction;
            command.CommandText = """
                INSERT INTO agent_definitions (id, name, enabled, data)
                VALUES (
                    'general-assistant',
                    'general-assistant',
                    TRUE,
                    '{"id":"general-assistant","name":"general-assistant","displayName":"General Assistant","description":"General assistant","instructions":"blocked","modelConnectionName":"blocked","enabled":true,"updatedAt":"1970-01-01T00:00:00Z"}');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new PostgresAgentDefinitionRepository(connectionFactory);
        var survivor = CreateDefinition("survivor");
        var survivingWrite = repository.UpsertAgentDefinitionAsync(survivor);
        using var notificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await WaitForConflictingInsertAsync(observer, notificationTimeout.Token);

        var appliedConnections = new List<string?>();
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                appliedConnections.Add(call.GetArgument<string?>(0));
                return Task.FromResult<AIAgent?>(A.Fake<AIAgent>());
            });
        var agent = new GeneralAgent(
            resolver,
            repository,
            A.Fake<IMcpToolRegistry>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);
        await agent.InitializeAsync();

        DateTime markerMustFollow;
        await using (var command = observer.CreateCommand())
        {
            command.CommandText = "SELECT clock_timestamp();";
            markerMustFollow = Assert.IsType<DateTime>(await command.ExecuteScalarAsync()).ToUniversalTime();
        }

        await blockerTransaction.RollbackAsync();
        await survivingWrite;
        await agent.RefreshConfigAsync();

        var persistedSurvivor = await repository.GetAgentDefinitionAsync(survivor.Id);
        Assert.NotNull(persistedSurvivor);
        Assert.Equal("survivor", persistedSurvivor.ModelConnectionName);
        Assert.True(persistedSurvivor.UpdatedAt > markerMustFollow);
        Assert.Equal(survivor.UpdatedAt, persistedSurvivor.UpdatedAt);
        Assert.Equal([null, "survivor"], appliedConnections);
    }

    private static async Task WaitForDelayedWriterAsync(
        NpgsqlConnection observer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE application_name = 'agent-definition-repository-test'
                      AND wait_event = 'advisory'
                );
                """;
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return;
            }

            await Task.Yield();
        }
    }

    private static async Task WaitForConflictingInsertAsync(
        NpgsqlConnection observer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE application_name = 'agent-definition-repository-test'
                      AND state = 'active'
                      AND wait_event_type = 'Lock'
                      AND query LIKE 'INSERT INTO agent_definitions%'
                );
                """;
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return;
            }

            await Task.Yield();
        }
    }

    private static AgentDefinition CreateDefinition(string modelConnectionName)
    {
        return new AgentDefinition
        {
            Id = "general-assistant",
            Name = "general-assistant",
            DisplayName = "General Assistant",
            Description = "General assistant",
            Instructions = "Use tools",
            ModelConnectionName = modelConnectionName,
        };
    }
}
