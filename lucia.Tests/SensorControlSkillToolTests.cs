using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Pins the tool output of <see cref="SensorControlSkill"/> before it moves onto the shared skill helpers.
/// Error messages and cache invalidation are covered in <see cref="SensorControlSkillTests"/>.
/// </summary>
public sealed class SensorControlSkillToolTests
{
    private static readonly string NewLine = Environment.NewLine;

    private const string Temperature =
        "State: 71.5°F, Type: temperature, Area: Living Room, Name: Living Room Temperature";

    private static async Task<FakeHomeAssistantClient> CreateHomeAssistantAsync()
    {
        var ha = new FakeHomeAssistantClient();
        await ha.SetEntityStateAsync("sensor.living_room_temperature", "71.5", new Dictionary<string, object>
        {
            ["friendly_name"] = "Living Room Temperature",
            ["unit_of_measurement"] = "°F",
            ["device_class"] = "temperature",
            ["state_class"] = "measurement",
        });
        await ha.SetEntityStateAsync("sensor.kitchen_humidity", "41", new Dictionary<string, object>
        {
            ["friendly_name"] = "Kitchen Humidity",
            ["unit_of_measurement"] = "%",
            ["device_class"] = "humidity",
        });
        await ha.SetEntityStateAsync("binary_sensor.front_door", "off", new Dictionary<string, object>
        {
            ["friendly_name"] = "Front Door",
            ["device_class"] = "door",
        });
        await ha.SetEntityStateAsync("light.hallway", "on", new Dictionary<string, object>
        {
            ["friendly_name"] = "Hallway Light",
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
        A.CallTo(() => location.GetAreaForEntity("sensor.living_room_temperature"))
            .Returns(new AreaInfo { AreaId = "living_room", Name = "Living Room" });
        A.CallTo(() => location.GetAreaForEntity("sensor.kitchen_humidity"))
            .Returns(new AreaInfo { AreaId = "kitchen", Name = "Kitchen" });
        return location;
    }

    private static IEmbeddingProviderResolver CreateResolver()
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(new DeterministicEmbeddingGenerator()));
        return resolver;
    }

    private static IDeviceCacheService CreateEmptyDeviceCache()
    {
        var cache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => cache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(null));
        return cache;
    }

    private static async Task<SensorControlSkill> CreateSkillAsync(
        IHomeAssistantClient ha,
        IEntityLocationService location,
        IDeviceCacheService? deviceCache = null)
    {
        var skill = new SensorControlSkill(
            ha,
            CreateResolver(),
            NullLogger<SensorControlSkill>.Instance,
            deviceCache ?? CreateEmptyDeviceCache(),
            location,
            new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance),
            new TestOptionsMonitor<SensorControlSkillOptions>(new SensorControlSkillOptions()));

        await skill.InitializeAsync();
        return skill;
    }

    private static async Task<SensorControlSkill> CreateLoadedSkillAsync(HierarchicalSearchResult? hierarchy = null)
        => await CreateSkillAsync(
            await CreateHomeAssistantAsync(),
            CreateLocation(hierarchy ?? Resolved(ResolutionStrategy.None)));

    [Fact]
    public async Task GetTools_ExposesTheFiveSensorTools_WithoutAnAsyncSuffix()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            ["FindSensor", "FindSensorsByArea", "GetAreaSensors", "GetBinarySensorState", "GetSensorState"],
            skill.GetTools().Select(tool => tool.Name).Order().ToArray());
    }

    // ── FindSensorAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FindSensorAsync_ExactName_ReturnsThatSensorWithItsState()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            $"Found sensor: Living Room Temperature (Entity ID: sensor.living_room_temperature), {Temperature}",
            await skill.FindSensorAsync("Living Room Temperature"));
    }

    [Fact]
    public async Task FindSensorAsync_BinarySensor_UsesTheBinaryFormat()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "Found sensor: Front Door (Entity ID: binary_sensor.front_door), State: off, Type: door, Name: Front Door",
            await skill.FindSensorAsync("Front Door"));
    }

    [Fact]
    public async Task FindSensorAsync_NoSensorsInHomeAssistant_SaysSo()
    {
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        Assert.Equal("No sensors available in the system.", await skill.FindSensorAsync("temperature"));
    }

    [Fact]
    public async Task SensorLookups_WithNoSensors_StillEmitASpanWithTheElapsedTime()
    {
        using var recorder = new ActivityRecorder("Lucia.Skills.SensorControl");
        var skill = await CreateSkillAsync(new FakeHomeAssistantClient(), CreateLocation(Resolved(ResolutionStrategy.None)));

        await skill.FindSensorAsync("empty-home-span-3");
        await skill.FindSensorsByAreaAsync("empty-home-span-4");
        await skill.GetAreaSensorsAsync("empty-home-span-5");

        Assert.NotNull(recorder.WithTag("search.term", "empty-home-span-3")?.GetTagItem("elapsed_ms"));
        Assert.NotNull(recorder.WithTag("search.area", "empty-home-span-4")?.GetTagItem("elapsed_ms"));
        Assert.NotNull(recorder.WithTag("search.area", "empty-home-span-5")?.GetTagItem("elapsed_ms"));
    }

    [Fact]
    public async Task FindSensorAsync_NoNameMatchButAreaResolves_ListsTheAreasSensors()
    {
        var skill = await CreateLoadedSkillAsync(
            Resolved(ResolutionStrategy.Area, "sensor.living_room_temperature", "sensor.kitchen_humidity"));

        Assert.Equal(
            $"Found 2 sensor(s) matching 'downstairs':{NewLine}" +
            $"- Living Room Temperature (Entity ID: sensor.living_room_temperature), {Temperature}{NewLine}" +
            "- Kitchen Humidity (Entity ID: sensor.kitchen_humidity), State: 41%, Type: humidity, Area: Kitchen, Name: Kitchen Humidity",
            await skill.FindSensorAsync("downstairs"));
    }

    [Fact]
    public async Task FindSensorAsync_NothingMatches_ListsAvailableSensors()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal(
            "No sensor found matching 'qzxv wvpp'. Available sensors: Living Room Temperature, Kitchen Humidity, Front Door",
            await skill.FindSensorAsync("qzxv wvpp"));
    }

    // ── FindSensorsByAreaAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindSensorsByAreaAsync_ResolvedArea_ListsItsSensors()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "sensor.living_room_temperature"));

        Assert.Equal(
            $"Found 1 sensor(s) matching 'living room':{NewLine}" +
            $"- Living Room Temperature (Entity ID: sensor.living_room_temperature), {Temperature}",
            await skill.FindSensorsByAreaAsync("living room"));
    }

    [Fact]
    public async Task FindSensorsByAreaAsync_NoAreaMatch_ListsAreasWithSensors()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal(
            "No area found matching 'garage'. No matches at any level. Available areas with sensors: Living Room, Kitchen",
            await skill.FindSensorsByAreaAsync("garage"));
    }

    [Fact]
    public async Task FindSensorsByAreaAsync_AreaHasNoSensors_SaysSo()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, "light.hallway"));

        Assert.Equal(
            "No sensors found in the matched location for 'hallway'.",
            await skill.FindSensorsByAreaAsync("hallway"));
    }

    // ── State reads ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetSensorStateAsync_CachedSensor_IncludesTypeAreaAndName()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(Temperature, await skill.GetSensorStateAsync("sensor.living_room_temperature"));
    }

    [Fact]
    public async Task GetSensorStateAsync_SensorMissingFromTheCache_OmitsTheArea()
    {
        var ha = await CreateHomeAssistantAsync();
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)));
        await ha.SetEntityStateAsync("sensor.new_power", "5", new Dictionary<string, object>
        {
            ["unit_of_measurement"] = "W",
        });

        Assert.Equal("State: 5W, Name: sensor.new_power", await skill.GetSensorStateAsync("sensor.new_power"));
    }

    [Fact]
    public async Task GetSensorStateAsync_UnknownSensor_SaysNotFound()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal("Sensor 'sensor.nope' not found.", await skill.GetSensorStateAsync("sensor.nope"));
    }

    [Fact]
    public async Task GetBinarySensorStateAsync_ReportsStateTypeAreaAndName()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal(
            "State: off, Type: door, Area: unknown, Name: Front Door",
            await skill.GetBinarySensorStateAsync("binary_sensor.front_door"));
    }

    [Fact]
    public async Task GetBinarySensorStateAsync_UnknownSensor_SaysNotFound()
    {
        var skill = await CreateLoadedSkillAsync();

        Assert.Equal("Binary sensor 'binary_sensor.nope' not found.", await skill.GetBinarySensorStateAsync("binary_sensor.nope"));
    }

    // ── GetAreaSensorsAsync ─────────────────────────────────────────────

    private static readonly string[] AllSensors =
        ["sensor.living_room_temperature", "sensor.kitchen_humidity", "binary_sensor.front_door"];

    [Fact]
    public async Task GetAreaSensorsAsync_NoFilter_ListsEverySensorWithItsTypeAndUnit()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, AllSensors));

        Assert.Equal(
            $"Found 3 sensor(s) in 'downstairs':{NewLine}" +
            $"- Living Room Temperature: 71.5°F [sensor/temperature] (Entity ID: sensor.living_room_temperature){NewLine}" +
            $"- Kitchen Humidity: 41% [sensor/humidity] (Entity ID: sensor.kitchen_humidity){NewLine}" +
            "- Front Door: off [binary_sensor/door] (Entity ID: binary_sensor.front_door)",
            await skill.GetAreaSensorsAsync("downstairs"));
    }

    [Fact]
    public async Task GetAreaSensorsAsync_WithADeviceClass_KeepsOnlyThatClass()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, AllSensors));

        Assert.Equal(
            $"Found 1 sensor(s) in 'downstairs' (type: humidity):{NewLine}" +
            "- Kitchen Humidity: 41% [sensor/humidity] (Entity ID: sensor.kitchen_humidity)",
            await skill.GetAreaSensorsAsync("downstairs", "humidity"));
    }

    [Fact]
    public async Task GetAreaSensorsAsync_NoSensorOfThatClass_SaysSo()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.Area, AllSensors));

        Assert.Equal("No battery sensors found in 'downstairs'.", await skill.GetAreaSensorsAsync("downstairs", "battery"));
    }

    [Fact]
    public async Task GetAreaSensorsAsync_NoAreaMatch_IncludesTheResolutionReason()
    {
        var skill = await CreateLoadedSkillAsync(Resolved(ResolutionStrategy.None));

        Assert.Equal("No area found matching 'garage'. No matches at any level", await skill.GetAreaSensorsAsync("garage"));
    }

    // ── Cache ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_BuildsTheCacheOnceAndStoresItInTheDeviceCache()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = CreateEmptyDeviceCache();
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        await skill.FindSensorAsync("Living Room Temperature");
        await skill.FindSensorAsync("Front Door");

        A.CallTo(() => ha.GetAllEntityStatesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => deviceCache.SetCachedSensorsAsync(
                A<List<SensorEntity>>.That.Matches(sensors => sensors.Count == 3),
                A<TimeSpan>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InitializeAsync_WithAFullDeviceCache_DoesNotReadAllStatesFromHomeAssistant()
    {
        var real = await CreateHomeAssistantAsync();
        var ha = A.Fake<IHomeAssistantClient>(options => options.Wrapping(real));
        var deviceCache = A.Fake<IDeviceCacheService>();
        A.CallTo(() => deviceCache.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(
            [
                new SensorEntity { EntityId = "sensor.living_room_temperature", FriendlyName = "Living Room Temperature", Area = "Cached Area" },
            ]));
        A.CallTo(() => deviceCache.GetEmbeddingAsync("sensor:sensor.living_room_temperature", A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(new Embedding<float>(new float[] { 1f })));
        var skill = await CreateSkillAsync(ha, CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        var state = await skill.GetSensorStateAsync("sensor.living_room_temperature");

        A.CallTo(() => ha.GetAllEntityStatesAsync(A<CancellationToken>._)).MustNotHaveHappened();
        Assert.Contains("Area: Cached Area", state);
    }

    [Fact]
    public async Task InitializeAsync_WhenTheDeviceCacheWriteFails_StillUsesTheLoadedSensors()
    {
        var deviceCache = CreateEmptyDeviceCache();
        A.CallTo(() => deviceCache.SetCachedSensorsAsync(A<List<SensorEntity>>._, A<TimeSpan>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("cache down"));
        var skill = await CreateSkillAsync(
            await CreateHomeAssistantAsync(), CreateLocation(Resolved(ResolutionStrategy.None)), deviceCache);

        Assert.Equal(
            $"Found sensor: Living Room Temperature (Entity ID: sensor.living_room_temperature), {Temperature}",
            await skill.FindSensorAsync("Living Room Temperature"));
    }

    [Fact]
    public async Task FindSensorAsync_WhenCancelled_Propagates()
    {
        var location = CreateLocation(Resolved(ResolutionStrategy.None));
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        var skill = await CreateSkillAsync(await CreateHomeAssistantAsync(), location);

        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.FindSensorAsync("qzxv wvpp"));
    }
}
