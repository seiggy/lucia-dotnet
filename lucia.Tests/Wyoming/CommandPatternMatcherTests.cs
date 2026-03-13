using lucia.Agents.Abstractions;
using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

public sealed class CommandPatternMatcherTests
{
    private static CommandPatternMatcher CreateMatcher(params CommandPattern[] patterns)
    {
        var providers = new[] { new TestPatternProvider(patterns) };
        var registry = new CommandPatternRegistry(providers);
        return new CommandPatternMatcher(registry);
    }

    private static CommandPattern LightPattern => new()
    {
        Id = "light-on-off",
        SkillId = "LightControlSkill",
        Action = "toggle",
        Templates =
        [
            "turn {action:on|off} [the] {entity}",
            "{action:on|off} [the] {entity}",
            "lights {action:on|off} [in] [the] {area}",
        ],
        MinConfidence = 0.6f,
    };

    private static CommandPattern ClimatePattern => new()
    {
        Id = "climate-set",
        SkillId = "ClimateControlSkill",
        Action = "set_temperature",
        Templates = ["set [the] thermostat to {value}"],
        MinConfidence = 0.6f,
    };

    [Fact]
    public void ExactMatch_TurnOnLights()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn on the lights");

        Assert.True(result.IsMatch);
        Assert.Equal("LightControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal("on", result.CapturedValues!["action"]);
    }

    [Fact]
    public void Match_WithoutOptionalWord()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn off lights");

        Assert.True(result.IsMatch);
        Assert.Equal("off", result.CapturedValues!["action"]);
    }

    [Fact]
    public void Match_WithFillerWords()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("um please turn on the kitchen lights");

        Assert.True(result.IsMatch);
        Assert.Equal("kitchen lights", result.CapturedValues!["entity"]);
    }

    [Fact]
    public void NoMatch_UnrelatedText()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("what is the weather today");

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Match_ConstrainedCapture_RejectsInvalidValues()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn purple the lights");

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Match_MultiplePatterns_ReturnsBestMatch()
    {
        var matcher = CreateMatcher(LightPattern, ClimatePattern);

        var result = matcher.Match("set the thermostat to 72");

        Assert.True(result.IsMatch);
        Assert.Equal("ClimateControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal("72", result.CapturedValues!["value"]);
    }

    [Fact]
    public void EmptyTranscript_ReturnsNoMatch()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("");

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Match_CapturesEntityName()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn on the kitchen lights");

        Assert.True(result.IsMatch);
        Assert.NotNull(result.CapturedValues);
        Assert.Equal("kitchen lights", result.CapturedValues["entity"]);
    }

    [Fact]
    public void Match_AreaCapture()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("lights on in the kitchen");

        Assert.True(result.IsMatch);
        Assert.NotNull(result.CapturedValues);
        Assert.Equal("kitchen", result.CapturedValues["area"]);
    }

    [Fact]
    public void Match_ConfidenceAboveThreshold()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn on the lights");

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= LightPattern.MinConfidence);
    }

    [Fact]
    public void Match_Duration_IsTracked()
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match("turn on the lights");

        Assert.True(result.MatchDuration > TimeSpan.Zero);
    }
}

file sealed class TestPatternProvider(CommandPattern[] patterns) : ICommandPatternProvider
{
    private readonly CommandPattern[] _patterns = patterns;

    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
        _patterns.Select(static pattern => new CommandPatternDefinition
        {
            Id = pattern.Id,
            SkillId = pattern.SkillId,
            Action = pattern.Action,
            Templates = pattern.Templates.ToArray(),
            MinConfidence = pattern.MinConfidence,
            Priority = pattern.Priority,
        }).ToList();
}
