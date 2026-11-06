using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Family;

public sealed class ActionBrokerTests : IDisposable
{
    private readonly SqliteStoreEnvironment _environment;
    private readonly SqliteFamilyMemberStore _members;
    private readonly SqliteActionRecordStore _actions;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly ActionBroker _broker;
    private readonly FamilyMember _parent = new() { Id = "parent", DisplayName = "Alex", Role = FamilyRole.Adult };
    private readonly FamilyMember _child = new() { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child, GuardianIds = ["parent"] };

    public ActionBrokerTests()
    {
        _environment = SqliteStoreEnvironment.CreateAsync().GetAwaiter().GetResult();
        _members = new SqliteFamilyMemberStore(_environment.Config);
        _actions = new SqliteActionRecordStore(_environment.Config);
        _broker = new ActionBroker(_actions, _members, _time, NullLogger<ActionBroker>.Instance);
        _members.UpsertAsync(_parent).GetAwaiter().GetResult();
        _members.UpsertAsync(_child).GetAwaiter().GetResult();
    }

    public void Dispose() => _environment.Dispose();

    private static Dictionary<string, object?> Args(int temperature) => new()
    {
        ["entityId"] = "climate.bedroom",
        ["temperature"] = temperature,
    };

    [Theory]
    [InlineData(FamilyRole.Administrator, "SetClimateTemperatureAsync", ActionRule.Allow)]
    [InlineData(FamilyRole.Adult, "UnlockDoor", ActionRule.Allow)]
    [InlineData(FamilyRole.Child, "GetClimateStateAsync", ActionRule.Allow)]
    [InlineData(FamilyRole.Child, "FindLightsByArea", ActionRule.Allow)]
    [InlineData(FamilyRole.Child, "SetClimateTemperatureAsync", ActionRule.AskGuardian)]
    [InlineData(FamilyRole.Child, "UnlockDoor", ActionRule.Deny)]
    [InlineData(FamilyRole.Child, "DisarmAlarm", ActionRule.Deny)]
    [InlineData(FamilyRole.Guest, "ListShoppingItemsAsync", ActionRule.Allow)]
    [InlineData(FamilyRole.Guest, "ControlLightsAsync", ActionRule.Deny)]
    public void Evaluate_ToolRolePresets(FamilyRole role, string tool, ActionRule expected)
    {
        var member = new FamilyMember { Id = "m", DisplayName = "M", Role = role };

        Assert.Equal(expected, _broker.Evaluate(member, CapabilityKind.Tool, tool));
    }

    [Theory]
    [InlineData(FamilyRole.Child, "security-agent", ActionRule.Deny)]
    [InlineData(FamilyRole.Guest, "security-agent", ActionRule.Deny)]
    [InlineData(FamilyRole.Child, "light-agent", ActionRule.Allow)]
    [InlineData(FamilyRole.Adult, "security-agent", ActionRule.Allow)]
    public void Evaluate_AgentRolePresets(FamilyRole role, string agent, ActionRule expected)
    {
        var member = new FamilyMember { Id = "m", DisplayName = "M", Role = role };

        Assert.Equal(expected, _broker.Evaluate(member, CapabilityKind.Agent, agent));
    }

    [Fact]
    public void Evaluate_MemberRulesOverridePresetsIgnoringCaseAndAsyncSuffix()
    {
        var child = new FamilyMember
        {
            Id = "kid",
            DisplayName = "Sam",
            Role = FamilyRole.Child,
            CapabilityRules =
            [
                new CapabilityRule { Kind = CapabilityKind.Tool, CapabilityId = "controllights", Rule = ActionRule.Allow },
                new CapabilityRule { Kind = CapabilityKind.Tool, CapabilityId = "GetClimateStateAsync", Rule = ActionRule.Deny },
            ],
        };

        Assert.Equal(ActionRule.Allow, _broker.Evaluate(child, CapabilityKind.Tool, "ControlLightsAsync"));
        Assert.Equal(ActionRule.Deny, _broker.Evaluate(child, CapabilityKind.Tool, "GetClimateState"));
    }

    [Fact]
    public void RequiresMediation_OnlyForRestrictedRolesOrNonAllowRules()
    {
        Assert.False(ActionBroker.RequiresMediation(_parent));
        Assert.True(ActionBroker.RequiresMediation(_child));
        var restrictedAdult = new FamilyMember
        {
            Id = "a",
            DisplayName = "A",
            Role = FamilyRole.Adult,
            CapabilityRules = [new CapabilityRule { Kind = CapabilityKind.Agent, CapabilityId = "music-agent", Rule = ActionRule.Deny }],
        };
        Assert.True(ActionBroker.RequiresMediation(restrictedAdult));
    }

    [Fact]
    public async Task AuthorizeToolAsync_AllowedCallProceedsWithoutARecord()
    {
        var outcome = await _broker.AuthorizeToolAsync(_parent, "climate-agent", "SetClimateTemperatureAsync", Args(24));

        Assert.Equal(BrokerOutcomeKind.Proceed, outcome.Kind);
        Assert.Null(outcome.Record);
        Assert.Empty(await _actions.ListForRequesterAsync("parent", 10));
    }

    [Fact]
    public async Task AuthorizeToolAsync_AgentDenyBlocksItsTools()
    {
        var outcome = await _broker.AuthorizeToolAsync(_child, "security-agent", "GetSecurityStatus", Args(1));

        Assert.Equal(BrokerOutcomeKind.Denied, outcome.Kind);
    }

    [Fact]
    public async Task AuthorizeToolAsync_AskGuardianCreatesOneDurableRequest()
    {
        var first = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        var repeat = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));

        Assert.Equal(BrokerOutcomeKind.Pending, first.Kind);
        Assert.Equal(first.Record!.Id, repeat.Record!.Id);
        var pending = Assert.Single(await _actions.ListAwaitingDecisionAsync("parent"));
        Assert.Equal(["parent"], pending.ApproverIds);
        Assert.Equal("kid", pending.RequesterId);
        Assert.Contains("SetClimateTemperature", pending.Summary);
        Assert.Contains("Sam", pending.Summary);
    }

    [Fact]
    public async Task ApprovedRequest_RunsOnceAndOnlyForTheSameArguments()
    {
        var pending = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        var decision = await _broker.DecideAsync(pending.Record!.Id, "parent", approve: true);
        Assert.Equal(ApprovalDecisionStatus.Decided, decision.Status);

        var different = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(30));
        Assert.Equal(BrokerOutcomeKind.Pending, different.Kind);
        Assert.NotEqual(pending.Record.Id, different.Record!.Id);

        var approved = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        Assert.Equal(BrokerOutcomeKind.Proceed, approved.Kind);
        Assert.Equal(pending.Record.Id, approved.Record!.Id);
        Assert.Equal(ActionStatus.Executing, (await _actions.GetAsync(pending.Record.Id))!.Status);
        await _broker.CompleteAsync(approved.Record, succeeded: true, "done");
        Assert.Equal(ActionStatus.Succeeded, (await _actions.GetAsync(pending.Record.Id))!.Status);

        var again = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        Assert.Equal(BrokerOutcomeKind.Pending, again.Kind);
        Assert.NotEqual(pending.Record.Id, again.Record!.Id);
    }

    [Fact]
    public async Task ApprovedRequest_IsRejectedWhenPolicyNowDenies()
    {
        var pending = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        await _broker.DecideAsync(pending.Record!.Id, "parent", approve: true);
        _child.CapabilityRules = [new CapabilityRule { Kind = CapabilityKind.Tool, CapabilityId = "SetClimateTemperature", Rule = ActionRule.Deny }];

        var outcome = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));

        Assert.Equal(BrokerOutcomeKind.Denied, outcome.Kind);
        Assert.Equal(ActionStatus.Approved, (await _actions.GetAsync(pending.Record.Id))!.Status);
    }

    [Fact]
    public async Task DeniedRequest_IsNotExecutedAndAskingAgainCreatesANewRequest()
    {
        var pending = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        await _broker.DecideAsync(pending.Record!.Id, "parent", approve: false);

        var again = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));

        Assert.Equal(BrokerOutcomeKind.Pending, again.Kind);
        Assert.NotEqual(pending.Record.Id, again.Record!.Id);
        Assert.Equal(ActionStatus.Denied, (await _actions.GetAsync(pending.Record.Id))!.Status);
    }

    [Fact]
    public async Task DecideAsync_OnlyAnActiveListedApproverDecidesOnceBeforeExpiry()
    {
        var other = new FamilyMember { Id = "other", DisplayName = "Jo", Role = FamilyRole.Adult };
        await _members.UpsertAsync(other);
        var pending = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        var id = pending.Record!.Id;

        Assert.Equal(ApprovalDecisionStatus.NotFound, (await _broker.DecideAsync(id, "other", true)).Status);
        Assert.Equal(ApprovalDecisionStatus.NotFound, (await _broker.DecideAsync(id, "kid", true)).Status);
        Assert.Equal(ApprovalDecisionStatus.NotFound, (await _broker.DecideAsync("missing", "parent", true)).Status);

        _parent.IsActive = false;
        await _members.UpsertAsync(_parent);
        Assert.Equal(ApprovalDecisionStatus.NotFound, (await _broker.DecideAsync(id, "parent", true)).Status);
        _parent.IsActive = true;
        await _members.UpsertAsync(_parent);

        Assert.Equal(ApprovalDecisionStatus.Decided, (await _broker.DecideAsync(id, "parent", true)).Status);
        Assert.Equal(ApprovalDecisionStatus.AlreadyDecided, (await _broker.DecideAsync(id, "parent", false)).Status);
        Assert.Equal("parent", (await _actions.GetAsync(id))!.DecidedBy);
    }

    [Fact]
    public async Task DecideAsync_ExpiredRequestCannotBeApproved()
    {
        var pending = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));
        _time.Advance(ActionBroker.DecisionLifetime + TimeSpan.FromMinutes(1));

        Assert.Equal(ApprovalDecisionStatus.Expired, (await _broker.DecideAsync(pending.Record!.Id, "parent", true)).Status);
        Assert.Equal(ActionStatus.Expired, (await _actions.GetAsync(pending.Record.Id))!.Status);
    }

    [Fact]
    public async Task AuthorizeToolAsync_ChildWithoutAnActiveGuardianIsDenied()
    {
        _parent.IsActive = false;
        await _members.UpsertAsync(_parent);

        var outcome = await _broker.AuthorizeToolAsync(_child, "climate-agent", "SetClimateTemperatureAsync", Args(24));

        Assert.Equal(BrokerOutcomeKind.Denied, outcome.Kind);
        Assert.Empty(await _actions.ListForRequesterAsync("kid", 10));
    }
}
