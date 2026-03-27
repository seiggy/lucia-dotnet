using FakeItEasy;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests that the UseCascadingResolver feature flag correctly routes
/// DirectSkillExecutor between the new CascadingEntityResolver and the old code path.
///
/// Feature flag: IFeatureManager.IsEnabledAsync("UseCascadingResolver")
///   - ON  → DirectSkillExecutor calls ICascadingEntityResolver.Resolve()
///   - OFF → DirectSkillExecutor uses the legacy ExactMatchEntities path
///   - ON + cascade bails → returns SkillExecutionResult with bail reason
/// </summary>
public sealed class FeatureFlagTests
{
    // ── Feature flag ON → uses CascadingEntityResolver ──────────

    [Fact]
    public async Task FeatureFlagOn_DirectSkillExecutor_UsesCascadingResolver()
    {
        // Arrange — feature flag ON; cascade bails so we can verify it was called
        var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadeResolver.Resolve(
            A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = false,
                BailReason = BailReason.Ambiguous,
                Explanation = "test: verifying cascade was called"
            });

        var executor = CreateExecutor(
            featureFlagEnabled: true, cascadeResolver: cascadeResolver);
        var route = CreateLightRoute("turn on the kitchen light", "kitchen");

        // Act
        await executor.ExecuteAsync(route, CreateContext("Kitchen"));

        // Assert — CascadingEntityResolver was called
        A.CallTo(() => cascadeResolver.Resolve(
            A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── Feature flag OFF → uses old code path ───────────────────

    [Fact]
    public async Task FeatureFlagOff_DirectSkillExecutor_UsesLegacyPath()
    {
        // Arrange — feature flag OFF; cache not ready → immediate bail before cascade
        var cascadeResolver = A.Fake<ICascadingEntityResolver>();

        var executor = CreateExecutor(
            featureFlagEnabled: false,
            cascadeResolver: cascadeResolver,
            cacheReady: false);
        var route = CreateLightRoute("turn on the kitchen light", "kitchen");

        // Act
        var result = await executor.ExecuteAsync(route, CreateContext("Kitchen"));

        // Assert — CascadingEntityResolver was NOT called; legacy cache-miss bail
        Assert.False(result.Success);
        Assert.Equal("cache_miss", result.BailReason);
        A.CallTo(() => cascadeResolver.Resolve(
            A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ── Feature flag ON + cascade bails → LLM fallback ──────────

    [Fact]
    public async Task FeatureFlagOn_CascadeBails_FallsThroughToLlm()
    {
        // Arrange — cascade returns bail with Ambiguous
        var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadeResolver.Resolve(
            A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = false,
                BailReason = BailReason.Ambiguous,
                Explanation = "lamp found in 3 areas without caller context"
            });

        var executor = CreateExecutor(
            featureFlagEnabled: true, cascadeResolver: cascadeResolver);
        var route = CreateLightRoute("turn on the lamp", "lamp");

        // Act
        var result = await executor.ExecuteAsync(route, CreateContext(callerArea: null));

        // Assert — executor returns failure indicating bail
        Assert.False(result.Success);
        Assert.Contains("Ambiguous", result.BailReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Feature flag ON + CacheNotReady → bail to LLM ───────────

    [Fact]
    public async Task FeatureFlagOn_CacheNotReady_BailsToLlm()
    {
        // Arrange — cascade returns CacheNotReady
        var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        A.CallTo(() => cascadeResolver.Resolve(
            A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = false,
                BailReason = BailReason.CacheNotReady,
                Explanation = "Entity cache not loaded"
            });

        var executor = CreateExecutor(
            featureFlagEnabled: true, cascadeResolver: cascadeResolver);
        var route = CreateLightRoute("turn on the bedroom lights", "bedroom");

        // Act
        var result = await executor.ExecuteAsync(route, CreateContext("Living Room"));

        // Assert
        Assert.False(result.Success);
        Assert.Contains("CacheNotReady", result.BailReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static DirectSkillExecutor CreateExecutor(
        bool featureFlagEnabled,
        ICascadingEntityResolver? cascadeResolver = null,
        bool cacheReady = true)
    {
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.IsCacheReady).Returns(cacheReady);

        var featureManager = A.Fake<IFeatureManager>();
        A.CallTo(() => featureManager.IsEnabledAsync("UseCascadingResolver"))
            .Returns(featureFlagEnabled);

        // Service provider must resolve LightControlSkill for dispatch
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(lucia.Agents.Skills.LightControlSkill)))
            .Returns(A.Fake<lucia.Agents.Skills.LightControlSkill>());

        return new DirectSkillExecutor(
            serviceProvider,
            locationService,
            cascadeResolver ?? A.Fake<ICascadingEntityResolver>(),
            featureManager,
            A.Fake<ILogger<DirectSkillExecutor>>());
    }

    private static CommandRouteResult CreateLightRoute(string transcript, string entity) => new()
    {
        IsMatch = true,
        Confidence = 0.95f,
        NormalizedTranscript = transcript,
        MatchedPattern = new CommandPattern
        {
            Id = "light-toggle",
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["{action:on|off} [the] {entity} [lights]"]
        },
        CapturedValues = new Dictionary<string, string>
        {
            ["action"] = "on",
            ["entity"] = entity
        }
    };

    private static ConversationContext CreateContext(string? callerArea = "Living Room") => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ConversationId = "test-feature-flag",
        DeviceArea = callerArea
    };
}
