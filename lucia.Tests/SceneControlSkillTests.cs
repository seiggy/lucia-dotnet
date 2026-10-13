using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class SceneControlSkillTests
{
    private static readonly string NewLine = Environment.NewLine;

    private static async Task<FakeHomeAssistantClient> CreateHomeAssistantAsync()
    {
        var ha = new FakeHomeAssistantClient();
        await ha.SetEntityStateAsync("scene.romantic", "scening", new Dictionary<string, object> { ["friendly_name"] = "Romantic" });
        await ha.SetEntityStateAsync("scene.movie_mode", "scening", new Dictionary<string, object> { ["friendly_name"] = "Movie Mode" });
        await ha.SetEntityStateAsync("scene.no_name", "scening", new Dictionary<string, object>());
        await ha.SetEntityStateAsync("light.kitchen", "on", new Dictionary<string, object> { ["friendly_name"] = "Kitchen" });
        return ha;
    }

    private static HierarchicalSearchResult Resolved(ResolutionStrategy strategy, params (string Id, string? Name)[] entities) => new()
    {
        FloorMatches = [],
        AreaMatches = [],
        EntityMatches = [],
        ResolvedEntities = [.. entities.Select(e => new HomeAssistantEntity { EntityId = e.Id, FriendlyName = e.Name! })],
        ResolutionStrategy = strategy,
        ResolutionReason = strategy == ResolutionStrategy.None ? "No matches at any level" : "stub",
    };

    private static SceneControlSkill CreateSkill(IHomeAssistantClient ha, HierarchicalSearchResult? hierarchy = null)
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Returns(hierarchy ?? Resolved(ResolutionStrategy.None));

        return new SceneControlSkill(
            ha,
            location,
            new TestOptionsMonitor<SceneControlSkillOptions>(new SceneControlSkillOptions()),
            NullLogger<SceneControlSkill>.Instance);
    }

    [Fact]
    public void GetTools_ExposesTheThreeSceneTools_WithoutAnAsyncSuffix()
    {
        var skill = CreateSkill(new FakeHomeAssistantClient());

        Assert.Equal(
            ["ActivateScene", "FindScenesByArea", "ListScenes"],
            skill.GetTools().Select(tool => tool.Name).Order().ToArray());
    }

    // ── ListScenesAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListScenesAsync_ListsScenesByNameWithAnIdFallbackForUnnamedOnes()
    {
        var skill = CreateSkill(await CreateHomeAssistantAsync());

        Assert.Equal(
            $"Found 3 scene(s):{NewLine}- Movie Mode (scene.movie_mode){NewLine}- Romantic (scene.romantic){NewLine}- scene.no_name (scene.no_name)",
            await skill.ListScenesAsync());
    }

    [Fact]
    public async Task ListScenesAsync_NoScenes_SaysSo()
    {
        var skill = CreateSkill(new FakeHomeAssistantClient());

        Assert.Equal("No scenes found in Home Assistant.", await skill.ListScenesAsync());
    }

    [Fact]
    public async Task ListScenesAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetStatesAsync(A<CancellationToken>._)).Throws(new InvalidOperationException("secret connection string"));
        var skill = CreateSkill(ha);

        var result = await skill.ListScenesAsync();

        Assert.Equal("Unable to list scenes. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task ListScenesAsync_WhenCancelled_Propagates()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetStatesAsync(A<CancellationToken>._)).Throws(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSkill(ha).ListScenesAsync());
    }

    // ── FindScenesByAreaAsync ───────────────────────────────────────────

    [Fact]
    public async Task FindScenesByAreaAsync_ResolvedArea_ListsItsScenes()
    {
        var skill = CreateSkill(
            new FakeHomeAssistantClient(),
            Resolved(ResolutionStrategy.Area, ("scene.movie_mode", "Movie Mode"), ("scene.romantic", null)));

        Assert.Equal(
            $"Found 2 scene(s) in 'living room':{NewLine}- Movie Mode (scene.movie_mode){NewLine}- scene.romantic (scene.romantic)",
            await skill.FindScenesByAreaAsync("living room"));
    }

    [Fact]
    public async Task FindScenesByAreaAsync_NoMatch_IncludesTheResolutionReason()
    {
        var skill = CreateSkill(new FakeHomeAssistantClient());

        Assert.Equal(
            "No scenes found in area 'garage'. No matches at any level",
            await skill.FindScenesByAreaAsync("garage"));
    }

    [Fact]
    public async Task FindScenesByAreaAsync_WhenTheLookupFails_ReturnsAGenericMessage()
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = new SceneControlSkill(
            new FakeHomeAssistantClient(),
            location,
            new TestOptionsMonitor<SceneControlSkillOptions>(new SceneControlSkillOptions()),
            NullLogger<SceneControlSkill>.Instance);

        var result = await skill.FindScenesByAreaAsync("garage");

        Assert.Equal("Unable to search for scenes in 'garage'. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    // ── ActivateSceneAsync ──────────────────────────────────────────────

    private static bool IsSceneRequest(ServiceCallRequest? request, string entityId)
        => request is not null && request.EntityId == entityId && request.Count == 1;

    [Theory]
    [InlineData("scene.movie_mode", "scene.movie_mode")]
    [InlineData("Scene.Movie_Mode", "Scene.Movie_Mode")]
    [InlineData("Movie Mode", "scene.movie_mode")]
    [InlineData("romantic", "scene.romantic")]
    public async Task ActivateSceneAsync_CallsSceneTurnOnWithTheNormalizedId(string input, string expectedEntityId)
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = CreateSkill(ha);

        var result = await skill.ActivateSceneAsync(input);

        Assert.Equal($"Scene '{expectedEntityId}' activated successfully.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "scene",
                "turn_on",
                A<string?>._,
                A<ServiceCallRequest?>.That.Matches(request => IsSceneRequest(request, expectedEntityId)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ActivateSceneAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = CreateSkill(ha);

        var result = await skill.ActivateSceneAsync("scene.movie_mode");

        Assert.Equal("Unable to activate scene 'scene.movie_mode'. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task ActivateSceneAsync_WhenCancelled_Propagates()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSkill(ha).ActivateSceneAsync("scene.movie_mode"));
    }
}
