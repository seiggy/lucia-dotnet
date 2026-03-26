using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services.EntityAssignment;

namespace lucia.Tests.Services.EntityAssignment;

public sealed class ExclusionPatternRuleTests
{
    private static readonly Dictionary<string, List<string>> s_emptyMap = new();

    private static HomeAssistantEntity MakeEntity(string entityId, string friendlyName = "", string? platform = null) =>
        new()
        {
            EntityId = entityId,
            FriendlyName = friendlyName.Length > 0 ? friendlyName : entityId,
            Platform = platform,
        };

    [Theory]
    [InlineData("switch.hallway_light_child_lock")]
    [InlineData("switch.bathroom_light_child_lock")]
    [InlineData("switch.back_porch_light_child_lock")]
    [InlineData("switch.air_conditioner_child_lock")]
    public void Excludes_ChildLock_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("light.upstairs_ap_2_led")]
    [InlineData("light.usw_flex_mini_office_led")]
    [InlineData("light.office_ap_led")]
    [InlineData("light.ont_power_outlet_led")]
    [InlineData("light.downstairs_ap_led")]
    public void Excludes_NetworkEquipment_Led_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("light.satellite1_91b604_led_ring")]
    [InlineData("light.satellite1_8f0768_led_ring")]
    public void Excludes_Satellite_LedRing_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.satellite1_8f0768_wake_sound")]
    [InlineData("switch.satellite1_8f0768_mute_microphones")]
    [InlineData("switch.satellite1_8f0768_beta_firmware")]
    [InlineData("switch.satellite1_91b604_bluetooth")]
    [InlineData("switch.satellite1_91b604_multi_target_tracking")]
    [InlineData("switch.bedroom_satellite1_snapcast")]
    public void Excludes_Satellite_Setting_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.garage_chime_extender")]
    [InlineData("switch.backyard_chime_extender")]
    public void Excludes_ChimeExtender_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.garage_privacy_mode")]
    [InlineData("switch.downstairs_privacy_mode")]
    public void Excludes_PrivacyMode_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.doorbell_deter_mode")]
    [InlineData("switch.backyard_deter_mode")]
    public void Excludes_DeterMode_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public void Excludes_EmergencyHeat_Entity()
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity("switch.thermostat_emergency_heat");

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.server_rack_ups_switch_1")]
    [InlineData("switch.office_ups_switch_1")]
    public void Excludes_Ups_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("switch.air_conditioner_panel_sound")]
    [InlineData("switch.air_conditioner_display_auto_off")]
    public void Excludes_ApplianceConfig_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out var agents);

        Assert.True(result);
        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Theory]
    [InlineData("light.kitchen_lights_light")]
    [InlineData("light.driveway_light")]
    [InlineData("light.bedroom_ceiling_fan_yellow_light")]
    [InlineData("switch.hallway_light_switch_1")]
    [InlineData("switch.coffee_pot_switch_1")]
    [InlineData("climate.thermostat")]
    [InlineData("fan.bedroom_ceiling_fan_fan")]
    [InlineData("media_player.yamaha_speakers")]
    [InlineData("scene.bedroom_watch_tv")]
    public void DoesNotExclude_Legitimate_Entities(string entityId)
    {
        var rule = new ExclusionPatternRule();
        var entity = MakeEntity(entityId);

        var result = rule.TryEvaluate(entity, s_emptyMap, out _);

        Assert.False(result);
    }
}
