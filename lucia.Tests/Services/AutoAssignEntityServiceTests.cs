using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Services.EntityAssignment;

namespace lucia.Tests.Services;

public sealed class AutoAssignEntityServiceTests
{
    private static readonly IEntityAssignmentRule[] s_rules =
    [
        new ExclusionPatternRule(),
        new PlatformExclusionRule(),
        new SwitchPositiveMatchRule(),
        new DomainMappingRule(),
    ];

    private static IOptimizableSkill MakeSkill(string agentId, params string[] domains)
    {
        var skill = A.Fake<IOptimizableSkill>();
        A.CallTo(() => skill.AgentId).Returns(agentId);
        A.CallTo(() => skill.EntityDomains).Returns(domains);
        return skill;
    }

    private static readonly IOptimizableSkill[] s_skills =
    [
        MakeSkill("light-agent", "light"),
        MakeSkill("climate-agent", "climate"),
        MakeSkill("fan-agent", "fan"),
        MakeSkill("music-agent", "media_player"),
        MakeSkill("scene-agent", "scene"),
    ];

    private static HomeAssistantEntity MakeEntity(string entityId, string friendlyName = "", string? platform = null) =>
        new()
        {
            EntityId = entityId,
            FriendlyName = friendlyName.Length > 0 ? friendlyName : entityId,
            Platform = platform,
        };

    private static AutoAssignEntityService CreateService(
        IEntityLocationService locationService,
        IOptimizableSkill[]? skills = null,
        IEntityAssignmentRule[]? rules = null)
    {
        return new AutoAssignEntityService(
            locationService,
            skills ?? s_skills,
            rules ?? s_rules);
    }

    private static IEntityLocationService FakeLocationService(params HomeAssistantEntity[] entities)
    {
        var service = A.Fake<IEntityLocationService>();
        A.CallTo(() => service.GetEntitiesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(entities));
        return service;
    }

    [Fact]
    public async Task NoneStrategy_SetsAllEntitiesToEmptyList()
    {
        var entities = new[]
        {
            MakeEntity("light.kitchen_lights_light", "Kitchen Lights"),
            MakeEntity("climate.thermostat", "Thermostat"),
            MakeEntity("fan.bedroom_ceiling_fan_fan", "Bedroom Fan"),
            MakeEntity("media_player.yamaha_speakers", "Yamaha Speakers"),
            MakeEntity("scene.bedroom_watch_tv", "Watch TV"),
        };
        var locationService = FakeLocationService(entities);
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.None);

        Assert.Equal(5, preview.TotalEntities);
        Assert.Equal(0, preview.AssignedCount);
        Assert.Equal(5, preview.ExcludedCount);

        foreach (var entity in entities)
        {
            Assert.True(preview.EntityAgentMap.ContainsKey(entity.EntityId));
            var agents = preview.EntityAgentMap[entity.EntityId];
            Assert.NotNull(agents);
            Assert.Empty(agents);
        }
    }

    [Fact]
    public async Task SmartStrategy_AssignsLightEntities_ToLightAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("light.kitchen_lights_light", "Kitchen Lights"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["light.kitchen_lights_light"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("light-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_AssignsClimateEntities_ToClimateAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("climate.thermostat", "Thermostat"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["climate.thermostat"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("climate-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_AssignsFanEntities_ToFanAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("fan.bedroom_ceiling_fan_fan", "Bedroom Ceiling Fan"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["fan.bedroom_ceiling_fan_fan"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("fan-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_AssignsMediaPlayerEntities_ToMusicAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("media_player.yamaha_speakers", "Yamaha Speakers"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["media_player.yamaha_speakers"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("music-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_AssignsSceneEntities_ToSceneAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("scene.bedroom_watch_tv", "Watch TV"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["scene.bedroom_watch_tv"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("scene-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_ExcludesChildLockSwitches()
    {
        var locationService = FakeLocationService(
            MakeEntity("switch.hallway_light_child_lock", "Hallway Light Child Lock"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["switch.hallway_light_child_lock"];
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task SmartStrategy_ExcludesNetworkEquipmentLeds()
    {
        var locationService = FakeLocationService(
            MakeEntity("light.upstairs_ap_2_led", "Upstairs AP 2 LED"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["light.upstairs_ap_2_led"];
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task SmartStrategy_AssignsLightSwitches_ToLightAgent()
    {
        var locationService = FakeLocationService(
            MakeEntity("switch.hallway_light_switch_1", "Hallway Light Switch"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["switch.hallway_light_switch_1"];
        Assert.NotNull(agents);
        Assert.Single(agents);
        Assert.Equal("light-agent", agents[0]);
    }

    [Fact]
    public async Task SmartStrategy_ExcludesUnmatchedSwitches()
    {
        var locationService = FakeLocationService(
            MakeEntity("switch.coffee_pot_switch_1", "Coffee Pot Switch"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["switch.coffee_pot_switch_1"];
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task SmartStrategy_ExcludesSensorEntities()
    {
        var locationService = FakeLocationService(
            MakeEntity("sensor.temperature", "Temperature"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["sensor.temperature"];
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task SmartStrategy_ExcludesUnifiPlatformEntities()
    {
        var locationService = FakeLocationService(
            MakeEntity("light.some_ap_thing", "Some AP Thing", platform: "unifi"));
        var sut = CreateService(locationService);

        var preview = await sut.PreviewAsync(AutoAssignStrategy.Smart);

        var agents = preview.EntityAgentMap["light.some_ap_thing"];
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task Preview_DoesNotCallSetEntityAgents()
    {
        var locationService = FakeLocationService(
            MakeEntity("light.kitchen_lights_light", "Kitchen Lights"));
        var sut = CreateService(locationService);

        await sut.PreviewAsync(AutoAssignStrategy.Smart);

        A.CallTo(() => locationService.SetEntityAgentsAsync(
            A<Dictionary<string, List<string>?>>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Apply_CallsSetEntityAgents()
    {
        var locationService = FakeLocationService(
            MakeEntity("light.kitchen_lights_light", "Kitchen Lights"));
        var sut = CreateService(locationService);

        await sut.ApplyAsync(AutoAssignStrategy.Smart);

        A.CallTo(() => locationService.SetEntityAgentsAsync(
            A<Dictionary<string, List<string>?>>.That.Matches(d => d.ContainsKey("light.kitchen_lights_light")),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
