using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Tests.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class GeneralAgentTests
{
    [Fact]
    public async Task RefreshConfigAsync_WhenTwoCallsOverlapOnFirstApply_BuildsAgentOnlyOnce()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;

        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(async call =>
            {
                Interlocked.Increment(ref buildCount);
                buildStarted.TrySetResult();
                await allowBuildToComplete.Task;
                return (AIAgent?)A.Fake<AIAgent>();
            });

        var definition = CreateDefinition("model-x", "instructions", DateTime.UnixEpoch.AddDays(1));
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("general-assistant", A<CancellationToken>._))
            .Returns(Task.FromResult<AgentDefinition?>(definition));

        var agent = CreateAgent(resolver, repository);

        var first = agent.RefreshConfigAsync();
        await buildStarted.Task;
        // Second call arrives while first is in-flight inside the semaphore.
        var second = agent.RefreshConfigAsync();
        allowBuildToComplete.SetResult();
        await Task.WhenAll(first, second);

        // The reload gate serializes both calls; the second sees _aiAgent already set by the
        // first and skips the expensive build entirely.
        Assert.Equal(1, buildCount);
    }

    [Fact]
    public async Task RefreshConfigAsync_WhenDefinitionChangesDuringApply_AppliesNewDefinitionNextCycle()
    {
        using var database = new AgentDefinitionSqliteTestDatabase();
        var repository = database.Repository;
        var firstDefinition = CreateDefinition("model-x", "instructions-x", DateTime.UnixEpoch);
        await repository.UpsertAgentDefinitionAsync(firstDefinition);
        var firstApplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedDefinitions = new List<(string? ModelConnectionName, string Instructions)>();

        GeneralAgent? agent = null;
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(async call =>
            {
                appliedDefinitions.Add((call.GetArgument<string?>(0), agent!.Instructions));
                if (appliedDefinitions.Count == 1)
                {
                    firstApplyStarted.SetResult();
                    await finishFirstApply.Task;
                }

                return (AIAgent?)A.Fake<AIAgent>();
            });

        agent = new GeneralAgent(
            resolver,
            repository,
            A.Fake<IMcpToolRegistry>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);

        var initialization = agent.InitializeAsync();
        await firstApplyStarted.Task;
        var nextDefinition = CreateDefinition("model-y", "instructions-y", DateTime.UnixEpoch);
        await repository.UpsertAgentDefinitionAsync(nextDefinition);
        finishFirstApply.SetResult();
        await initialization;

        var persistedWinner = await repository.GetAgentDefinitionAsync("general-assistant");
        await agent.RefreshConfigAsync();

        Assert.NotNull(persistedWinner);
        Assert.Equal("model-y", persistedWinner.ModelConnectionName);
        Assert.True(persistedWinner.UpdatedAt > firstDefinition.UpdatedAt);
        Assert.Equal(nextDefinition.UpdatedAt, persistedWinner.UpdatedAt);
        Assert.Equal(
            [("model-x", "instructions-x"), ("model-y", "instructions-y")],
            appliedDefinitions);
    }

    [Fact]
    public async Task RefreshConfigAsync_WhenDefinitionIsAbsent_DoesNotRebuildContinuously()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("general-assistant", A<CancellationToken>._))
            .Returns(Task.FromResult<AgentDefinition?>(null));

        var appliedConnections = new List<string?>();
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                appliedConnections.Add(call.GetArgument<string?>(0));
                return Task.FromResult<AIAgent?>(A.Fake<AIAgent>());
            });

        var agent = CreateAgent(resolver, repository);

        await agent.InitializeAsync();
        await agent.RefreshConfigAsync();

        Assert.Equal([null], appliedConnections);
    }

    [Fact]
    public async Task RefreshConfigAsync_WhenDefinitionIsCreatedDuringAbsentApply_AppliesCreatedDefinitionNextCycle()
    {
        using var database = new AgentDefinitionSqliteTestDatabase();
        var repository = database.Repository;
        var firstApplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedConnections = new List<string?>();

        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(async call =>
            {
                appliedConnections.Add(call.GetArgument<string?>(0));
                if (appliedConnections.Count == 1)
                {
                    firstApplyStarted.SetResult();
                    await finishFirstApply.Task;
                }

                return (AIAgent?)A.Fake<AIAgent>();
            });

        var agent = CreateAgent(resolver, repository);

        var initialization = agent.InitializeAsync();
        await firstApplyStarted.Task;
        var nextDefinition = CreateDefinition("model-y", "instructions-y", DateTime.UnixEpoch);
        await repository.UpsertAgentDefinitionAsync(nextDefinition);
        finishFirstApply.SetResult();
        await initialization;

        var absenceWatermark = (DateTime?)typeof(GeneralAgent)
            .GetField("_lastConfigUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(agent) ?? DateTime.UtcNow;
        using (var connection = database.ConnectionFactory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE agent_definitions
                SET data = json_set(data, '$.updatedAt', @updatedAt)
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@updatedAt", absenceWatermark.ToString("O"));
            command.Parameters.AddWithValue("@id", nextDefinition.Id);
            await command.ExecuteNonQueryAsync();
        }

        var persistedWinner = await repository.GetAgentDefinitionAsync("general-assistant");
        await agent.RefreshConfigAsync();

        Assert.NotNull(persistedWinner);
        Assert.Equal(absenceWatermark, persistedWinner.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, persistedWinner.UpdatedAt.Kind);
        Assert.Equal([null, "model-y"], appliedConnections);
    }

    private static GeneralAgent CreateAgent(
        IChatClientResolver resolver,
        IAgentDefinitionRepository repository)
    {
        return new GeneralAgent(
            resolver,
            repository,
            A.Fake<IMcpToolRegistry>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);
    }

    private static AgentDefinition CreateDefinition(
        string modelConnectionName,
        string instructions,
        DateTime updatedAt)
    {
        return new AgentDefinition
        {
            Id = "general-assistant",
            Name = "general-assistant",
            DisplayName = "General Assistant",
            Description = "General assistant",
            Instructions = instructions,
            ModelConnectionName = modelConnectionName,
            UpdatedAt = updatedAt,
        };
    }
}
