using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Services;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class ClimateCommandPatternTests
{
    [Fact]
    public void SetAreaToTemperature_MatchesClimateFastPath()
    {
        var skill = new ClimateControlSkill(
            A.Fake<IHomeAssistantClient>(),
            A.Fake<IEmbeddingProviderResolver>(),
            NullLogger<ClimateControlSkill>.Instance,
            A.Fake<IDeviceCacheService>(),
            A.Fake<IEntityLocationService>(),
            A.Fake<IHybridEntityMatcher>(),
            A.Fake<IOptionsMonitor<ClimateControlSkillOptions>>(),
            new ConfigurationBuilder().Build());
        var matcher = new CommandPatternMatcher(new CommandPatternRegistry([skill]));

        var result = matcher.Match("Set the office to 73.");

        Assert.True(result.IsMatch);
        Assert.Equal("ClimateControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal("office", result.CapturedValues!["entity"]);
        Assert.Equal("73", result.CapturedValues["value"]);
        Assert.True(result.Confidence >= result.MatchedPattern.MinConfidence);
    }
}
