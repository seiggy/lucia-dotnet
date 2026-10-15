using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services.EntityLocation;

/// <summary>
/// Behaviour every <see cref="IEntityLocationService"/> must keep, whichever persistence it uses.
/// Written against the current Redis and in-memory services before they are restructured.
/// Assertions name the entities that must be resolved, not internal scores.
/// </summary>
public abstract class EntityLocationServiceContract
{
    protected abstract Task<StoreScope<IEntityLocationService>> CreateAsync(SampleHomeAssistant home);

    protected static IEmbeddingProviderResolver EmbeddingResolver()
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(new DeterministicEmbeddingGenerator()));
        return resolver;
    }

    protected static IHybridEntityMatcher Matcher()
        => new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance);

    protected static LocationSearchService Search()
        => new(EmbeddingResolver(), Matcher(), NullLogger<LocationSearchService>.Instance);

    protected static async Task LoadAsync(IEntityLocationService service)
    {
        await service.InitializeAsync();
        await WaitForEmbeddingsAsync(service);
    }

    protected static async Task WaitForEmbeddingsAsync(IEntityLocationService service)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var progress = await service.GetEmbeddingProgressAsync();
            if (!progress.IsGenerationRunning && progress.EntityMissingCount == 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Embeddings were not generated in time.");
    }

    private static string[] Ids(IEnumerable<lucia.Agents.Models.HomeAssistant.HomeAssistantEntity> entities)
        => entities.Select(entity => entity.EntityId).Order().ToArray();

    // ── Loading ─────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_LoadsFloorsAreasAndEnabledEntities()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);

        await LoadAsync(scope.Store);

        Assert.Equal(["Ground Floor", "Upper Floor"], (await scope.Store.GetFloorsAsync()).Select(f => f.Name).Order());
        Assert.Equal(4, (await scope.Store.GetAreasAsync()).Count);
        var entities = await scope.Store.GetEntitiesAsync();
        Assert.Equal(7, entities.Count);
        Assert.DoesNotContain(entities, entity => entity.EntityId == "light.retired");
        Assert.True(scope.Store.IsCacheReady);
        Assert.NotNull(scope.Store.LastLoadedAt);
    }

    [Fact]
    public async Task InitializeAsync_AppliesRegistryNamesAndAliases()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var lamp = (await scope.Store.GetEntitiesAsync()).Single(entity => entity.EntityId == "light.living_room_lamp");

        Assert.Equal("Living Room Lamp", lamp.FriendlyName);
        Assert.Contains("reading lamp", lamp.Aliases);
        Assert.Equal("living_room", lamp.AreaId);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_ReadsHomeAssistantOnce()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);

        await LoadAsync(scope.Store);
        await scope.Store.InitializeAsync();

        Assert.Equal(1, home.RegistryLoadCount);
    }

    [Fact]
    public async Task GetAreaForEntity_AndGetFloorForArea_FollowTheRegistry()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.Equal("Kitchen", scope.Store.GetAreaForEntity("light.kitchen_island")?.Name);
        Assert.Null(scope.Store.GetAreaForEntity("light.porch"));
        Assert.Null(scope.Store.GetAreaForEntity("light.nope"));
        Assert.Equal("Ground Floor", scope.Store.GetFloorForArea("kitchen")?.Name);
        Assert.Null(scope.Store.GetFloorForArea("nope"));
    }

    [Fact]
    public async Task GetSnapshot_ExposesTheLoadedData()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var snapshot = scope.Store.GetSnapshot();

        Assert.Equal(2, snapshot.Floors.Length);
        Assert.Equal(7, snapshot.Entities.Length);
        Assert.True(snapshot.EntityById.ContainsKey("light.kitchen_ceiling"));
        Assert.True(snapshot.AreaById.ContainsKey("kitchen"));
    }

    // ── Exact match ─────────────────────────────────────────────────

    [Fact]
    public async Task ExactMatchEntities_ByEntityId_ReturnsThatEntity()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.Equal(["light.kitchen_ceiling"], Ids(scope.Store.ExactMatchEntities("light.kitchen_ceiling")));
        Assert.Empty(scope.Store.ExactMatchEntities("light.kitchen_ceiling", ["climate"]));
    }

    [Fact]
    public async Task ExactMatchEntities_ByAreaName_ReturnsEntitiesInTheAreaFilteredByDomain()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.Equal(["climate.upstairs_thermostat", "light.bedroom_main"], Ids(scope.Store.ExactMatchEntities("Bedroom")));
        Assert.Equal(["light.bedroom_main"], Ids(scope.Store.ExactMatchEntities("bedroom", ["light"])));
    }

    [Fact]
    public async Task ExactMatchArea_MatchesByDisplayNameOnly()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.Null(scope.Store.ExactMatchArea("living_room"));
        Assert.Equal("living_room", scope.Store.ExactMatchArea("Living Room")?.AreaId);
        Assert.Null(scope.Store.ExactMatchArea("lounge"));
        Assert.Null(scope.Store.ExactMatchArea("garage"));
    }

    [Fact]
    public async Task ExactMatchEntities_BeforeAnyLoad_ReturnsNothing()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);

        Assert.Empty(scope.Store.ExactMatchEntities("light.kitchen_ceiling"));
        Assert.False(scope.Store.IsCacheReady);
        Assert.Null(scope.Store.ExactMatchArea("kitchen"));
    }

    // ── Search ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchHierarchyAsync_AreaName_ResolvesEveryEntityInTheArea()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var result = await scope.Store.SearchHierarchyAsync("Kitchen", domainFilter: ["light"]);

        Assert.Equal(["light.kitchen_ceiling", "light.kitchen_island"], Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SearchHierarchyAsync_SpecificDevice_ResolvesThatDevice()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var result = await scope.Store.SearchHierarchyAsync("Kitchen Ceiling", domainFilter: ["light"]);

        Assert.Equal(["light.kitchen_ceiling"], Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SearchHierarchyAsync_FloorName_ResolvesEveryEntityOnTheFloor()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var result = await scope.Store.SearchHierarchyAsync("Upper Floor");

        Assert.Equal(
            ["climate.upstairs_thermostat", "light.bedroom_main", "media_player.office_speaker"],
            Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SearchHierarchyAsync_DomainFilter_LimitsTheResolvedEntities()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var result = await scope.Store.SearchHierarchyAsync("Upper Floor", domainFilter: ["climate"]);

        Assert.Equal(["climate.upstairs_thermostat"], Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SearchHierarchyAsync_Gibberish_ResolvesNothing()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var result = await scope.Store.SearchHierarchyAsync("zzqx wvvp");

        Assert.Empty(result.ResolvedEntities);
    }

    [Fact]
    public async Task FindEntitiesByLocationAsync_ReturnsTheEntitiesOfTheMatchedArea()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var found = await scope.Store.FindEntitiesByLocationAsync("Kitchen", ["light"]);

        Assert.Equal(["light.kitchen_ceiling", "light.kitchen_island"], Ids(found));
    }

    [Fact]
    public async Task SearchEntitiesAsync_FindsADeviceByAlias()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var matches = await scope.Store.SearchEntitiesAsync("reading lamp", ["light"]);

        Assert.Equal("light.living_room_lamp", matches[0].Entity.EntityId);
    }

    // ── Visibility ──────────────────────────────────────────────────

    [Fact]
    public async Task GetVisibilityConfigAsync_Default_ExposesEverything()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var config = await scope.Store.GetVisibilityConfigAsync();

        Assert.False(config.UseExposedEntitiesOnly);
        Assert.Empty(config.EntityAgentMap);
    }

    [Fact]
    public async Task SetEntityAgentsAsync_RestrictsAnEntityToTheListedAgents()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        await scope.Store.SetEntityAgentsAsync(new Dictionary<string, List<string>?>
        {
            ["light.kitchen_ceiling"] = ["light-agent"],
        });

        var filtered = Assert.IsAssignableFrom<IAgentFilteredEntityLocationService>(scope.Store);
        var forLights = await filtered.SearchHierarchyForAgentAsync("Kitchen", null, ["light"], "light-agent");
        var forOthers = await filtered.SearchHierarchyForAgentAsync("Kitchen", null, ["light"], "scene-agent");
        Assert.Equal(["light.kitchen_ceiling", "light.kitchen_island"], Ids(forLights.ResolvedEntities));
        Assert.Equal(["light.kitchen_island"], Ids(forOthers.ResolvedEntities));
        Assert.Equal(["light-agent"], (await scope.Store.GetVisibilityConfigAsync()).EntityAgentMap["light.kitchen_ceiling"]);
    }

    [Fact]
    public async Task SetEntityAgentsAsync_EmptyList_HidesTheEntityFromEveryAgent()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        await scope.Store.SetEntityAgentsAsync(new Dictionary<string, List<string>?> { ["light.kitchen_ceiling"] = [] });

        var result = await scope.Store.SearchHierarchyAsync("Kitchen", domainFilter: ["light"]);
        Assert.Equal(["light.kitchen_island"], Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SetEntityAgentsAsync_EmptyList_HidesTheEntityWithoutADomainFilter()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        await scope.Store.SetEntityAgentsAsync(new Dictionary<string, List<string>?> { ["light.kitchen_ceiling"] = [] });

        var result = await scope.Store.SearchHierarchyAsync("Kitchen");
        Assert.DoesNotContain("light.kitchen_ceiling", Ids(result.ResolvedEntities));
        Assert.Contains("light.kitchen_island", Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SetEntityAgentsAsync_ConcurrentUpdates_AllLand()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);
        var entityIds = home.Entities.Select(entity => entity.EntityId).ToArray();

        await Task.WhenAll(entityIds.Select(entityId => Task.Run(() => scope.Store.SetEntityAgentsAsync(
            new Dictionary<string, List<string>?> { [entityId] = ["light-agent"] }))));

        var map = (await scope.Store.GetVisibilityConfigAsync()).EntityAgentMap;
        Assert.Equal(entityIds.Order(), map.Keys.Order());
    }

    [Fact]
    public async Task ClearAllAgentFiltersAsync_RestoresVisibilityToEveryAgent()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);
        await scope.Store.SetEntityAgentsAsync(new Dictionary<string, List<string>?> { ["light.kitchen_ceiling"] = ["light-agent"] });

        await scope.Store.ClearAllAgentFiltersAsync();

        Assert.Empty((await scope.Store.GetVisibilityConfigAsync()).EntityAgentMap);
        var filtered = Assert.IsAssignableFrom<IAgentFilteredEntityLocationService>(scope.Store);
        var result = await filtered.SearchHierarchyForAgentAsync("Kitchen", null, ["light"], "scene-agent");
        Assert.Equal(["light.kitchen_ceiling", "light.kitchen_island"], Ids(result.ResolvedEntities));
    }

    [Fact]
    public async Task SetUseExposedOnlyAsync_ReloadsOnlyTheExposedEntities()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        await scope.Store.SetUseExposedOnlyAsync(true);
        await WaitForEmbeddingsAsync(scope.Store);

        Assert.True((await scope.Store.GetVisibilityConfigAsync()).UseExposedEntitiesOnly);
        Assert.Equal(
            ["light.kitchen_ceiling", "light.living_room_lamp"],
            Ids(await scope.Store.GetEntitiesAsync()));

        await scope.Store.SetUseExposedOnlyAsync(false);
        await WaitForEmbeddingsAsync(scope.Store);
        Assert.Equal(7, (await scope.Store.GetEntitiesAsync()).Count);
    }

    [Fact]
    public async Task SetUseExposedOnlyAsync_DuringAReload_StillFiltersTheEntities()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        A.CallTo(() => home.Client.GetFloorRegistryAsync(A<CancellationToken>._))
            .ReturnsLazily(async () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    blocked.SetResult();
                    await gate.Task;
                }

                return home.Floors.ToArray();
            });

        var reload = scope.Store.InvalidateAndReloadAsync();
        await blocked.Task;
        var toggle = scope.Store.SetUseExposedOnlyAsync(true);
        gate.SetResult();
        await Task.WhenAll(reload, toggle);
        await WaitForEmbeddingsAsync(scope.Store);

        Assert.Equal(
            ["light.kitchen_ceiling", "light.living_room_lamp"],
            Ids(await scope.Store.GetEntitiesAsync()));
    }

    // ── Reload and edits ────────────────────────────────────────────

    [Fact]
    public async Task InvalidateAndReloadAsync_PicksUpChangesFromHomeAssistant()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);
        home.Entities.Add(SampleHomeAssistant.Entity("light.garage", "Garage Light"));

        await scope.Store.InvalidateAndReloadAsync();
        await WaitForEmbeddingsAsync(scope.Store);

        Assert.Contains(await scope.Store.GetEntitiesAsync(), entity => entity.EntityId == "light.garage");
        Assert.Equal(2, home.RegistryLoadCount);
    }

    [Fact]
    public async Task RemoveEntityAsync_DropsTheEntityUntilTheNextReload()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.True(await scope.Store.RemoveEntityAsync("light.porch"));
        Assert.False(await scope.Store.RemoveEntityAsync("light.porch"));
        Assert.DoesNotContain(await scope.Store.GetEntitiesAsync(), entity => entity.EntityId == "light.porch");

        await scope.Store.InvalidateAndReloadAsync();
        Assert.Contains(await scope.Store.GetEntitiesAsync(), entity => entity.EntityId == "light.porch");
    }

    // ── Embeddings ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEmbeddingProgressAsync_AfterGeneration_CoversEveryItem()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        var progress = await scope.Store.GetEmbeddingProgressAsync();

        Assert.Equal(2, progress.FloorTotalCount);
        Assert.Equal(2, progress.FloorGeneratedCount);
        Assert.Equal(4, progress.AreaGeneratedCount);
        Assert.Equal(7, progress.EntityTotalCount);
        Assert.Equal(7, progress.EntityGeneratedCount);
        Assert.Equal(0, progress.EntityMissingCount);
    }

    [Fact]
    public async Task EvictEmbeddingAsync_ThenRegenerateEmbeddingAsync_RoundTrips()
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.True(await scope.Store.EvictEmbeddingAsync("entity", "light.kitchen_ceiling"));
        Assert.Equal(1, (await scope.Store.GetEmbeddingProgressAsync()).EntityMissingCount);

        Assert.True(await scope.Store.RegenerateEmbeddingAsync("entity", "light.kitchen_ceiling"));
        Assert.Equal(0, (await scope.Store.GetEmbeddingProgressAsync()).EntityMissingCount);
    }

    [Theory]
    [InlineData("entity", "light.nope")]
    [InlineData("gadget", "light.kitchen_ceiling")]
    [InlineData("", "light.kitchen_ceiling")]
    public async Task EvictAndRegenerateEmbeddingAsync_UnknownTargets_ReturnFalse(string itemType, string itemId)
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.False(await scope.Store.EvictEmbeddingAsync(itemType, itemId));
        Assert.False(await scope.Store.RegenerateEmbeddingAsync(itemType, itemId));
    }

    [Theory]
    [InlineData("floor", "ground")]
    [InlineData("area", "kitchen")]
    public async Task EvictEmbeddingAsync_FloorsAndAreas_AreSupported(string itemType, string itemId)
    {
        var home = new SampleHomeAssistant();
        await using var scope = await CreateAsync(home);
        await LoadAsync(scope.Store);

        Assert.True(await scope.Store.EvictEmbeddingAsync(itemType, itemId));
    }
}