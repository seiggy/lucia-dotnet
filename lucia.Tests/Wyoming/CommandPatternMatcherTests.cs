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

    private static CommandPattern ComfortPattern => new()
    {
        Id = "climate-comfort",
        SkillId = "ClimateControlSkill",
        Action = "adjust",
        Templates =
        [
            "make [it] {action:warmer|cooler|hotter|colder} [in] [the] {area}",
            "{action:cool|warm} [off] [up] [the] {area}",
        ],
        MinConfidence = 0.85f,
    };

    private static CommandPattern SecurityStateQuery => new()
    {
        Id = "security-state",
        SkillId = "SecurityControlSkill",
        Action = "status",
        Templates =
        [
            "is [the] {entity} {state:open|closed|locked|unlocked}",
            "are [the] {entity} {state:open|closed|locked|unlocked}",
        ],
        MinConfidence = 0.6f,
        IsQuery = true,
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

    // Temporal bail signal tests

    [Theory]
    [InlineData("turn on the lights at 7pm")]
    [InlineData("turn on the lights at 10am")]
    [InlineData("turn off lights in 5 minutes")]
    [InlineData("turn on lights in 30min")]
    public void TemporalReference_BailsToLLM(string transcript)
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match(transcript);

        Assert.False(result.IsMatch, $"'{transcript}' should bail due to temporal reference");
    }

    [Theory]
    [InlineData("turn on lights in the kitchen")]
    [InlineData("turn on lights in the bedroom")]
    public void SpatialPreposition_DoesNotBail(string transcript)
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match(transcript);

        Assert.True(result.IsMatch, $"'{transcript}' should NOT bail — 'in' is spatial, not temporal");
    }

    // Non-light device bail tests

    [Theory]
    [InlineData("turn office fan on")]
    [InlineData("turn on the fan")]
    [InlineData("turn off bedroom fan")]
    [InlineData("fans off")]
    public void NonLightDevice_Fan_DoesNotMatchLightPattern(string transcript)
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match(transcript);

        Assert.False(result.IsMatch, $"'{transcript}' should NOT match LightControlSkill");
    }

    [Theory]
    [InlineData("turn off the ac")]
    [InlineData("turn on the tv")]
    [InlineData("lock the door")]
    [InlineData("turn on the speaker")]
    [InlineData("turn off the vacuum")]
    [InlineData("turn off the garage")]
    public void NonLightDevice_OtherDevices_DoNotMatchLightPattern(string transcript)
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match(transcript);

        Assert.False(result.IsMatch, $"'{transcript}' should NOT match LightControlSkill");
    }

    // Light-word exception: "garage lights" and "fan light" should still match

    [Theory]
    [InlineData("turn on the lights")]
    [InlineData("turn off kitchen lights")]
    [InlineData("turn on the bedroom light")]
    [InlineData("lights on in the kitchen")]
    [InlineData("lights on in the garage")]
    [InlineData("turn on the garage lights")]
    [InlineData("turn off the garage light")]
    [InlineData("turn on the fan light")]
    [InlineData("turn off Zach's light")]
    public void LightDevice_StillMatchesLightPattern(string transcript)
    {
        var matcher = CreateMatcher(LightPattern);

        var result = matcher.Match(transcript);

        Assert.True(result.IsMatch, $"'{transcript}' should still match LightControlSkill");
    }

    // Query phase: status questions match query patterns only

    [Fact]
    public void Match_StatusQuestion_MatchesQueryPattern()
    {
        var matcher = CreateMatcher(SecurityStateQuery);

        var result = matcher.Match("are the garage doors open");

        Assert.True(result.IsMatch, "'are the garage doors open' should reach the query pattern");
        Assert.Equal("SecurityControlSkill", result.MatchedPattern!.SkillId);
        Assert.Equal("garage doors", result.CapturedValues!["entity"]);
    }

    [Fact]
    public void Match_StatusQuestionWithQuestionMark_MatchesQueryPattern()
    {
        var matcher = CreateMatcher(SecurityStateQuery);

        var result = matcher.Match("is the front door locked?");

        Assert.True(result.IsMatch, "'is the front door locked?' must not bail on the question mark");
        Assert.Equal("front door", result.CapturedValues!["entity"]);
    }

    [Fact]
    public void Match_StatusQuestion_DoesNotMatchCommandPatterns()
    {
        var matcher = CreateMatcher(LightPattern, SecurityStateQuery);

        var result = matcher.Match("is the kitchen light on");

        Assert.False(result.IsMatch, "'is the kitchen light on' has no query pattern and must not match command patterns");
    }

    [Fact]
    public void Match_CommandUtterance_DoesNotMatchQueryPatterns()
    {
        var matcher = CreateMatcher(SecurityStateQuery);

        var result = matcher.Match("open the garage doors");

        Assert.False(result.IsMatch, "'open the garage doors' is a command and must not match query patterns");
    }

    // Comfort phrasing: cool/warm no longer bails to the LLM

    [Fact]
    public void Match_CoolOff_MatchesComfortPattern()
    {
        var matcher = CreateMatcher(ComfortPattern);

        var result = matcher.Match("cool off the master bedroom");

        Assert.True(result.IsMatch, "'cool off the master bedroom' should match (warm/cool are no longer bail tokens)");
        Assert.Equal("cool", result.CapturedValues!["action"]);
        Assert.Equal("master bedroom", result.CapturedValues["area"]);
    }

    [Fact]
    public void Match_WarmUp_MatchesComfortPattern()
    {
        var matcher = CreateMatcher(ComfortPattern);

        var result = matcher.Match("warm up the garage");

        Assert.True(result.IsMatch, "'warm up the garage' should match (warm/cool are no longer bail tokens)");
        Assert.Equal("warm", result.CapturedValues!["action"]);
        Assert.Equal("garage", result.CapturedValues["area"]);
    }

    [Fact]
    public void Match_MakeItWarmer_StillMatchesComfortPattern()
    {
        var matcher = CreateMatcher(ComfortPattern);

        var result = matcher.Match("make it warmer in the office");

        Assert.True(result.IsMatch);
        Assert.Equal("warmer", result.CapturedValues!["action"]);
        Assert.Equal("office", result.CapturedValues["area"]);
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
            IsQuery = pattern.IsQuery,
        }).ToList();
}
