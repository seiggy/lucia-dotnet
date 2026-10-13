using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Pins the observable behaviour of <see cref="FanControlSkill"/> before it moves onto the shared skill
/// helpers. Expected strings are what the LLM sees from each tool.
/// </summary>
public sealed class FanControlSkillTests
{
    private static readonly string NewLine = Environment.NewLine;

    private static JsonElement List(params object[] items) => JsonSerializer.SerializeToElement(items);

    private static async Task<FakeHomeAssistantClient> CreateHomeAssistantAsync()
    {
        var ha = new FakeHomeAssistantClient();
        await ha.SetEntityStateAsync("fan.living_room", "on", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Fan",
            ["percentage"] = 50,
            ["direction"] = "forward",
            ["preset_mode"] = "auto",
            ["oscillating"] = true,
            ["preset_modes"] = List("auto", "sleep"),
            ["supported_features"] = 15,
        });

        // Tuya style: the modes live on a companion select entity, not on the fan.
        await ha.SetEntityStateAsync("fan.office_fan_fan", "on", new Dictionary<string, object>
        {
            ["friendly_name"] = "Office Fan",
            ["percentage"] = 30,
            ["supported_features"] = 1,
        });
        await ha.SetEntityStateAsync("select.office_fan_fan_mode", "low", new Dictionary<string, object>
        {
            ["friendly_name"] = "Office Fan Mode",
            ["options"] = List("low", "high"),
        });
        return ha;
    }

    private static HierarchicalSearchResult Resolved(ResolutionStrategy strategy, params string[] entityIds) => new()
    {
        FloorMatches = [],
        AreaMatches = [],
        EntityMatches = [],
        ResolvedEntities = [.. entityIds.Select(id => new HomeAssistantEntity { EntityId = id, FriendlyName = id })],
        ResolutionStrategy = strategy,
        ResolutionReason = strategy == ResolutionStrategy.None ? "No matches at any level" : "stub",
    };

    private static IEntityLocationService CreateLocation(HierarchicalSearchResult result)
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Returns(result);
        A.CallTo(() => location.GetAreaForEntity("fan.living_room"))
            .Returns(new AreaInfo { AreaId = "living_room", Name = "Living Room" });
        A.CallTo(() => location.GetAreaForEntity("fan.office_fan_fan"))
            .Returns(new AreaInfo { AreaId = "office", Name = "Office" });
        return location;
    }

    private static IEmbeddingProviderResolver CreateResolver(IEmbeddingGenerator<string, Embedding<float>>? generator)
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult(generator));
        return resolver;
    }

    private static IDeviceCacheService CreateEmptyDeviceCache()
    {
        var cache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => cache.GetCachedFansAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<FanEntity>?>(null));
        return cache;
    }

    private static async Task<FanControlSkill> CreateSkillAsync(
        IHomeAssistantClient ha,
        IEntityLocationService location,
        IDeviceCacheService? deviceCache = null,
        IEmbeddingProviderResolver? resolver = null)
    {
        var skill = new FanControlSkill(
            ha,
            resolver ?? CreateResolver(new DeterministicEmbeddingGenerator()),
            deviceCache ?? CreateEmptyDeviceCache(),
            location,
            new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance),
            new TestOptionsMonitor<FanControlSkillOptions>(new FanControlSkillOptions()),
            NullLogger<FanControlSkill>.Instance);

        await skill.InitializeAsync();
        return skill;
    }

    private static async Task<FanControlSkill> CreateLoadedSkillAsync(HierarchicalSearchResult? hierarchy = null)
        => await CreateSkillAsync(
            await CreateHomeAssistantAsync(),
            CreateLocation(hierarchy ?? Resolved(ResolutionStrategy.None)));

    // ── Tools and configuration ─────────────────────────────────────────

    [Fact]
    public async Task GetTools_ExposesTheEightFanTools_WithoutAnAsyncSuffix()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            [
                "FindFan", "FindFansByArea", "GetFanState", "SetFanDirection",
                "SetFanOscillation", "SetFanPresetMode", "SetFanSpeed", "SetFanState",
            ],
            skill.GetTools().Select(tool => tool.Name).Order().ToArray());
    }

    [Fact]
    public async Task GetCurrentMatchOptions_ReflectsTheConfiguredOptions()
    {
        var skill = await CreateLoadedSkillAsync();

        var options = skill.GetCurrentMatchOptions();

        Assert.Equal(0.55, options.Threshold);
        Assert.Equal(0.4, options.EmbeddingWeight);
        Assert.Equal(0.80, options.ScoreDropoffRatio);
        Assert.Equal(["fan"], skill.EntityDomains);
    }

    // ── FindFanAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task FindFanAsync_ExactName_ListsThatFanWithItsAreaAndScore()
    {
        var skill = await CreateLoadedSkillAsync();

        var result = await skill.FindFanAsync("Living Room Fan");

        Assert.StartsWith($"Found 1 fan(s) matching 'Living Room Fan':{NewLine}  - Living Room Fan (fan.living_room) in Living Room [score: ", result);
        Assert.EndsWith($"]{NewLine}", result);
    }

    [Fact]
    public async Task FindFanAsync_NoFansInHomeAssistant_SaysTheCacheIsEmpty()
    {
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("No fans found. The fan cache may be empty.", await skill.FindFanAsync("fan"));
    }

    [Fact]
    public async Task FindFanAsync_ProviderRemovedAfterLoading_ReportsItUnavailable()
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call => Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(
                call.GetArgument<string?>(0) == "gone" ? null : new DeterministicEmbeddingGenerator()));
        var skill = await CreateSkillAsync(
            await CreateHomeAssistantAsync(), CreateLocation(Resolved(ResolutionStrategy.None)), resolver: resolver);
        await skill.UpdateEmbeddingProviderAsync("gone");

        Assert.Equal("Embedding provider not available for fan search.", await skill.FindFanAsync("Living Room Fan"));
    }

    [Fact]
    public async Task FindFanAsync_NoNameMatchButAreaResolves_ListsTheAreasFans()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "fan.living_room"));

        Assert.Equal(
            $"Found 1 fan(s) matching 'downstairs':{NewLine}  - Living Room Fan (fan.living_room) in Living Room{NewLine}",
            await skill.FindFanAsync("downstairs"));
    }

    [Fact]
    public async Task FindFanAsync_NothingMatches_SaysSo()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal("No fans found matching 'qzxv wvpp'.", await skill.FindFanAsync("qzxv wvpp"));
    }

    [Fact]
    public async Task FindFanAsync_WhenTheLocationLookupFails_ReturnsTheFailureText()
    {
        var location = CreateLocation(Resolved(ResolutionStrategy.None));
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));
        var skill = await CreateSkillAsync(await CreateHomeAssistantAsync(), location);

        Assert.Equal("Unable to search for fans. Please try again.", await skill.FindFanAsync("qzxv wvpp"));
    }

    // ── FindFansByAreaAsync ─────────────────────────────────────────────

    [Fact]
    public async Task FindFansByAreaAsync_ResolvedArea_ListsFansWithTheirFeatures()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "fan.living_room", "fan.office_fan_fan"));

        var result = await skill.FindFansByAreaAsync("downstairs");

        Assert.Equal(
            $"Fans matching 'downstairs':{NewLine}" +
            $"  - Living Room Fan (fan.living_room) [speed, direction, oscillate, presets: [auto, sleep]]{NewLine}" +
            $"  - Office Fan (fan.office_fan_fan) [speed, presets: [low, high]]{NewLine}",
            result);
    }

    [Fact]
    public async Task FindFansByAreaAsync_NoAreaMatch_IncludesTheResolutionReason()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal("No fans found matching 'garage'. No matches at any level", await skill.FindFansByAreaAsync("garage"));
    }

    [Fact]
    public async Task FindFansByAreaAsync_AreaHasNoFans_SaysSo()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "light.kitchen"));

        Assert.Equal("No fans found matching 'kitchen'.", await skill.FindFansByAreaAsync("kitchen"));
    }

    [Fact]
    public async Task FindFansByAreaAsync_NoFansInHomeAssistant_SaysTheCacheIsEmpty()
    {
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("No fans found. The fan cache may be empty.", await skill.FindFansByAreaAsync("kitchen"));
    }

    // ── GetFanStateAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetFanStateAsync_NativeFan_ReportsStateAndCapabilities()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "State: on, Speed: 50%, Direction: forward, Preset: auto, Oscillating: True, " +
            "Capabilities: [speed, direction, oscillate, presets: [auto, sleep]]",
            await skill.GetFanStateAsync("fan.living_room"));
    }

    [Fact]
    public async Task GetFanStateAsync_FanWithACompanionSelect_ReportsTheSelectsModes()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "State: on, Speed: 30%, Mode source: select entity, Available modes: [low, high], " +
            "Capabilities: [speed, presets: [low, high]]",
            await skill.GetFanStateAsync("fan.office_fan_fan"));
    }

    [Fact]
    public async Task GetFanStateAsync_UnknownFan_SaysNotFound()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal("Fan 'fan.nope' not found.", await skill.GetFanStateAsync("fan.nope"));
    }

    [Fact]
    public async Task GetFanStateAsync_WhenHomeAssistantFails_ReturnsTheFailureText()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetEntityStateAsync("fan.living_room", A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("Unable to get the state of fan 'fan.living_room'. Please try again.", await skill.GetFanStateAsync("fan.living_room"));
    }

    // ── Control tools ───────────────────────────────────────────────────

    private static bool HasData(ServiceCallRequest? request, string entityId, string? key, object? expected)
        => request is not null
           && request.EntityId == entityId
           && (key is null
               ? request.Count == 1
               : request.TryGetValue(key, out var value) && Equals(value, expected));

    private static async Task AssertServiceCallAsync(
        Func<FanControlSkill, Task<string>> invoke,
        string domain,
        string service,
        string entityId,
        string? dataKey,
        object? dataValue,
        string expectedMessage)
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        var result = await invoke(skill);

        Assert.Equal(expectedMessage, result);
        A.CallTo(() => ha.CallServiceAsync(
                domain,
                service,
                A<string?>._,
                A<ServiceCallRequest?>.That.Matches(request => HasData(request, entityId, dataKey, dataValue)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public Task SetFanStateAsync_On_CallsTurnOn()
        => AssertServiceCallAsync(
            skill => skill.SetFanStateAsync("fan.living_room", "on"),
            "fan", "turn_on", "fan.living_room", null, null,
            "Turned on fan 'fan.living_room'.");

    [Fact]
    public Task SetFanStateAsync_Off_CallsTurnOff()
        => AssertServiceCallAsync(
            skill => skill.SetFanStateAsync("fan.living_room", "off"),
            "fan", "turn_off", "fan.living_room", null, null,
            "Turned off fan 'fan.living_room'.");

    [Fact]
    public Task SetFanSpeedAsync_CallsSetPercentage()
        => AssertServiceCallAsync(
            skill => skill.SetFanSpeedAsync("fan.living_room", 40),
            "fan", "set_percentage", "fan.living_room", "percentage", 40,
            "Set fan 'fan.living_room' speed to 40%.");

    [Fact]
    public Task SetFanDirectionAsync_CallsSetDirection()
        => AssertServiceCallAsync(
            skill => skill.SetFanDirectionAsync("fan.living_room", "reverse"),
            "fan", "set_direction", "fan.living_room", "direction", "reverse",
            "Set fan 'fan.living_room' direction to 'reverse'.");

    [Fact]
    public Task SetFanOscillationAsync_On_CallsOscillate()
        => AssertServiceCallAsync(
            skill => skill.SetFanOscillationAsync("fan.living_room", true),
            "fan", "oscillate", "fan.living_room", "oscillating", true,
            "Set fan 'fan.living_room' oscillation to on.");

    [Fact]
    public Task SetFanOscillationAsync_Off_CallsOscillate()
        => AssertServiceCallAsync(
            skill => skill.SetFanOscillationAsync("fan.living_room", false),
            "fan", "oscillate", "fan.living_room", "oscillating", false,
            "Set fan 'fan.living_room' oscillation to off.");

    [Fact]
    public Task SetFanPresetModeAsync_NativeFan_CallsSetPresetMode()
        => AssertServiceCallAsync(
            skill => skill.SetFanPresetModeAsync("fan.living_room", "sleep"),
            "fan", "set_preset_mode", "fan.living_room", "preset_mode", "sleep",
            "Set fan 'fan.living_room' preset mode to 'sleep'.");

    [Fact]
    public Task SetFanPresetModeAsync_FanWithACompanionSelect_SelectsTheOptionIgnoringCase()
        => AssertServiceCallAsync(
            skill => skill.SetFanPresetModeAsync("fan.office_fan_fan", "HIGH"),
            "select", "select_option", "select.office_fan_fan_mode", "option", "high",
            "Set fan 'fan.office_fan_fan' mode to 'high'.");

    [Fact]
    public Task SetFanPresetModeAsync_UnknownFan_FallsBackToTheNativeService()
        => AssertServiceCallAsync(
            skill => skill.SetFanPresetModeAsync("fan.unknown", "eco"),
            "fan", "set_preset_mode", "fan.unknown", "preset_mode", "eco",
            "Set fan 'fan.unknown' preset mode to 'eco'.");

    [Fact]
    public async Task SetFanSpeedAsync_WhenHomeAssistantFails_ReturnsTheFailureText()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("Unable to set fan speed. Please try again.", await skill.SetFanSpeedAsync("fan.living_room", 40));
        Assert.Equal("Unable to set the fan state. Please try again.", await skill.SetFanStateAsync("fan.living_room", "on"));
        Assert.Equal("Unable to set fan direction. Please try again.", await skill.SetFanDirectionAsync("fan.living_room", "forward"));
        Assert.Equal("Unable to set fan oscillation. Please try again.", await skill.SetFanOscillationAsync("fan.living_room", true));
        Assert.Equal("Unable to set the fan preset mode. Please try again.", await skill.SetFanPresetModeAsync("fan.living_room", "auto"));
    }

    [Fact]
    public async Task SetFanSpeedAsync_WhenCancelled_Propagates()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.SetFanSpeedAsync("fan.living_room", 40));
    }

    // ── Cache ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_BuildsTheCacheOnceAndStoresItInTheDeviceCache()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = CreateEmptyDeviceCache();
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        await skill.FindFanAsync("Living Room Fan");
        await skill.FindFanAsync("Office Fan");

        A.CallTo(() => ha.GetStatesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => deviceCache.SetCachedFansAsync(
                A<List<FanEntity>>.That.Matches(fans => fans.Count == 2),
                A<TimeSpan>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InitializeAsync_WithACachedFanList_DoesNotReadStatesFromHomeAssistant()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedFansAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<FanEntity>?>(
            [
                new FanEntity { EntityId = "fan.living_room", FriendlyName = "Living Room Fan", PresetModes = ["cached-mode"], SupportedFeatures = 8 },
            ]));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        var state = await skill.GetFanStateAsync("fan.living_room");

        A.CallTo(() => ha.GetStatesAsync(A<CancellationToken>._)).MustNotHaveHappened();
        Assert.EndsWith("Capabilities: [presets: [cached-mode]]", state);
    }

    [Fact]
    public async Task FindFanAsync_WhenCancelled_Propagates()
    {
        var location = CreateLocation(Resolved(ResolutionStrategy.None));
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        var skill = await CreateSkillAsync(await CreateHomeAssistantAsync(), location);

        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.FindFanAsync("qzxv wvpp"));
    }

    [Fact]
    public async Task FindFanAsync_NoFansInHomeAssistant_DoesNotQueryHomeAssistantOnEveryCall()
    {
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(new FakeHomeAssistantClient()));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        await skill.FindFanAsync("fan");
        await skill.FindFanAsync("fan");

        A.CallTo(() => ha.GetStatesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InitializeAsync_WhenTheDeviceCacheWriteFails_StillUsesTheLoadedFans()
    {
        var deviceCache = CreateEmptyDeviceCache();
        A.CallTo(() => deviceCache.SetCachedFansAsync(A<List<FanEntity>>._, A<TimeSpan>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("cache down"));
        var skill = await CreateSkillAsync(
            await CreateHomeAssistantAsync(), CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        var result = await skill.FindFanAsync("Living Room Fan");

        Assert.StartsWith("Found 1 fan(s) matching 'Living Room Fan':", result);
    }
}
