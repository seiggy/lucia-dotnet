using FakeItEasy;
using lucia.AgentHost.Family;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Family;

public sealed class FamilyMemberServiceTests : IDisposable
{
    private readonly SqliteStoreEnvironment _environment;
    private readonly SqliteFamilyMemberStore _members;
    private readonly SqliteApiKeyService _keys;
    private readonly SqliteLlmUsageStore _usage;
    private readonly IModelProviderRepository _providers = A.Fake<IModelProviderRepository>();
    private readonly ISpeakerProfileStore _speakers = A.Fake<ISpeakerProfileStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly FamilyMemberService _service;

    public FamilyMemberServiceTests()
    {
        _environment = SqliteStoreEnvironment.CreateAsync().GetAwaiter().GetResult();
        _members = new SqliteFamilyMemberStore(_environment.Config);
        _keys = new SqliteApiKeyService(_environment.Config, NullLogger<SqliteApiKeyService>.Instance);
        _usage = new SqliteLlmUsageStore(_environment.Config);
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        A.CallTo(() => _providers.GetProviderAsync(A<string>._, A<CancellationToken>._)).Returns((ModelProvider?)null);
        A.CallTo(() => _providers.GetProviderAsync("local", A<CancellationToken>._))
            .Returns(new ModelProvider { Id = "local", Name = "Local" });
        A.CallTo(() => _providers.GetProviderAsync("cloud", A<CancellationToken>._))
            .Returns(new ModelProvider { Id = "cloud", Name = "Cloud" });
        A.CallTo(() => _speakers.GetAsync(A<string>._, A<CancellationToken>._)).Returns((SpeakerProfile?)null);
        A.CallTo(() => _speakers.GetAsync("voice-1", A<CancellationToken>._))
            .Returns(new SpeakerProfile { Id = "voice-1", Name = "Sam", IsAuthorized = true });
        _service = new FamilyMemberService(_members, _keys, _usage, _providers, _speakers, _time);
    }

    public void Dispose() => _environment.Dispose();

    private static FamilyMemberRequest Request(string name, FamilyRole role, params string[] guardians) =>
        new(name, role, [.. guardians], BirthYear: null, IsActive: null, AccessExpiresAt: null);

    private async Task<string> CreateAsync(string name, FamilyRole role, params string[] guardians)
    {
        var result = await _service.CreateAsync(Request(name, role, guardians));
        Assert.Equal(FamilyOperationStatus.Ok, result.Status);
        return result.Value!.Id;
    }

    [Fact]
    public async Task CreateAsync_AdultAndGuardedChild_Succeed()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);
        var child = await CreateAsync("Sam", FamilyRole.Child, parent);

        var loaded = await _members.GetAsync(child);
        Assert.Equal([parent], loaded!.GuardianIds);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, loaded.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_IsInvalid(string name)
    {
        var result = await _service.CreateAsync(Request(name, FamilyRole.Adult));

        Assert.Equal(FamilyOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task CreateAsync_ChildWithoutGuardian_IsInvalid()
    {
        var result = await _service.CreateAsync(Request("Sam", FamilyRole.Child));

        Assert.Equal(FamilyOperationStatus.Invalid, result.Status);
        Assert.Contains("guardian", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_GuardianMustBeAnActiveAdultOrAdministrator()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);
        var sibling = await CreateAsync("Jo", FamilyRole.Child, parent);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.CreateAsync(Request("Sam", FamilyRole.Child, sibling))).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.CreateAsync(Request("Sam", FamilyRole.Child, "missing"))).Status);

        var inactive = (await _members.GetAsync(parent))!;
        inactive.IsActive = false;
        await _members.UpsertAsync(inactive);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.CreateAsync(Request("Sam", FamilyRole.Child, parent))).Status);
    }

    [Fact]
    public async Task UpdateAsync_CannotBeOwnGuardian()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);

        var result = await _service.UpdateAsync(parent, Request("Alex", FamilyRole.Adult, parent));

        Assert.Equal(FamilyOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_GuardianCannotBeDemotedToChildOrGuest()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);
        await CreateAsync("Sam", FamilyRole.Child, parent);
        var other = await CreateAsync("Jordan", FamilyRole.Adult);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.UpdateAsync(parent, Request("Alex", FamilyRole.Guest, other))).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.UpdateAsync(parent, Request("Alex", FamilyRole.Administrator))).Status);
    }

    [Fact]
    public async Task UpdateAsync_UnknownMember_IsNotFound()
    {
        Assert.Equal(FamilyOperationStatus.NotFound, (await _service.UpdateAsync("missing", Request("X", FamilyRole.Adult))).Status);
    }

    [Fact]
    public async Task DeleteAsync_RefusesToLeaveAChildWithoutAGuardian()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);
        var child = await CreateAsync("Sam", FamilyRole.Child, parent);

        Assert.Equal(FamilyOperationStatus.Conflict, (await _service.DeleteAsync(parent)).Status);

        var second = await CreateAsync("Jordan", FamilyRole.Adult);
        await _service.UpdateAsync(child, Request("Sam", FamilyRole.Child, parent, second));
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.DeleteAsync(parent)).Status);
        Assert.Equal([second], (await _members.GetAsync(child))!.GuardianIds);
    }

    [Fact]
    public async Task DeleteAsync_RevokesTheMembersDeviceKeys()
    {
        await _keys.CreateKeyAsync("portal");
        var kid = await CreateAsync("Jordan", FamilyRole.Adult);
        var device = await _keys.CreateMemberKeyAsync("phone");
        await _members.LinkIdentityAsync(new MemberIdentityLink { MemberId = kid, Kind = MemberIdentityKind.ApiKey, Subject = device.Id });

        Assert.Equal(FamilyOperationStatus.Ok, (await _service.DeleteAsync(kid)).Status);

        Assert.Null(await _keys.ValidateKeyAsync(device.Key));
        Assert.Null(await _members.GetAsync(kid));
    }

    [Fact]
    public async Task SetCapabilityRulesAsync_RejectsDuplicatesAndBlankIds()
    {
        var adult = await CreateAsync("Alex", FamilyRole.Adult);
        CapabilityRule Rule(string id, ActionRule rule) => new() { Kind = CapabilityKind.Agent, CapabilityId = id, Rule = rule };

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetCapabilityRulesAsync(adult, [Rule("climate", ActionRule.Allow), Rule("CLIMATE", ActionRule.Deny)])).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetCapabilityRulesAsync(adult, [Rule(" ", ActionRule.Deny)])).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.SetCapabilityRulesAsync(adult, [Rule("climate", ActionRule.AskGuardian)])).Status);
        Assert.Equal(ActionRule.AskGuardian, Assert.Single((await _members.GetAsync(adult))!.CapabilityRules).Rule);
    }

    [Fact]
    public async Task SetLlmControlsAsync_ValidatesProvidersBudgetsAndQuietHours()
    {
        var adult = await CreateAsync("Alex", FamilyRole.Adult);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetLlmControlsAsync(adult, new MemberLlmControls { AllowedProviderIds = ["missing"] })).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetLlmControlsAsync(adult, new MemberLlmControls { AllowedProviderIds = ["local"], PreferredProviderId = "cloud" })).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetLlmControlsAsync(adult, new MemberLlmControls { DailyBudgetUsd = -1 })).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetLlmControlsAsync(adult, new MemberLlmControls { QuietHours = new QuietHoursWindow { Start = new(21, 0), End = new(7, 0), TimeZoneId = "Not/AZone" } })).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.SetLlmControlsAsync(adult, new MemberLlmControls { QuietHours = new QuietHoursWindow { Start = new(21, 0), End = new(21, 0) } })).Status);

        var valid = new MemberLlmControls
        {
            AllowedProviderIds = ["local"],
            PreferredProviderId = "local",
            DailyBudgetUsd = 0.5m,
            BlockedTopics = ["  violence  ", ""],
            QuietHours = new QuietHoursWindow { Start = new(21, 0), End = new(7, 0), TimeZoneId = EasternTimeZone.Id },
        };
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.SetLlmControlsAsync(adult, valid)).Status);
        Assert.Equal(["violence"], (await _members.GetAsync(adult))!.LlmControls.BlockedTopics);
    }

    [Fact]
    public async Task LinkIdentityAsync_SpeakerProfileMustExistAndBeUnclaimed()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);
        var other = await CreateAsync("Jordan", FamilyRole.Adult);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.LinkIdentityAsync(parent, new LinkIdentityRequest(MemberIdentityKind.SpeakerProfile, null, "unknown", null))).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.LinkIdentityAsync(parent, new LinkIdentityRequest(MemberIdentityKind.SpeakerProfile, null, "voice-1", null))).Status);
        Assert.Equal(FamilyOperationStatus.Conflict, (await _service.LinkIdentityAsync(other, new LinkIdentityRequest(MemberIdentityKind.SpeakerProfile, null, "voice-1", null))).Status);
    }

    [Fact]
    public async Task LinkIdentityAsync_OidcNeedsIssuerAndSubject()
    {
        var parent = await CreateAsync("Alex", FamilyRole.Adult);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.LinkIdentityAsync(parent, new LinkIdentityRequest(MemberIdentityKind.Oidc, null, "sub-1", null))).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.LinkIdentityAsync(parent, new LinkIdentityRequest(MemberIdentityKind.Oidc, "https://id.example", "sub-1", "Alex (Entra)"))).Status);
        Assert.Equal(parent, (await _members.FindByIdentityAsync(MemberIdentityKind.Oidc, "https://id.example", "sub-1"))?.Id);
    }

    [Fact]
    public async Task LinkIdentityAsync_FullAccessKeysLinkOnlyToAdministrators()
    {
        var full = await _keys.CreateKeyAsync("portal");
        var adult = await CreateAsync("Alex", FamilyRole.Adult);
        var owner = await CreateAsync("Owner", FamilyRole.Administrator);

        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.LinkIdentityAsync(adult, new LinkIdentityRequest(MemberIdentityKind.ApiKey, null, full.Id, null))).Status);
        Assert.Equal(FamilyOperationStatus.Invalid, (await _service.LinkIdentityAsync(owner, new LinkIdentityRequest(MemberIdentityKind.ApiKey, null, "missing-key", null))).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.LinkIdentityAsync(owner, new LinkIdentityRequest(MemberIdentityKind.ApiKey, null, full.Id, null))).Status);
    }

    [Fact]
    public async Task UnlinkIdentityAsync_DeviceKeyIsRevokedAndForeignLinksAreNotFound()
    {
        await _keys.CreateKeyAsync("portal");
        var adult = await CreateAsync("Alex", FamilyRole.Adult);
        var other = await CreateAsync("Jordan", FamilyRole.Adult);
        var device = await _keys.CreateMemberKeyAsync("phone");
        await _members.LinkIdentityAsync(new MemberIdentityLink { MemberId = adult, Kind = MemberIdentityKind.ApiKey, Subject = device.Id });

        Assert.Equal(FamilyOperationStatus.NotFound, (await _service.UnlinkIdentityAsync(other, MemberIdentityKind.ApiKey, null, device.Id)).Status);
        Assert.Equal(FamilyOperationStatus.Ok, (await _service.UnlinkIdentityAsync(adult, MemberIdentityKind.ApiKey, null, device.Id)).Status);

        Assert.Null(await _keys.ValidateKeyAsync(device.Key));
        Assert.Empty(await _members.ListIdentitiesAsync(adult));
    }

    [Fact]
    public async Task GetUsageAsync_SplitsTodayAndThisMonth()
    {
        var adult = await CreateAsync("Alex", FamilyRole.Adult);
        LlmUsageRecord Usage(string id, DateTime at, decimal cost) => new()
        {
            Id = id, MemberId = adult, Timestamp = at, ProviderId = "local", InputTokens = 10, OutputTokens = 5, CostUsd = cost,
        };
        await _usage.RecordAsync(Usage("last-month", new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc), 9m));
        await _usage.RecordAsync(Usage("earlier", new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc), 0.2m));
        await _usage.RecordAsync(Usage("today", new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc), 0.1m));

        var result = await _service.GetUsageAsync(adult, recentLimit: 10);

        Assert.Equal(FamilyOperationStatus.Ok, result.Status);
        Assert.Equal(0.1m, result.Value!.Today.CostUsd);
        Assert.Equal(0.3m, result.Value.Month.CostUsd);
        Assert.Equal(3, result.Value.Recent.Count);
    }
}
