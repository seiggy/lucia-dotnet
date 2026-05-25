using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class SecurityControlSkillTests
{
    [Fact]
    public async Task ArmAlarm_UsesDefaultAlarmCode_WhenCodeIsNotProvided()
    {
        var homeAssistantClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistantClient.CallServiceAsync(
                "alarm_control_panel",
                "alarm_arm_away",
                null,
                A<ServiceCallRequest>.That.Matches(request => HasEntityCode(request, "alarm_control_panel.home_alarm", "2468")),
                A<CancellationToken>._))
            .Returns(Task.FromResult(Array.Empty<object>()));

        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                "downstairs",
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>.That.Matches(domains => domains != null && domains.Contains("alarm_control_panel")),
                A<CancellationToken>._))
            .Returns(Task.FromResult(CreateSearchResult(new HomeAssistantEntity
            {
                EntityId = "alarm_control_panel.home_alarm",
                FriendlyName = "Home Alarm",
                AreaId = "downstairs",
            })));

        var skill = new SecurityControlSkill(
            homeAssistantClient,
            NullLogger<SecurityControlSkill>.Instance,
            locationService,
            new TestOptionsMonitor<SecurityControlSkillOptions>(new SecurityControlSkillOptions
            {
                DefaultAlarmCode = "2468",
            }));

        var result = await skill.ArmAlarm("downstairs", null);

        Assert.Contains("Home Alarm", result, StringComparison.Ordinal);
        A.CallTo(() => homeAssistantClient.CallServiceAsync(
                "alarm_control_panel",
                "alarm_arm_away",
                null,
                A<ServiceCallRequest>.That.Matches(request => HasEntityCode(request, "alarm_control_panel.home_alarm", "2468")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnlockDoor_DoesNotUseDefaultAlarmCode_WhenLockCodeIsNotProvided()
    {
        var homeAssistantClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistantClient.CallServiceAsync(
                "lock",
                "unlock",
                A<string?>._,
                A<ServiceCallRequest>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Array.Empty<object>()));

        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                "entryway",
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>.That.Matches(domains => domains != null && domains.Contains("lock")),
                A<CancellationToken>._))
            .Returns(Task.FromResult(CreateSearchResult(new HomeAssistantEntity
            {
                EntityId = "lock.front_door",
                FriendlyName = "Front Door",
                AreaId = "entryway",
            })));

        var skill = new SecurityControlSkill(
            homeAssistantClient,
            NullLogger<SecurityControlSkill>.Instance,
            locationService,
            new TestOptionsMonitor<SecurityControlSkillOptions>(new SecurityControlSkillOptions
            {
                DefaultAlarmCode = "2468",
            }));

        var result = await skill.UnlockDoor("entryway", null, null);

        Assert.Contains("Front Door", result, StringComparison.Ordinal);
        A.CallTo(() => homeAssistantClient.CallServiceAsync(
                "lock",
                "unlock",
                A<string?>._,
                A<ServiceCallRequest>.That.Matches(request => HasEntityWithoutCode(request, "lock.front_door")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => homeAssistantClient.CallServiceAsync(
                "lock",
                "unlock",
                A<string?>._,
                A<ServiceCallRequest>.That.Matches(request => HasEntityCode(request, "lock.front_door", "2468")),
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task GetSecurityStatus_ReturnsAlarmAndLockStates_ForRequestedArea()
    {
        var homeAssistantClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistantClient.GetEntityStateAsync("alarm_control_panel.home_alarm", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(new HomeAssistantState
            {
                EntityId = "alarm_control_panel.home_alarm",
                State = "armed_away",
                Attributes = new Dictionary<string, object>
                {
                    ["friendly_name"] = "Home Alarm",
                },
            }));
        A.CallTo(() => homeAssistantClient.GetEntityStateAsync("lock.front_door", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(new HomeAssistantState
            {
                EntityId = "lock.front_door",
                State = "locked",
                Attributes = new Dictionary<string, object>
                {
                    ["friendly_name"] = "Front Door",
                },
            }));

        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.SearchHierarchyAsync(
                "entryway",
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(CreateSearchResult(
                new HomeAssistantEntity
                {
                    EntityId = "alarm_control_panel.home_alarm",
                    FriendlyName = "Home Alarm",
                    AreaId = "entryway",
                },
                new HomeAssistantEntity
                {
                    EntityId = "lock.front_door",
                    FriendlyName = "Front Door",
                    AreaId = "entryway",
                })));

        var skill = new SecurityControlSkill(
            homeAssistantClient,
            NullLogger<SecurityControlSkill>.Instance,
            locationService,
            new TestOptionsMonitor<SecurityControlSkillOptions>(new SecurityControlSkillOptions()));

        var result = await skill.GetSecurityStatus("entryway");

        Assert.Contains("Home Alarm", result, StringComparison.Ordinal);
        Assert.Contains("armed away", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Front Door", result, StringComparison.Ordinal);
        Assert.Contains("locked", result, StringComparison.OrdinalIgnoreCase);
    }

    private static HierarchicalSearchResult CreateSearchResult(params HomeAssistantEntity[] entities)
    {
        return new HierarchicalSearchResult
        {
            AreaMatches = [],
            EntityMatches = [],
            FloorMatches = [],
            ResolvedEntities = entities,
            ResolutionStrategy = entities.Length == 0 ? ResolutionStrategy.None : ResolutionStrategy.Area,
            ResolutionReason = entities.Length == 0 ? "No matches." : "Area match.",
        };
    }

    private static bool HasEntityCode(ServiceCallRequest request, string entityId, string expectedCode)
    {
        if (!string.Equals(request.EntityId, entityId, StringComparison.Ordinal))
        {
            return false;
        }

        return request.TryGetValue("code", out var code) &&
            string.Equals(code?.ToString(), expectedCode, StringComparison.Ordinal);
    }

    private static bool HasEntityWithoutCode(ServiceCallRequest request, string entityId)
    {
        return string.Equals(request.EntityId, entityId, StringComparison.Ordinal) &&
            !request.TryGetValue("code", out _);
    }
}
