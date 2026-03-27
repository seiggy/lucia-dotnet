using FakeItEasy;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Abstractions;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests that the UseCascadingResolver feature flag correctly routes
/// DirectSkillExecutor between the new CascadingEntityResolver and the old code path.
///
/// Expected feature flag: CommandRoutingOptions.UseCascadingResolver (bool)
///   - ON  → DirectSkillExecutor calls ICascadingEntityResolver.Resolve()
///   - OFF → DirectSkillExecutor calls the legacy ExactMatchEntities / SearchHierarchyAsync path
///   - ON + cascade bails → falls through to LLM orchestrator
/// </summary>
public sealed class FeatureFlagTests
{
    // ── Feature flag ON → uses CascadingEntityResolver ──────────

    [Fact(Skip = "Pending CascadingEntityResolver integration into DirectSkillExecutor by Parker")]
    public async Task FeatureFlagOn_DirectSkillExecutor_UsesCascadingResolver()
    {
        // Arrange — CommandRoutingOptions.UseCascadingResolver = true
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.IsCacheReady).Returns(true);
        //
        // var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        // A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions
        // {
        //     UseCascadingResolver = true
        // });
        //
        // var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        // A.CallTo(() => cascadeResolver.Resolve(
        //     A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
        //     .Returns(new CascadeResult
        //     {
        //         IsResolved = true,
        //         ResolvedArea = "Kitchen",
        //         ResolvedEntityIds = ["light.kitchen_main"]
        //     });
        //
        // var executor = CreateExecutor(locationService, routingOptions, cascadeResolver);
        // var route = CreateLightRoute("turn on the kitchen light", "kitchen");
        //
        // Act
        // var result = await executor.ExecuteAsync(route, CreateContext("Kitchen"));
        //
        // Assert — CascadingEntityResolver was called
        // Assert.True(result.Success);
        // A.CallTo(() => cascadeResolver.Resolve(
        //     A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
        //     .MustHaveHappenedOnceExactly();
    }

    // ── Feature flag OFF → uses old code path ───────────────────

    [Fact(Skip = "Pending CascadingEntityResolver integration into DirectSkillExecutor by Parker")]
    public async Task FeatureFlagOff_DirectSkillExecutor_UsesLegacyPath()
    {
        // Arrange — CommandRoutingOptions.UseCascadingResolver = false (or not set)
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.IsCacheReady).Returns(true);
        //
        // var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        // A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions
        // {
        //     UseCascadingResolver = false
        // });
        //
        // var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        // var executor = CreateExecutor(locationService, routingOptions, cascadeResolver);
        // var route = CreateLightRoute("turn on the kitchen light", "kitchen");
        //
        // Act
        // var result = await executor.ExecuteAsync(route, CreateContext("Kitchen"));
        //
        // Assert — CascadingEntityResolver was NOT called; old path used
        // A.CallTo(() => cascadeResolver.Resolve(
        //     A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
        //     .MustNotHaveHappened();
    }

    // ── Feature flag ON + cascade bails → LLM fallback ──────────

    [Fact(Skip = "Pending CascadingEntityResolver integration into DirectSkillExecutor by Parker")]
    public async Task FeatureFlagOn_CascadeBails_FallsThroughToLlm()
    {
        // Arrange — cascade returns IsResolved=false with BailReason=Ambiguous
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.IsCacheReady).Returns(true);
        //
        // var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        // A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions
        // {
        //     UseCascadingResolver = true
        // });
        //
        // var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        // A.CallTo(() => cascadeResolver.Resolve(
        //     A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
        //     .Returns(new CascadeResult
        //     {
        //         IsResolved = false,
        //         BailReason = BailReason.Ambiguous,
        //         Explanation = "lamp found in 3 areas without caller context"
        //     });
        //
        // var executor = CreateExecutor(locationService, routingOptions, cascadeResolver);
        // var route = CreateLightRoute("turn on the lamp", "lamp");
        //
        // Act
        // var result = await executor.ExecuteAsync(route, CreateContext(callerArea: null));
        //
        // Assert — executor returns failure indicating LLM fallback needed
        // Assert.False(result.Success);
        // Assert.Contains("bail", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Feature flag ON + CacheNotReady → bail to LLM ───────────

    [Fact(Skip = "Pending CascadingEntityResolver integration into DirectSkillExecutor by Parker")]
    public async Task FeatureFlagOn_CacheNotReady_BailsToLlm()
    {
        // Arrange — cache not loaded, cascade returns CacheNotReady
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.IsCacheReady).Returns(false);
        //
        // var routingOptions = A.Fake<IOptionsMonitor<CommandRoutingOptions>>();
        // A.CallTo(() => routingOptions.CurrentValue).Returns(new CommandRoutingOptions
        // {
        //     UseCascadingResolver = true
        // });
        //
        // var cascadeResolver = A.Fake<ICascadingEntityResolver>();
        // A.CallTo(() => cascadeResolver.Resolve(
        //     A<string>._, A<string?>._, A<string?>._, A<IReadOnlyList<string>>._, A<CancellationToken>._))
        //     .Returns(new CascadeResult
        //     {
        //         IsResolved = false,
        //         BailReason = BailReason.CacheNotReady,
        //         Explanation = "Entity cache not loaded"
        //     });
        //
        // var executor = CreateExecutor(locationService, routingOptions, cascadeResolver);
        // var route = CreateLightRoute("turn on the bedroom lights", "bedroom");
        //
        // Act
        // var result = await executor.ExecuteAsync(route, CreateContext("Living Room"));
        //
        // Assert
        // Assert.False(result.Success);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ConversationContext CreateContext(string? callerArea = "Living Room") => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ConversationId = "test-feature-flag",
        DeviceArea = callerArea
    };
}
