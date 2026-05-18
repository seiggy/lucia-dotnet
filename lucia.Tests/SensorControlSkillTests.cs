using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class SensorControlSkillTests
{
    [Fact]
    public async Task InvalidateEmbeddingCacheAsync_RebuildsEmbeddingsWithCurrentProvider()
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
                        Entity = candidates[0],
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
            CreateOptionsMonitor());

        await skill.InitializeAsync();
        _ = await skill.FindSensorAsync("kitchen");

        await skill.UpdateEmbeddingProviderAsync("new-provider");
        await skill.InvalidateEmbeddingCacheAsync();
        _ = await skill.FindSensorAsync("kitchen");

        Assert.Equal([1f, 2f], seenEmbeddings);
    }

    [Fact]
    public async Task InitializeAsync_UsesConfiguredEntityDomains_WhenBuildingCache()
    {
        var homeAssistantClient = new FakeHomeAssistantClient();
        await homeAssistantClient.SetEntityStateAsync(
            "custom_sensor.kitchen_air_quality",
            "good",
            new Dictionary<string, object>
            {
                ["friendly_name"] = "Kitchen Air Quality",
            });
        await homeAssistantClient.SetEntityStateAsync(
            "sensor.kitchen_temperature",
            "72",
            new Dictionary<string, object>
            {
                ["friendly_name"] = "Kitchen Temperature",
            });

        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(null));

        var entityMatcher = A.Fake<IHybridEntityMatcher>();
        var capturedEntityIds = new List<string>();
        A.CallTo(() => entityMatcher.FindMatchesAsync<SensorEntity>(
                A<string>.That.IsNotNull(),
                A<IReadOnlyList<SensorEntity>>._,
                A<IEmbeddingGenerator<string, Embedding<float>>>._,
                A<HybridMatchOptions>._,
                A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                var candidates = call.GetArgument<IReadOnlyList<SensorEntity>>(1)!;
                capturedEntityIds.AddRange(candidates.Select(candidate => candidate.EntityId));
                return Task.FromResult<IReadOnlyList<EntityMatchResult<SensorEntity>>>(
                [
                    new EntityMatchResult<SensorEntity>
                    {
                        Entity = candidates[0],
                        HybridScore = 0.90,
                        EmbeddingSimilarity = 0.90,
                    },
                ]);
            });

        var skill = new SensorControlSkill(
            homeAssistantClient,
            new StubEmbeddingProviderResolver(CreateEmbeddingGenerator(new Embedding<float>(new float[] { 3f }))),
            NullLogger<SensorControlSkill>.Instance,
            deviceCache,
            A.Fake<IEntityLocationService>(),
            entityMatcher,
            CreateOptionsMonitor(["custom_sensor"]));

        await skill.InitializeAsync();
        _ = await skill.FindSensorAsync("kitchen");

        Assert.Equal(["custom_sensor.kitchen_air_quality"], capturedEntityIds);
    }

    [Fact]
    public async Task FindSensorAsync_ReturnsGenericError_WhenMatcherThrows()
    {
        var skill = await CreateInitializedSkillAsync(
            entityMatcher: CreateThrowingMatcher(),
            locationService: A.Fake<IEntityLocationService>());

        var result = await skill.FindSensorAsync("kitchen");

        Assert.Equal("Unable to query sensor data. Please try again.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindSensorsByAreaAsync_ReturnsGenericError_WhenLocationLookupThrows()
    {
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                A<string>.That.IsNotNull(),
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret area failure"));

        var skill = await CreateInitializedSkillAsync(locationService: locationService);

        var result = await skill.FindSensorsByAreaAsync("kitchen");

        Assert.Equal("Unable to query sensors for that area. Please try again.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSensorStateAsync_ReturnsGenericError_WhenHomeAssistantThrows()
    {
        var homeAssistantClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistantClient.GetEntityStateAsync("sensor.kitchen_temperature", A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret sensor failure"));

        var skill = CreateSkill(homeAssistantClient: homeAssistantClient);

        var result = await skill.GetSensorStateAsync("sensor.kitchen_temperature");

        Assert.Equal("Unable to retrieve sensor data. Please try again.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetBinarySensorStateAsync_ReturnsGenericError_WhenHomeAssistantThrows()
    {
        var homeAssistantClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistantClient.GetEntityStateAsync("binary_sensor.front_door", A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret binary sensor failure"));

        var skill = CreateSkill(homeAssistantClient: homeAssistantClient);

        var result = await skill.GetBinarySensorStateAsync("binary_sensor.front_door");

        Assert.Equal("Unable to retrieve binary sensor data. Please try again.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAreaSensorsAsync_ReturnsGenericError_WhenLocationLookupThrows()
    {
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                A<string>.That.IsNotNull(),
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret area sensor failure"));

        var skill = await CreateInitializedSkillAsync(locationService: locationService);

        var result = await skill.GetAreaSensorsAsync("kitchen", "temperature");

        Assert.Equal("Unable to query sensors for that area. Please try again.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    private static SensorControlSkill CreateSkill(
        IHomeAssistantClient? homeAssistantClient = null,
        IEmbeddingProviderResolver? embeddingResolver = null,
        IDeviceCacheService? deviceCache = null,
        IEntityLocationService? locationService = null,
        IHybridEntityMatcher? entityMatcher = null,
        TestOptionsMonitor<SensorControlSkillOptions>? options = null)
    {
        return new SensorControlSkill(
            homeAssistantClient ?? A.Fake<IHomeAssistantClient>(),
            embeddingResolver ?? new StubEmbeddingProviderResolver(CreateEmbeddingGenerator(new Embedding<float>(new float[] { 1f }))),
            NullLogger<SensorControlSkill>.Instance,
            deviceCache ?? A.Fake<IDeviceCacheService>(),
            locationService ?? A.Fake<IEntityLocationService>(),
            entityMatcher ?? A.Fake<IHybridEntityMatcher>(),
            options ?? CreateOptionsMonitor());
    }

    private static async Task<SensorControlSkill> CreateInitializedSkillAsync(
        IHomeAssistantClient? homeAssistantClient = null,
        IEmbeddingProviderResolver? embeddingResolver = null,
        IDeviceCacheService? deviceCache = null,
        IEntityLocationService? locationService = null,
        IHybridEntityMatcher? entityMatcher = null,
        TestOptionsMonitor<SensorControlSkillOptions>? options = null)
    {
        var resolvedDeviceCache = deviceCache ?? A.Fake<IDeviceCacheService>();
        A.CallTo(() => resolvedDeviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(
            [
                new SensorEntity
                {
                    EntityId = "sensor.kitchen_temperature",
                    FriendlyName = "Kitchen Temperature",
                },
            ]));
        A.CallTo(() => resolvedDeviceCache.GetEmbeddingAsync("sensor:sensor.kitchen_temperature", A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(new Embedding<float>(new float[] { 1f })));

        var skill = CreateSkill(
            homeAssistantClient,
            embeddingResolver,
            resolvedDeviceCache,
            locationService,
            entityMatcher,
            options);

        await skill.InitializeAsync();
        return skill;
    }

    private static IHybridEntityMatcher CreateThrowingMatcher()
    {
        var entityMatcher = A.Fake<IHybridEntityMatcher>();
        A.CallTo(() => entityMatcher.FindMatchesAsync<SensorEntity>(
                A<string>.That.IsNotNull(),
                A<IReadOnlyList<SensorEntity>>._,
                A<IEmbeddingGenerator<string, Embedding<float>>>._,
                A<HybridMatchOptions>._,
                A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret matcher failure"));
        return entityMatcher;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(Embedding<float> embedding)
    {
        return new FixedEmbeddingGenerator(embedding);
    }

    private static TestOptionsMonitor<SensorControlSkillOptions> CreateOptionsMonitor(IEnumerable<string>? entityDomains = null)
    {
        return new TestOptionsMonitor<SensorControlSkillOptions>(new SensorControlSkillOptions
        {
            CacheRefreshMinutes = 5,
            EntityDomains = entityDomains?.ToList() ?? ["sensor", "binary_sensor"],
        });
    }
}
