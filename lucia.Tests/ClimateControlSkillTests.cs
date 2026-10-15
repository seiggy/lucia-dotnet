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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Pins the observable behaviour of <see cref="ClimateControlSkill"/> before it is refactored onto a shared
/// skill base. Expected strings are what the LLM sees from each tool.
/// </summary>
public sealed class ClimateControlSkillTests
{
    private const string LivingRoomLine =
        "Living Room Thermostat (Entity ID: climate.living_room), State: heat, Current Temperature: 68°, " +
        "Target Temperature: 70°, Current Humidity: 40%, Target Humidity: 45%, Action: heating, Fan Mode: auto, " +
        "Swing Mode: off, Preset Mode: eco, Available HVAC Modes: [off, heat, cool], Available Fan Modes: [auto, low], " +
        "Available Swing Modes: [off, on], Available Preset Modes: [eco, sleep], Temp Range: 50°-90°";

    private static JsonElement List(params object[] items) => JsonSerializer.SerializeToElement(items);

    private static async Task<FakeHomeAssistantClient> CreateHomeAssistantAsync()
    {
        var ha = new FakeHomeAssistantClient();
        await ha.SetEntityStateAsync("climate.living_room", "heat", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Thermostat",
            ["current_temperature"] = 68,
            ["temperature"] = 70,
            ["current_humidity"] = 40,
            ["humidity"] = 45,
            ["hvac_action"] = "heating",
            ["fan_mode"] = "auto",
            ["swing_mode"] = "off",
            ["preset_mode"] = "eco",
            ["hvac_modes"] = List("off", "heat", "cool"),
            ["fan_modes"] = List("auto", "low"),
            ["swing_modes"] = List("off", "on"),
            ["preset_modes"] = List("eco", "sleep"),
            ["min_temp"] = 50,
            ["max_temp"] = 90,
        });

        // SmartThings devices report numeric fan modes.
        await ha.SetEntityStateAsync("climate.bedroom", "cool", new Dictionary<string, object>
        {
            ["friendly_name"] = "Bedroom Climate",
            ["fan_modes"] = List(4, 1, 2, 3),
            ["min_temp"] = 60,
            ["max_temp"] = 80,
        });

        await ha.SetEntityStateAsync("sensor.living_room_temperature", "71.5", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Temperature",
            ["unit_of_measurement"] = "°F",
            ["device_class"] = "temperature",
        });
        await ha.SetEntityStateAsync("sensor.living_room_humidity", "41", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Humidity",
            ["unit_of_measurement"] = "%",
        });
        await ha.SetEntityStateAsync("sensor.living_room_motion", "on", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Motion",
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
        A.CallTo(() => location.GetAreaForEntity("climate.living_room"))
            .Returns(new AreaInfo { AreaId = "living_room", Name = "Living Room" });
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
        A.CallTo(() => cache.GetCachedClimateDevicesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<ClimateEntity>?>(null));
        return cache;
    }

    private static async Task<ClimateControlSkill> CreateSkillAsync(
        IHomeAssistantClient ha,
        IEntityLocationService location,
        IDeviceCacheService? deviceCache = null,
        IEmbeddingGenerator<string, Embedding<float>>? generator = null,
        int comfortAdjustment = 3)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClimateAgent:ComfortAdjustmentF"] = comfortAdjustment.ToString() })
            .Build();

        var skill = new ClimateControlSkill(
            ha,
            CreateResolver(generator ?? new DeterministicEmbeddingGenerator()),
            NullLogger<ClimateControlSkill>.Instance,
            deviceCache ?? CreateEmptyDeviceCache(),
            location,
            new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance),
            new TestOptionsMonitor<ClimateControlSkillOptions>(new ClimateControlSkillOptions()),
            configuration);

        await skill.InitializeAsync();
        return skill;
    }

    private static async Task<ClimateControlSkill> CreateLoadedSkillAsync(HierarchicalSearchResult? hierarchy = null)
        => await CreateSkillAsync(
            await CreateHomeAssistantAsync(),
            CreateLocation(hierarchy ?? Resolved(ResolutionStrategy.None)));

    // ── Tools and configuration ─────────────────────────────────────────

    [Fact]
    public async Task GetTools_ExposesTheTwelveClimateTools_WithoutAnAsyncSuffix()
    {
        var skill = await CreateLoadedSkillAsync();

        var names = skill.GetTools().Select(tool => tool.Name).Order().ToArray();

        Assert.Equal(
            [
                "FindClimateDevice", "FindClimateDevicesByArea", "GetAreaClimateSensors",
                "GetClimateState", "GetComfortAdjustment", "GetSensorState",
                "SetClimateFanMode", "SetClimateHumidity", "SetClimateHvacMode",
                "SetClimatePresetMode", "SetClimateSwingMode", "SetClimateTemperature",
            ],
            names);
    }

    [Fact]
    public async Task GetComfortAdjustment_UsesTheConfiguredDegrees()
    {
        var skill = await CreateSkillAsync(
            await CreateHomeAssistantAsync(), CreateLocation(Resolved(ResolutionStrategy.None)), comfortAdjustment: 5);

        Assert.Equal(
            "The configured comfort adjustment is 5°F. When the user says they are cold, increase the target temperature by 5°F. " +
            "When the user says they are hot, decrease it by 5°F.",
            skill.GetComfortAdjustment());
    }

    [Fact]
    public async Task GetCurrentMatchOptions_ReflectsTheConfiguredOptions()
    {
        var skill = await CreateLoadedSkillAsync();

        var options = skill.GetCurrentMatchOptions();

        Assert.Equal(0.55, options.Threshold);
        Assert.Equal(0.4, options.EmbeddingWeight);
        Assert.Equal(0.80, options.ScoreDropoffRatio);
        Assert.Equal(0.4, options.DisagreementPenalty);
        Assert.Equal(0.10, options.EmbeddingResolutionMargin);
        Assert.Equal(["climate"], skill.EntityDomains);
    }

    // ── FindClimateDeviceAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindClimateDeviceAsync_ExactName_ReturnsThatDeviceWithItsFullState()
    {
        var skill = await CreateLoadedSkillAsync();

        var result = await skill.FindClimateDeviceAsync("Living Room Thermostat");

        Assert.Equal($"Found climate device: {LivingRoomLine}", result);
    }

    [Fact]
    public async Task FindClimateDeviceAsync_NoClimateEntities_SaysSo()
    {
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("No climate devices available in the system.", await skill.FindClimateDeviceAsync("thermostat"));
    }

    [Fact]
    public async Task FindClimateDeviceAsync_NoClimateEntities_StillEmitsASpanWithTheElapsedTime()
    {
        using var recorder = new ActivityRecorder("Lucia.Skills.ClimateControl");
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        await skill.FindClimateDeviceAsync("empty-home-span-1");

        var activity = recorder.WithTag("search.term", "empty-home-span-1");
        Assert.NotNull(activity);
        Assert.NotNull(activity.GetTagItem("elapsed_ms"));
    }

    [Fact]
    public async Task FindClimateDevicesByAreaAsync_NoClimateEntities_StillEmitsASpanWithTheElapsedTime()
    {
        using var recorder = new ActivityRecorder("Lucia.Skills.ClimateControl");
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("No climate devices available in the system.", await skill.FindClimateDevicesByAreaAsync("empty-home-span-2"));

        var activity = recorder.WithTag("search.area", "empty-home-span-2");
        Assert.NotNull(activity);
        Assert.NotNull(activity.GetTagItem("elapsed_ms"));
    }

    [Fact]
    public async Task FindClimateDeviceAsync_NoEmbeddingProvider_ReportsNoDevices()
    {
        var ha = await CreateHomeAssistantAsync();
        var skill = new ClimateControlSkill(
            ha,
            CreateResolver(null),
            NullLogger<ClimateControlSkill>.Instance,
            CreateEmptyDeviceCache(),
            CreateLocation(Resolved(ResolutionStrategy.None)),
            new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance),
            new TestOptionsMonitor<ClimateControlSkillOptions>(new ClimateControlSkillOptions()),
            new ConfigurationBuilder().Build());
        await skill.InitializeAsync();

        Assert.Equal("No climate devices available in the system.", await skill.FindClimateDeviceAsync("Living Room Thermostat"));
    }

    [Fact]
    public async Task FindClimateDeviceAsync_NoNameMatchButAreaResolves_ListsTheAreasDevices()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "climate.living_room"));

        var result = await skill.FindClimateDeviceAsync("downstairs");

        Assert.Equal($"Found 1 climate device(s) matching 'downstairs':\n- {LivingRoomLine}".Replace("\n", Environment.NewLine), result);
    }

    [Fact]
    public async Task FindClimateDeviceAsync_NothingMatches_ListsAvailableDevices()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        var result = await skill.FindClimateDeviceAsync("qzxv wvpp");

        Assert.Equal(
            "No climate device or area found matching 'qzxv wvpp'. Available devices: Living Room Thermostat, Bedroom Climate",
            result);
    }

    // ── FindClimateDevicesByAreaAsync ───────────────────────────────────

    [Fact]
    public async Task FindClimateDevicesByAreaAsync_ResolvedArea_ListsItsClimateDevices()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "climate.living_room"));

        var result = await skill.FindClimateDevicesByAreaAsync("living room");

        Assert.Equal($"Found 1 climate device(s) matching 'living room':\n- {LivingRoomLine}".Replace("\n", Environment.NewLine), result);
    }

    [Fact]
    public async Task FindClimateDevicesByAreaAsync_NoAreaMatch_ListsAreasWithClimateDevices()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        var result = await skill.FindClimateDevicesByAreaAsync("garage");

        Assert.Equal(
            "No area found matching 'garage'. No matches at any level. Available areas with climate devices: Living Room",
            result);
    }

    [Fact]
    public async Task FindClimateDevicesByAreaAsync_AreaHasNoClimateDevices_SaysSo()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "light.kitchen"));

        Assert.Equal(
            "No climate devices found in the matched location for 'kitchen'.",
            await skill.FindClimateDevicesByAreaAsync("kitchen"));
    }

    // ── State and sensor reads ──────────────────────────────────────────

    [Fact]
    public async Task GetClimateStateAsync_KnownDevice_IncludesTheDevicesCapabilities()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(LivingRoomLine["Living Room Thermostat (Entity ID: climate.living_room), ".Length..], await skill.GetClimateStateAsync("climate.living_room"));
    }

    [Fact]
    public async Task GetClimateStateAsync_NumericFanModes_AreListedAsText()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "State: cool, Available Fan Modes: [4, 1, 2, 3], Temp Range: 60°-80°",
            await skill.GetClimateStateAsync("climate.bedroom"));
    }

    [Fact]
    public async Task GetClimateStateAsync_UnknownDevice_SaysNotFound()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal("Climate device 'climate.nope' not found.", await skill.GetClimateStateAsync("climate.nope"));
    }

    [Fact]
    public async Task GetAreaClimateSensorsAsync_KeepsOnlyTemperatureAndHumiditySensors()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(
            ResolutionStrategy.Area,
            "sensor.living_room_temperature", "sensor.living_room_humidity", "sensor.living_room_motion"));

        var result = await skill.GetAreaClimateSensorsAsync("living room");

        Assert.Equal(
            ("Found 2 climate sensor(s) in 'living room':\n" +
             "- Living Room Temperature: 71.5°F (Entity ID: sensor.living_room_temperature)\n" +
             "- Living Room Humidity: 41% (Entity ID: sensor.living_room_humidity)").Replace("\n", Environment.NewLine),
            result);
    }

    [Fact]
    public async Task GetAreaClimateSensorsAsync_NoneFound_IncludesTheResolutionReason()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal(
            "No temperature or humidity sensors found in 'attic'. No matches at any level",
            await skill.GetAreaClimateSensorsAsync("attic"));
    }

    [Fact]
    public async Task GetSensorStateAsync_ReportsValueUnitAndType()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "Sensor 'Living Room Temperature': 71.5°F (type: temperature)",
            await skill.GetSensorStateAsync("sensor.living_room_temperature"));
        Assert.Equal(
            "Sensor 'Living Room Humidity': 41%",
            await skill.GetSensorStateAsync("sensor.living_room_humidity"));
        Assert.Equal("Sensor 'sensor.nope' not found.", await skill.GetSensorStateAsync("sensor.nope"));
    }

    // ── Control tools ───────────────────────────────────────────────────

    private static bool HasData(ServiceCallRequest? request, string key, object expected)
        => request is not null
           && request.EntityId == "climate.living_room"
           && request.TryGetValue(key, out var value)
           && Equals(value, expected);

    private static async Task AssertServiceCallAsync(
        Func<ClimateControlSkill, Task<string>> invoke,
        string service,
        string dataKey,
        object dataValue,
        string expectedMessage)
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        var result = await invoke(skill);

        Assert.Equal(expectedMessage, result);
        A.CallTo(() => ha.CallServiceAsync(
                "climate",
                service,
                A<string?>._,
                A<ServiceCallRequest?>.That.Matches(request => HasData(request, dataKey, dataValue)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public Task SetClimateTemperatureAsync_CallsSetTemperature()
        => AssertServiceCallAsync(
            skill => skill.SetClimateTemperatureAsync("climate.living_room", 72.5),
            "set_temperature", "temperature", 72.5,
            "Set target temperature for 'climate.living_room' to 72.5°.");

    [Fact]
    public Task SetClimateHvacModeAsync_CallsSetHvacMode()
        => AssertServiceCallAsync(
            skill => skill.SetClimateHvacModeAsync("climate.living_room", "cool"),
            "set_hvac_mode", "hvac_mode", "cool",
            "Set HVAC mode for 'climate.living_room' to 'cool'.");

    [Fact]
    public Task SetClimateFanModeAsync_CallsSetFanMode()
        => AssertServiceCallAsync(
            skill => skill.SetClimateFanModeAsync("climate.living_room", "low"),
            "set_fan_mode", "fan_mode", "low",
            "Set fan mode for 'climate.living_room' to 'low'.");

    [Fact]
    public Task SetClimateHumidityAsync_CallsSetHumidity()
        => AssertServiceCallAsync(
            skill => skill.SetClimateHumidityAsync("climate.living_room", 50),
            "set_humidity", "humidity", 50,
            "Set target humidity for 'climate.living_room' to 50%.");

    [Fact]
    public Task SetClimatePresetModeAsync_CallsSetPresetMode()
        => AssertServiceCallAsync(
            skill => skill.SetClimatePresetModeAsync("climate.living_room", "sleep"),
            "set_preset_mode", "preset_mode", "sleep",
            "Set preset mode for 'climate.living_room' to 'sleep'.");

    [Fact]
    public Task SetClimateSwingModeAsync_CallsSetSwingMode()
        => AssertServiceCallAsync(
            skill => skill.SetClimateSwingModeAsync("climate.living_room", "on"),
            "set_swing_mode", "swing_mode", "on",
            "Set swing mode for 'climate.living_room' to 'on'.");

    [Fact]
    public async Task SetClimateTemperatureAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        var result = await skill.SetClimateTemperatureAsync("climate.living_room", 70);

        Assert.Equal("Unable to set temperature for 'climate.living_room'. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task SetClimateTemperatureAsync_WhenCancelled_Propagates()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => skill.SetClimateTemperatureAsync("climate.living_room", 70));
    }

    [Fact]
    public async Task GetClimateStateAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetEntityStateAsync("climate.living_room", A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));

        var result = await skill.GetClimateStateAsync("climate.living_room");

        Assert.Equal("Unable to get the state of 'climate.living_room'. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    // ── Cache ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_BuildsTheCacheOnceAndStoresItInTheDeviceCache()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = CreateEmptyDeviceCache();
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        await skill.FindClimateDeviceAsync("Living Room Thermostat");
        await skill.FindClimateDeviceAsync("Bedroom Climate");

        A.CallTo(() => ha.GetAllEntityStatesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => deviceCache.SetCachedClimateDevicesAsync(
                A<List<ClimateEntity>>.That.Matches(devices => devices.Count == 2),
                A<TimeSpan>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => deviceCache.SetEmbeddingAsync(
                "climate:climate.living_room", A<Embedding<float>>._, A<TimeSpan>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InitializeAsync_WithAFullDeviceCache_DoesNotReadAllStatesFromHomeAssistant()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var cachedDevice = new ClimateEntity
        {
            EntityId = "climate.living_room",
            FriendlyName = "Living Room Thermostat",
            HvacModes = ["cached-mode"],
            MinTemp = 55,
            MaxTemp = 85,
        };
        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedClimateDevicesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<ClimateEntity>?>([cachedDevice]));
        A.CallTo(() => deviceCache.GetEmbeddingAsync("climate:climate.living_room", A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(new Embedding<float>(new float[] { 1f })));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        var state = await skill.GetClimateStateAsync("climate.living_room");

        A.CallTo(() => ha.GetAllEntityStatesAsync(A<CancellationToken>._)).MustNotHaveHappened();
        Assert.Contains("Available HVAC Modes: [cached-mode]", state);
        Assert.EndsWith("Temp Range: 55°-85°", state);
    }

    [Fact]
    public async Task InitializeAsync_WhenTheDeviceCacheWriteFails_StillUsesTheLoadedDevices()
    {
        var real = await CreateHomeAssistantAsync();
        var deviceCache = CreateEmptyDeviceCache();
        A.CallTo(() => deviceCache.SetCachedClimateDevicesAsync(A<List<ClimateEntity>>._, A<TimeSpan>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("cache down"));
        var skill = await CreateSkillAsync(real, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        var result = await skill.FindClimateDeviceAsync("Living Room Thermostat");

        Assert.Equal($"Found climate device: {LivingRoomLine}", result);
    }

    [Fact]
    public async Task InitializeAsync_WithAnIncompleteDeviceCache_ReloadsFromHomeAssistant()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedClimateDevicesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<ClimateEntity>?>([new ClimateEntity { EntityId = "climate.living_room", FriendlyName = "Living Room Thermostat" }]));
        A.CallTo(() => deviceCache.GetEmbeddingAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(null));

        await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        A.CallTo(() => ha.GetAllEntityStatesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
