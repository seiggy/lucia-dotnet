using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

/// <summary>
/// Runs transcripts against the shipped light and climate templates at the default
/// routing threshold, so template or matcher changes are checked against what users hit.
/// </summary>
public sealed class ProductionCommandPatternTests
{
    [Theory]
    // Questions and statements that happen to end in "on" or "off"
    [InlineData("can you tell me whether the office light is on")]
    [InlineData("tell me if the office lights are on")]
    [InlineData("i wonder if the office lights are on")]
    [InlineData("who left the porch light on")]
    [InlineData("the office lights are on")]
    [InlineData("the porch light should be off")]
    [InlineData("the kids turned the porch light off")]
    [InlineData("remind me to turn the porch light off")]
    // Negated commands
    [InlineData("don't turn the office lights on")]
    [InlineData("can you not turn the lights on")]
    // Conditions, exceptions and times the fast path cannot honor
    [InlineData("turn on the office lights if nobody is home")]
    [InlineData("turn off the porch light unless someone is outside")]
    [InlineData("turn off the office lights, not the lamp")]
    [InlineData("office lights on or off")]
    [InlineData("turn on the lights at sunset")]
    [InlineData("turn on the lights in the morning")]
    [InlineData("lights on in the kitchen if anyone is there")]
    [InlineData("make it warmer in the office if someone is there")]
    [InlineData("dim the lights to 20 unless i'm watching tv")]
    // Targets that need conversation context
    [InlineData("turn it off")]
    // A light is not a thermostat
    [InlineData("set the office lights to 50")]
    public async Task Route_TranscriptTheFastPathCannotActOnSafely_DefersToAgent(string transcript)
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(transcript, CancellationToken.None);

        Assert.False(result.IsMatch, $"'{transcript}' matched {result.MatchedTemplate}");
    }

    [Theory]
    [InlineData("turn on the office lights", "entity", "office lights")]
    [InlineData("turn the porch light on", "entity", "porch light")]
    [InlineData("turn the lights on", "entity", "lights")]
    [InlineData("turn all the lights off", "entity", "all the lights")]
    [InlineData("turn the office lights on", "area", "office")]
    [InlineData("can you turn on the office lights", "entity", "office lights")]
    [InlineData("could you turn the porch light off", "entity", "porch light")]
    [InlineData("turn off the lights for me", "entity", "lights")]
    [InlineData("kitchen lights off", "entity", "kitchen lights")]
    [InlineData("lights off in the kitchen", "area", "kitchen")]
    [InlineData("turn on the lights in the living room", "entity", "lights in the living room")]
    [InlineData("turn on the can lights", "entity", "can lights")]
    [InlineData("turn off Zach's light", "entity", "zach s light")]
    public async Task Route_LightCommand_CapturesOnlyTheTarget(string transcript, string captureName, string expected)
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(transcript, CancellationToken.None);

        Assert.True(result.IsMatch, $"'{transcript}' did not match");
        Assert.Equal("LightControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal(expected, result.CapturedValues![captureName]);
    }

    [Theory]
    [InlineData("dim the office lights to 20 percent", "LightControlSkill", "office lights", "20")]
    [InlineData("brighten the office lights to 80", "LightControlSkill", "office lights", "80")]
    [InlineData("set the thermostat to 70 degrees", "ClimateControlSkill", "thermostat", "70")]
    [InlineData("set the office temperature to 70 degrees", "ClimateControlSkill", "office", "70")]
    public async Task Route_ValueCommand_SeparatesTargetFromValue(
        string transcript, string skillId, string entity, string value)
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(transcript, CancellationToken.None);

        Assert.True(result.IsMatch, $"'{transcript}' did not match");
        Assert.Equal(skillId, result.MatchedPattern!.SkillId);
        Assert.Equal(entity, result.CapturedValues!["entity"]);
        Assert.Equal(value, result.CapturedValues["value"]);
    }

    private static CommandPatternRouter CreateRouter()
    {
        var light = new LightControlSkill(
            A.Fake<IHomeAssistantClient>(),
            NullLogger<LightControlSkill>.Instance,
            A.Fake<IEntityLocationService>(),
            A.Fake<IOptionsMonitor<LightControlSkillOptions>>());
        var climate = new ClimateControlSkill(
            A.Fake<IHomeAssistantClient>(),
            A.Fake<IEmbeddingProviderResolver>(),
            NullLogger<ClimateControlSkill>.Instance,
            A.Fake<IDeviceCacheService>(),
            A.Fake<IEntityLocationService>(),
            A.Fake<IHybridEntityMatcher>(),
            A.Fake<IOptionsMonitor<ClimateControlSkillOptions>>(),
            new ConfigurationBuilder().Build());
        var matcher = new CommandPatternMatcher(new CommandPatternRegistry(new ICommandPatternProvider[] { light, climate }));

        return new CommandPatternRouter(
            matcher,
            new TestOptionsMonitor<CommandRoutingOptions>(new CommandRoutingOptions()),
            NullLogger<CommandPatternRouter>.Instance);
    }
}
