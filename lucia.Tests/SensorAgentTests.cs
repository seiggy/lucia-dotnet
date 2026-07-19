using A2A;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.Agents.Training;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class SensorAgentTests
{
    [Fact]
    public async Task RefreshConfigAsync_WhenEmbeddingUpdateFails_RetriesEntireDefinition()
    {
        var embeddingResolver = A.Fake<IEmbeddingProviderResolver>();
        var newProviderAttempts = 0;
        A.CallTo(() => embeddingResolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                if (call.GetArgument<string?>(0) == "new-provider" && newProviderAttempts++ == 0)
                {
                    return Task.FromException<IEmbeddingGenerator<string, Embedding<float>>?>(
                        new InvalidOperationException("Embedding provider unavailable."));
                }

                return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null);
            });

        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>([]));
        var skill = new SensorControlSkill(
            new FakeHomeAssistantClient(),
            embeddingResolver,
            NullLogger<SensorControlSkill>.Instance,
            deviceCache,
            A.Fake<IEntityLocationService>(),
            A.Fake<IHybridEntityMatcher>(),
            new TestOptionsMonitor<SensorControlSkillOptions>(new SensorControlSkillOptions()));

        var oldDefinition = CreateDefinition(null, DateTime.UtcNow, "old-model");
        var newDefinition = CreateDefinition("new-provider", oldDefinition.UpdatedAt.AddMinutes(1), "new-model");
        var currentDefinition = oldDefinition;
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("sensor-agent", A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<AgentDefinition?>(currentDefinition));

        var appliedModels = new List<string?>();
        var chatClientResolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => chatClientResolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                appliedModels.Add(call.GetArgument<string?>(0));
                return Task.FromResult<AIAgent?>(A.Fake<AIAgent>());
            });

        var agent = new SensorAgent(
            chatClientResolver,
            repository,
            skill,
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);

        await agent.InitializeAsync();
        currentDefinition = newDefinition;
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RefreshConfigAsync());
        await agent.RefreshConfigAsync();

        Assert.Equal(["old-model", "new-model", "new-model"], appliedModels);
    }

    [Fact]
    public async Task RefreshConfigAsync_WhenTwoCallsOverlapOnEmbeddingUpdate_EmbeddingUpdatedOnlyOnce()
    {
        var embeddingUpdateCount = 0;
        var firstUpdateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstUpdateToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var embeddingResolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => embeddingResolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(async call =>
            {
                Interlocked.Increment(ref embeddingUpdateCount);
                firstUpdateStarted.TrySetResult();
                await allowFirstUpdateToComplete.Task;
                return (IEmbeddingGenerator<string, Embedding<float>>?)null;
            });

        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>([]));

        var skill = new SensorControlSkill(
            new FakeHomeAssistantClient(),
            embeddingResolver,
            NullLogger<SensorControlSkill>.Instance,
            deviceCache,
            A.Fake<IEntityLocationService>(),
            A.Fake<IHybridEntityMatcher>(),
            new TestOptionsMonitor<SensorControlSkillOptions>(new SensorControlSkillOptions()));

        var definition = CreateDefinition("new-provider", DateTime.UtcNow.AddMinutes(1));
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("sensor-agent", A<CancellationToken>._))
            .Returns(Task.FromResult<AgentDefinition?>(definition));

        var chatClientResolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => chatClientResolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(A.Fake<AIAgent>()));

        var agent = new SensorAgent(
            chatClientResolver,
            repository,
            skill,
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);

        var first = agent.RefreshConfigAsync();
        await firstUpdateStarted.Task;
        var second = agent.RefreshConfigAsync();
        allowFirstUpdateToComplete.SetResult();
        await Task.WhenAll(first, second);

        // The reload gate serializes both calls; the second sees _lastEmbeddingProviderName
        // already set by the first and skips the embedding update entirely.
        Assert.Equal(1, embeddingUpdateCount);
    }

    [Fact]
    public async Task RefreshConfigAsync_WhenEmbeddingProviderChanges_RebuildsSensorEmbeddings()
    {
        var homeAssistantClient = new FakeHomeAssistantClient();
        await homeAssistantClient.SetEntityStateAsync(
            "sensor.kitchen_temperature",
            "72",
            new Dictionary<string, object>
            {
                ["friendly_name"] = "Kitchen Temperature",
                ["unit_of_measurement"] = "\u00B0F",
            });

        var oldEmbedding = new Embedding<float>(new float[] { 1f });
        var newEmbedding = new Embedding<float>(new float[] { 2f });
        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(
            [
                new SensorEntity
                {
                    EntityId = "sensor.kitchen_temperature",
                    FriendlyName = "Kitchen Temperature",
                },
            ]));
        A.CallTo(() => deviceCache.GetEmbeddingAsync("sensor:sensor.kitchen_temperature", A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(oldEmbedding));

        var embeddingResolver = A.Fake<IEmbeddingProviderResolver>();
        var oldGenerator = CreateEmbeddingGenerator(oldEmbedding);
        var newGenerator = CreateEmbeddingGenerator(newEmbedding);
        A.CallTo(() => embeddingResolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                var providerName = call.GetArgument<string?>(0);
                var generator = string.Equals(providerName, "new-provider", StringComparison.Ordinal)
                    ? newGenerator
                    : oldGenerator;
                return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(generator);
            });

        var entityMatcher = A.Fake<IHybridEntityMatcher>();
        var seenEmbeddings = new List<float>();
        A.CallTo(() => entityMatcher.FindMatchesAsync<SensorEntity>(
                A<string>.That.IsNotNull(),
                A<IReadOnlyList<SensorEntity>>._,
                A<IEmbeddingGenerator<string, Embedding<float>>>._,
                A<HybridMatchOptions>._,
                A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                var candidates = call.GetArgument<IReadOnlyList<SensorEntity>>(1)!;
                seenEmbeddings.Add(candidates[0].NameEmbedding!.Vector.ToArray()[0]);
                return Task.FromResult<IReadOnlyList<EntityMatchResult<SensorEntity>>>(
                [
                    new EntityMatchResult<SensorEntity>
                    {
                        Entity = candidates![0],
                        HybridScore = 0.95,
                        EmbeddingSimilarity = 0.95,
                    },
                ]);
            });

        var skill = new SensorControlSkill(
            homeAssistantClient,
            embeddingResolver,
            NullLogger<SensorControlSkill>.Instance,
            deviceCache,
            A.Fake<IEntityLocationService>(),
            entityMatcher,
            new TestOptionsMonitor<SensorControlSkillOptions>(new SensorControlSkillOptions()));

        var agentDefinitionRepository = A.Fake<IAgentDefinitionRepository>();
        var oldDefinition = CreateDefinition("old-provider", DateTime.UtcNow);
        var newDefinition = CreateDefinition("new-provider", DateTime.UtcNow.AddYears(1));
        var callCount = 0;
        A.CallTo(() => agentDefinitionRepository.GetAgentDefinitionAsync("sensor-agent", A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                callCount++;
                return Task.FromResult<AgentDefinition?>(callCount == 1 ? oldDefinition : newDefinition);
            });

        var chatClientResolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => chatClientResolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(A.Fake<AIAgent>()));

        var agent = new SensorAgent(
            chatClientResolver,
            agentDefinitionRepository,
            skill,
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);

        await agent.InitializeAsync();
        _ = await skill.FindSensorAsync("kitchen");

        await agent.RefreshConfigAsync();
        _ = await skill.FindSensorAsync("kitchen");

        Assert.Equal([1f, 2f], seenEmbeddings);
    }

    private static AgentDefinition CreateDefinition(
        string? embeddingProviderName,
        DateTime updatedAt,
        string? modelConnectionName = null)
    {
        return new AgentDefinition
        {
            Id = "sensor-agent",
            Name = "sensor-agent",
            DisplayName = "Sensor Agent",
            Description = "Sensor agent",
            Instructions = "Use tools",
            EmbeddingProviderName = embeddingProviderName,
            ModelConnectionName = modelConnectionName,
            UpdatedAt = updatedAt,
        };
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(Embedding<float> embedding)
    {
        return new FixedEmbeddingGenerator(embedding);
    }
}

