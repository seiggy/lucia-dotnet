using FakeItEasy;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Presence;

public sealed class PresenceDetectionServiceTests
{
    private readonly IHomeAssistantClient _haClient = A.Fake<IHomeAssistantClient>();
    private readonly IEntityLocationService _locationService = A.Fake<IEntityLocationService>();
    private readonly IPresenceSensorRepository _repository = A.Fake<IPresenceSensorRepository>();
    private readonly ILogger<PresenceDetectionService> _logger = A.Fake<ILogger<PresenceDetectionService>>();
    private readonly PresenceDetectionService _service;

    public PresenceDetectionServiceTests()
    {
        A.CallTo(() => _repository.GetEnabledAsync(A<CancellationToken>._)).Returns(true);
        _service = new PresenceDetectionService(_haClient, _locationService, _repository, _logger);
    }

    // -- ClassifySensor tests --

    [Fact]
    public void ClassifySensor_MmWaveTargetCount_ReturnsHighest()
    {
        var state = MakeState("sensor.bedroom_presence_target_count", "2");
        Assert.Equal(PresenceConfidence.Highest, PresenceDetectionService.ClassifySensor(state));
    }

    [Fact]
    public void ClassifySensor_MmWaveBinary_ReturnsHigh()
    {
        var state = MakeState("binary_sensor.bedroom_presence", "on");
        Assert.Equal(PresenceConfidence.High, PresenceDetectionService.ClassifySensor(state));
    }

    [Fact]
    public void ClassifySensor_MotionSensor_ReturnsMedium()
    {
        var state = MakeState("binary_sensor.living_room_motion", "on", deviceClass: "motion");
        Assert.Equal(PresenceConfidence.Medium, PresenceDetectionService.ClassifySensor(state));
    }

    [Fact]
    public void ClassifySensor_OccupancySensor_ReturnsLow()
    {
        var state = MakeState("binary_sensor.downstairs_occupancy", "on", deviceClass: "occupancy");
        Assert.Equal(PresenceConfidence.Low, PresenceDetectionService.ClassifySensor(state));
    }

    [Fact]
    public void ClassifySensor_UnrelatedEntity_ReturnsNone()
    {
        var state = MakeState("light.kitchen_lights", "on");
        Assert.Equal(PresenceConfidence.None, PresenceDetectionService.ClassifySensor(state));
    }

    [Fact]
    public void ClassifySensor_SensorPresenceNonNumeric_ReturnsNone()
    {
        var state = MakeState("sensor.bedroom_presence_status", "home");
        Assert.Equal(PresenceConfidence.None, PresenceDetectionService.ClassifySensor(state));
    }

    // -- DiscoverPresenceSensors tests --

    [Fact]
    public void DiscoverPresenceSensors_FindsAllTypes()
    {
        var states = new HomeAssistantState[]
        {
            MakeState("sensor.bedroom_presence_target_count", "1"),
            MakeState("binary_sensor.office_presence", "on"),
            MakeState("binary_sensor.kitchen_motion", "off", deviceClass: "motion"),
            MakeState("binary_sensor.garage_occupancy", "on", deviceClass: "occupancy"),
            MakeState("light.bedroom_light", "on"),
        };

        var areas = new List<AreaInfo>
        {
            new() { AreaId = "bedroom", Name = "Bedroom" },
            new() { AreaId = "office", Name = "Office" },
            new() { AreaId = "kitchen", Name = "Kitchen" },
            new() { AreaId = "garage", Name = "Garage" },
        };

        A.CallTo(() => _locationService.GetAreaForEntity("sensor.bedroom_presence_target_count"))
            .Returns(areas[0]);
        A.CallTo(() => _locationService.GetAreaForEntity("binary_sensor.office_presence"))
            .Returns(areas[1]);
        A.CallTo(() => _locationService.GetAreaForEntity("binary_sensor.kitchen_motion"))
            .Returns(areas[2]);
        A.CallTo(() => _locationService.GetAreaForEntity("binary_sensor.garage_occupancy"))
            .Returns(areas[3]);

        var discovered = _service.DiscoverPresenceSensors(states, areas);

        Assert.Equal(4, discovered.Count);
        Assert.Contains(discovered, m => m.EntityId == "sensor.bedroom_presence_target_count" && m.Confidence == PresenceConfidence.Highest);
        Assert.Contains(discovered, m => m.EntityId == "binary_sensor.office_presence" && m.Confidence == PresenceConfidence.High);
        Assert.Contains(discovered, m => m.EntityId == "binary_sensor.kitchen_motion" && m.Confidence == PresenceConfidence.Medium);
        Assert.Contains(discovered, m => m.EntityId == "binary_sensor.garage_occupancy" && m.Confidence == PresenceConfidence.Low);
    }

    [Fact]
    public void DiscoverPresenceSensors_SkipsEntitiesWithoutArea()
    {
        var states = new HomeAssistantState[]
        {
            MakeState("binary_sensor.orphan_presence", "on"),
        };

        A.CallTo(() => _locationService.GetAreaForEntity("binary_sensor.orphan_presence"))
            .Returns((AreaInfo?)null);

        var discovered = _service.DiscoverPresenceSensors(states, []);

        Assert.Empty(discovered);
    }

    // -- IsOccupiedAsync tests --

    [Fact]
    public async Task IsOccupiedAsync_ReturnsNull_WhenDisabled()
    {
        A.CallTo(() => _repository.GetEnabledAsync(A<CancellationToken>._)).Returns(false);

        var result = await _service.IsOccupiedAsync("bedroom");

        Assert.Null(result);
    }

    [Fact]
    public async Task IsOccupiedAsync_ReturnsNull_WhenNoSensorsForArea()
    {
        var result = await _service.IsOccupiedAsync("bedroom");

        Assert.Null(result);
    }

    [Fact]
    public async Task IsOccupiedAsync_ReturnsTrue_WhenHighestSensorDetectsPresence()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "sensor.bedroom_presence_target_count",
                AreaId = "bedroom",
                Confidence = PresenceConfidence.Highest
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("sensor.bedroom_presence_target_count", "2")]);

        var result = await _service.IsOccupiedAsync("bedroom");

        Assert.True(result);
    }

    [Fact]
    public async Task IsOccupiedAsync_ReturnsFalse_WhenCountIsZero()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "sensor.bedroom_presence_target_count",
                AreaId = "bedroom",
                Confidence = PresenceConfidence.Highest
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("sensor.bedroom_presence_target_count", "0")]);

        var result = await _service.IsOccupiedAsync("bedroom");

        Assert.False(result);
    }

    [Fact]
    public async Task IsOccupiedAsync_ReturnsTrue_WhenBinarySensorIsOn()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "binary_sensor.office_presence",
                AreaId = "office",
                Confidence = PresenceConfidence.High
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("binary_sensor.office_presence", "on")]);

        var result = await _service.IsOccupiedAsync("office");

        Assert.True(result);
    }

    // -- GetOccupantCountAsync tests --

    [Fact]
    public async Task GetOccupantCountAsync_ReturnsCount_FromRadarSensor()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "sensor.bedroom_presence_target_count",
                AreaId = "bedroom",
                Confidence = PresenceConfidence.Highest
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("sensor.bedroom_presence_target_count", "3")]);

        var result = await _service.GetOccupantCountAsync("bedroom");

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task GetOccupantCountAsync_ReturnsNull_WhenOnlyBinarySensors()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "binary_sensor.bedroom_presence",
                AreaId = "bedroom",
                Confidence = PresenceConfidence.High
            });

        var result = await _service.GetOccupantCountAsync("bedroom");

        Assert.Null(result);
    }

    // -- GetOccupiedAreasAsync tests --

    [Fact]
    public async Task GetOccupiedAreasAsync_ReturnsOccupiedAreas_OrderedByConfidence()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "binary_sensor.kitchen_motion",
                AreaId = "kitchen",
                AreaName = "Kitchen",
                Confidence = PresenceConfidence.Medium
            },
            new PresenceSensorMapping
            {
                EntityId = "sensor.bedroom_presence_target_count",
                AreaId = "bedroom",
                AreaName = "Bedroom",
                Confidence = PresenceConfidence.Highest
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._)).Returns(
        [
            MakeState("binary_sensor.kitchen_motion", "on"),
            MakeState("sensor.bedroom_presence_target_count", "1"),
        ]);

        A.CallTo(() => _locationService.GetAreasAsync(A<CancellationToken>._)).Returns(new List<AreaInfo>
        {
            new() { AreaId = "bedroom", Name = "Bedroom" },
            new() { AreaId = "kitchen", Name = "Kitchen" },
        });

        var results = await _service.GetOccupiedAreasAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("bedroom", results[0].AreaId);
        Assert.Equal(1, results[0].OccupantCount);
        Assert.Equal("kitchen", results[1].AreaId);
        Assert.Null(results[1].OccupantCount);
    }

    [Fact]
    public async Task GetOccupiedAreasAsync_ExcludesUnoccupiedAreas()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "binary_sensor.office_presence",
                AreaId = "office",
                AreaName = "Office",
                Confidence = PresenceConfidence.High
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("binary_sensor.office_presence", "off")]);

        A.CallTo(() => _locationService.GetAreasAsync(A<CancellationToken>._)).Returns(new List<AreaInfo>
        {
            new() { AreaId = "office", Name = "Office" },
        });

        var results = await _service.GetOccupiedAreasAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetOccupiedAreasAsync_ExcludesDisabledSensors()
    {
        await LoadMappings(
            new PresenceSensorMapping
            {
                EntityId = "binary_sensor.office_presence",
                AreaId = "office",
                AreaName = "Office",
                Confidence = PresenceConfidence.High,
                IsDisabled = true
            });

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._))
            .Returns([MakeState("binary_sensor.office_presence", "on")]);

        A.CallTo(() => _locationService.GetAreasAsync(A<CancellationToken>._)).Returns(new List<AreaInfo>
        {
            new() { AreaId = "office", Name = "Office" },
        });

        var results = await _service.GetOccupiedAreasAsync();

        Assert.Empty(results);
    }

    // -- RefreshSensorMappingsAsync tests --

    [Fact]
    public async Task RefreshSensorMappingsAsync_DiscoversSensorsAndPersists()
    {
        var states = new HomeAssistantState[]
        {
            MakeState("binary_sensor.office_presence", "on"),
        };

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._)).Returns(states);
        A.CallTo(() => _locationService.GetAreasAsync(A<CancellationToken>._)).Returns(new List<AreaInfo>
        {
            new() { AreaId = "office", Name = "Office" },
        });
        A.CallTo(() => _locationService.GetAreaForEntity("binary_sensor.office_presence"))
            .Returns(new AreaInfo { AreaId = "office", Name = "Office" });
        A.CallTo(() => _repository.GetAllMappingsAsync(A<CancellationToken>._))
            .Returns(new List<PresenceSensorMapping>
            {
                new()
                {
                    EntityId = "binary_sensor.office_presence",
                    AreaId = "office",
                    Confidence = PresenceConfidence.High
                }
            });

        await _service.RefreshSensorMappingsAsync();

        A.CallTo(() => _repository.ReplaceAutoDetectedMappingsAsync(
            A<IReadOnlyList<PresenceSensorMapping>>.That.Matches(list => list.Count == 1),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    // -- SetEnabledAsync / IsEnabledAsync tests --

    [Fact]
    public async Task SetEnabledAsync_DelegatesToRepository()
    {
        await _service.SetEnabledAsync(false);

        A.CallTo(() => _repository.SetEnabledAsync(false, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // -- Helpers --

    private async Task LoadMappings(params PresenceSensorMapping[] mappings)
    {
        A.CallTo(() => _repository.GetAllMappingsAsync(A<CancellationToken>._))
            .Returns(mappings.ToList());

        A.CallTo(() => _haClient.GetStatesAsync(A<CancellationToken>._)).Returns([]);
        A.CallTo(() => _locationService.GetAreasAsync(A<CancellationToken>._)).Returns(new List<AreaInfo>());

        await _service.RefreshSensorMappingsAsync();

        Fake.ClearRecordedCalls(_haClient);
    }

    private static HomeAssistantState MakeState(string entityId, string state, string? deviceClass = null)
    {
        var attrs = new Dictionary<string, object>();
        if (deviceClass is not null)
            attrs["device_class"] = deviceClass;

        return new HomeAssistantState
        {
            EntityId = entityId,
            State = state,
            Attributes = attrs,
            LastChanged = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
        };
    }
}
