using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Pins <see cref="LightControlSkill"/> tool behaviour with a faked location service. The end-to-end flow
/// against a real location service is covered by <c>FindLightSkillEvalTests</c>.
/// </summary>
public sealed class LightControlSkillToolTests
{
    private static readonly string NewLine = Environment.NewLine;

    private static HomeAssistantEntity Entity(string id, string? name, HashSet<string>? includeForAgent = null) => new()
    {
        EntityId = id,
        FriendlyName = name!,
        IncludeForAgent = includeForAgent,
    };

    private static HierarchicalSearchResult Resolved(params HomeAssistantEntity[] entities) => new()
    {
        FloorMatches = [],
        AreaMatches = [],
        EntityMatches = [],
        ResolvedEntities = entities,
        ResolutionStrategy = entities.Length == 0 ? ResolutionStrategy.None : ResolutionStrategy.Area,
        ResolutionReason = "stub",
    };

    private static IEntityLocationService CreateLocation(Dictionary<string, HierarchicalSearchResult> byTerm)
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .ReturnsLazily(call => Task.FromResult(
                byTerm.TryGetValue(call.GetArgument<string>(0)!, out var result) ? result : Resolved()));
        return location;
    }

    private static LightControlSkill CreateSkill(IHomeAssistantClient ha, IEntityLocationService location, string agentId = "light-agent")
        => new(
            ha,
            NullLogger<LightControlSkill>.Instance,
            location,
            new TestOptionsMonitor<LightControlSkillOptions>(new LightControlSkillOptions()))
        {
            AgentId = agentId,
        };

    private static async Task<FakeHomeAssistantClient> CreateHomeAssistantAsync()
    {
        var ha = new FakeHomeAssistantClient();
        await ha.SetEntityStateAsync("light.kitchen_ceiling", "on", new Dictionary<string, object>
        {
            ["brightness"] = 128,
            ["color_temp"] = 370,
        });
        await ha.SetEntityStateAsync("light.kitchen_island", "off", new Dictionary<string, object>());
        await ha.SetEntityStateAsync("switch.porch", "on", new Dictionary<string, object>());
        return ha;
    }

    [Fact]
    public void GetTools_ExposesTheTwoLightTools_WithoutAnAsyncSuffix()
    {
        var skill = CreateSkill(new FakeHomeAssistantClient(), CreateLocation([]));

        Assert.Equal(["ControlLights", "GetLightsState"], skill.GetTools().Select(tool => tool.Name).Order().ToArray());
    }

    // ── GetLightsStateAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetLightsStateAsync_ListsEachLightWithItsStateBrightnessAndColorTemp()
    {
        var skill = CreateSkill(
            await CreateHomeAssistantAsync(),
            CreateLocation(new()
            {
                ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling"), Entity("light.kitchen_island", "Kitchen Island")),
            }));

        Assert.Equal(
            $"Found 2 light(s):{NewLine}" +
            $"- Kitchen Ceiling (light.kitchen_ceiling): on at 50%, color temp 370{NewLine}" +
            "- Kitchen Island (light.kitchen_island): off",
            await skill.GetLightsStateAsync(["kitchen"]));
    }

    [Fact]
    public async Task GetLightsStateAsync_SeveralTerms_DeduplicatesEntities()
    {
        var ceiling = Entity("light.kitchen_ceiling", "Kitchen Ceiling");
        var skill = CreateSkill(
            await CreateHomeAssistantAsync(),
            CreateLocation(new() { ["kitchen"] = Resolved(ceiling), ["ceiling"] = Resolved(ceiling) }));

        var result = await skill.GetLightsStateAsync(["kitchen", "ceiling"]);

        Assert.StartsWith("Found 1 light(s):", result);
    }

    [Fact]
    public async Task GetLightsStateAsync_HidesEntitiesTheAgentMayNotSee()
    {
        var skill = CreateSkill(
            await CreateHomeAssistantAsync(),
            CreateLocation(new()
            {
                ["kitchen"] = Resolved(
                    Entity("light.kitchen_ceiling", "Kitchen Ceiling", ["other-agent"]),
                    Entity("light.kitchen_island", "Kitchen Island", ["light-agent"])),
            }));

        Assert.Equal(
            $"Found 1 light(s):{NewLine}- Kitchen Island (light.kitchen_island): off",
            await skill.GetLightsStateAsync(["kitchen"]));
    }

    [Fact]
    public async Task GetLightsStateAsync_NothingResolves_NamesTheTerms()
    {
        var skill = CreateSkill(await CreateHomeAssistantAsync(), CreateLocation([]));

        Assert.Equal("No lights found matching: attic, garage", await skill.GetLightsStateAsync(["attic", "garage"]));
    }

    [Fact]
    public async Task GetLightsStateAsync_WhenTheLookupFails_ReturnsAGenericMessage()
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = CreateSkill(new FakeHomeAssistantClient(), location);

        var result = await skill.GetLightsStateAsync(["kitchen"]);

        Assert.Equal("Unable to get the light states. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task GetLightsStateAsync_WhenCancelled_Propagates()
    {
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateSkill(new FakeHomeAssistantClient(), location).GetLightsStateAsync(["kitchen"]));
    }

    // ── ControlLightsAsync ──────────────────────────────────────────────

    private static bool Matches(ServiceCallRequest? request, string entityId, int? brightness = null, string? color = null)
        => request is not null
           && Equals(request["entity_id"], entityId)
           && (brightness is null ? !request.ContainsKey("brightness") : Equals(request["brightness"], brightness))
           && (color is null ? !request.ContainsKey("color_name") : Equals(request["color_name"], color));

    [Fact]
    public async Task ControlLightsAsync_Off_CallsTurnOff()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = CreateSkill(ha, CreateLocation(new() { ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling")) }));

        var result = await skill.ControlLightsAsync(["kitchen"], "OFF");

        Assert.Equal("'Kitchen Ceiling' turned off.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "light", "turn_off", A<string?>._, A<ServiceCallRequest?>.That.Matches(r => Matches(r, "light.kitchen_ceiling")), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ControlLightsAsync_OnWithBrightnessAndColor_ConvertsBrightnessToTheHomeAssistantScale()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = CreateSkill(ha, CreateLocation(new() { ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling")) }));

        var result = await skill.ControlLightsAsync(["kitchen"], "on", brightness: 50, color: "red");

        Assert.Equal("'Kitchen Ceiling' turned on at 50% with red color.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "light", "turn_on", A<string?>._, A<ServiceCallRequest?>.That.Matches(r => Matches(r, "light.kitchen_ceiling", 128, "red")), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ControlLightsAsync_Switch_UsesTheSwitchDomainAndIgnoresBrightness()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = CreateSkill(ha, CreateLocation(new() { ["porch"] = Resolved(Entity("switch.porch", "Porch")) }));

        var result = await skill.ControlLightsAsync(["porch"], "on", brightness: 50, color: "red");

        Assert.Equal("'Porch' turned on.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "switch", "turn_on", A<string?>._, A<ServiceCallRequest?>.That.Matches(r => Matches(r, "switch.porch")), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ControlLightsAsync_InvalidState_IsRejectedBeforeAnyServiceCall()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        var skill = CreateSkill(ha, CreateLocation(new() { ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling")) }));

        Assert.Equal("Invalid state 'banana'. Use 'on' or 'off'.", await skill.ControlLightsAsync(["kitchen"], "banana"));
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ControlLightsAsync_NothingResolves_NamesTheTerms()
    {
        var skill = CreateSkill(A.Fake<IHomeAssistantClient>(), CreateLocation([]));

        Assert.Equal("No lights found matching: attic", await skill.ControlLightsAsync(["attic"], "on"));
    }

    [Fact]
    public async Task ControlLightsAsync_PreResolved_UsesTheCachedEntityOrFallsBackToTheHomeAssistantName()
    {
        var ha = await CreateHomeAssistantAsync();
        await ha.SetEntityStateAsync("light.new_lamp", "off", new Dictionary<string, object> { ["friendly_name"] = "New Lamp" });
        var location = A.Fake<IEntityLocationService>();
        A.CallTo(() => location.ExactMatchEntities("light.kitchen_ceiling", A<IReadOnlyList<string>?>._))
            .Returns([Entity("light.kitchen_ceiling", "Kitchen Ceiling")]);
        A.CallTo(() => location.ExactMatchEntities("light.new_lamp", A<IReadOnlyList<string>?>._))
            .Returns(Array.Empty<HomeAssistantEntity>());
        var skill = CreateSkill(ha, location);

        var result = await skill.ControlLightsAsync(["light.kitchen_ceiling", "light.new_lamp"], "off", preResolved: true);

        Assert.Equal($"'Kitchen Ceiling' turned off.{NewLine}'New Lamp' turned off.", result);
    }

    [Fact]
    public async Task ControlLightsAsync_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("secret connection string"));
        var skill = CreateSkill(ha, CreateLocation(new() { ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling")) }));

        var result = await skill.ControlLightsAsync(["kitchen"], "on");

        Assert.Equal("Unable to control the lights. Please try again.", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public async Task ControlLightsAsync_WhenCancelled_Propagates()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        var skill = CreateSkill(ha, CreateLocation(new() { ["kitchen"] = Resolved(Entity("light.kitchen_ceiling", "Kitchen Ceiling")) }));

        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.ControlLightsAsync(["kitchen"], "on"));
    }
}
