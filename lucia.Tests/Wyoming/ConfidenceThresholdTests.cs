using lucia.Agents.Abstractions;
using lucia.Tests.Helpers;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class ConfidenceThresholdTests
{
    [Fact]
    public async Task Router_MatchBelowGlobalThreshold_ReturnsNoMatch()
    {
        var router = CreateRouter(
            threshold: 0.95f,
            fallbackToLlm: true,
            patterns: [CreateLightPattern(minConfidence: 0.6f)]);

        var result = await router.RouteAsync("turn on the kitchen lights", CancellationToken.None);

        Assert.False(result.IsMatch);
        Assert.Null(result.MatchedPattern);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task Router_MatchAboveGlobalThreshold_ReturnsMatch()
    {
        var router = CreateRouter(
            threshold: 0.8f,
            fallbackToLlm: true,
            patterns: [CreateLightPattern(minConfidence: 0.6f)]);

        var result = await router.RouteAsync("turn on the kitchen lights", CancellationToken.None);

        Assert.True(result.IsMatch);
        Assert.NotNull(result.MatchedPattern);
        Assert.True(result.Confidence >= 0.8f);
    }

    [Fact]
    public async Task Router_FallbackToLlmDisabled_NoFallback()
    {
        var router = CreateRouter(
            threshold: 0.95f,
            fallbackToLlm: false,
            patterns: [CreateLightPattern(minConfidence: 0.6f)]);

        var result = await router.RouteAsync("turn on the kitchen lights", CancellationToken.None);

        Assert.False(router.FallbackToLlmEnabled);
        Assert.False(result.IsMatch);
        Assert.Null(result.MatchedPattern);
    }

    private static CommandPatternRouter CreateRouter(
        float threshold,
        bool fallbackToLlm,
        IReadOnlyList<CommandPattern> patterns)
    {
        var registry = new CommandPatternRegistry([new TestRouterPatternProvider(patterns)]);
        var matcher = new CommandPatternMatcher(registry);

        return new CommandPatternRouter(
            matcher,
            new TestOptionsMonitor<CommandRoutingOptions>(
                new CommandRoutingOptions
                {
                    Enabled = true,
                    ConfidenceThreshold = threshold,
                    FallbackToLlm = fallbackToLlm,
                }),
            NullLogger<CommandPatternRouter>.Instance);
    }

    private static CommandPattern CreateLightPattern(float minConfidence)
    {
        return new CommandPattern
        {
            Id = "test-light",
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["turn {action:on|off} [the] {entity}"],
            MinConfidence = minConfidence,
        };
    }

    private sealed class TestRouterPatternProvider(IReadOnlyList<CommandPattern> patterns) : ICommandPatternProvider
    {
        public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
            patterns.Select(static pattern => new CommandPatternDefinition
            {
                Id = pattern.Id,
                SkillId = pattern.SkillId,
                Action = pattern.Action,
                Templates = pattern.Templates.ToArray(),
                MinConfidence = pattern.MinConfidence,
                Priority = pattern.Priority,
            }).ToList();
    }
}
