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
    [Theory]
    [InlineData("Set the office to 73.", "73")]
    [InlineData("Set the office to 72.5.", "72.5")]
    [InlineData("Set the office to -5.", "-5")]
    public void SetAreaToTemperature_MatchesClimateFastPath(string input, string expectedTemperature)
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

        var result = matcher.Match(input);

        Assert.True(result.IsMatch);
        Assert.Equal("ClimateControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal("office", result.CapturedValues!["entity"]);
        Assert.Equal(expectedTemperature, result.CapturedValues["value"]);
        Assert.True(result.Confidence >= result.MatchedPattern.MinConfidence);
    }
}
