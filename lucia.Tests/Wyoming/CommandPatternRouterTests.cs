using lucia.Agents.Abstractions;
using lucia.Tests.Helpers;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class CommandPatternRouterTests
{
    private static CommandPatternRouter CreateRouter(
        bool enabled = true,
        float threshold = 0.6f,
        bool fallbackToLlm = true,
        params CommandPattern[] patterns)
    {
        var providers = new[] { new TestRouterPatternProvider(patterns) };
        var registry = new CommandPatternRegistry(providers);
        var matcher = new CommandPatternMatcher(registry);
        var options = new TestOptionsMonitor<CommandRoutingOptions>(new CommandRoutingOptions
        {
            Enabled = enabled,
            ConfidenceThreshold = threshold,
            FallbackToLlm = fallbackToLlm,
        });
        return new CommandPatternRouter(matcher, options, NullLogger<CommandPatternRouter>.Instance);
    }

    private static CommandPattern TestLightPattern => new()
    {
        Id = "test-light",
        SkillId = "LightControlSkill",
        Action = "toggle",
        Templates = ["turn {action:on|off} [the] {entity}"],
        MinConfidence = 0.6f,
    };

    [Fact]
    public async Task Route_MatchingTranscript_ReturnsFastPath()
    {
        var router = CreateRouter(patterns: TestLightPattern);
        var result = await router.RouteAsync("turn on the kitchen lights", default);
        Assert.True(result.IsMatch);
        Assert.Equal("LightControlSkill", result.MatchedPattern!.SkillId);
    }

    [Fact]
    public async Task Route_NoMatch_ReturnsFallback()
    {
        var router = CreateRouter(patterns: TestLightPattern);
        var result = await router.RouteAsync("what is the weather today", default);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Route_Disabled_AlwaysReturnsFallback()
    {
        var router = CreateRouter(enabled: false, patterns: TestLightPattern);
        var result = await router.RouteAsync("turn on the lights", default);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Route_EmptyTranscript_ReturnsFallback()
    {
        var router = CreateRouter(patterns: TestLightPattern);
        var result = await router.RouteAsync(string.Empty, default);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Route_MatchBelowGlobalThreshold_ReturnsNoMatch()
    {
        var router = CreateRouter(threshold: 0.95f, patterns: TestLightPattern);

        var result = await router.RouteAsync("turn on the kitchen lights", default);

        Assert.False(result.IsMatch);
        Assert.Null(result.MatchedPattern);
    }

    [Fact]
    public void FallbackToLlmEnabled_ReflectsConfiguredOption()
    {
        var router = CreateRouter(fallbackToLlm: false, patterns: TestLightPattern);

        Assert.False(router.FallbackToLlmEnabled);
    }

    private sealed class TestRouterPatternProvider(CommandPattern[] patterns) : ICommandPatternProvider
    {
        private readonly CommandPattern[] _patterns = patterns;

        public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
            _patterns.Select(static p => new CommandPatternDefinition
            {
                Id = p.Id,
                SkillId = p.SkillId,
                Action = p.Action,
                Templates = p.Templates.ToArray(),
                MinConfidence = p.MinConfidence,
                Priority = p.Priority,
            }).ToList();
    }
}
