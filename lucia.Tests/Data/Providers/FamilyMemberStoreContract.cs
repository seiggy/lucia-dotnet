using lucia.Agents.Abstractions;
using lucia.Agents.Family;

namespace lucia.Tests.Data.Providers;

public abstract class FamilyMemberStoreContract
{
    protected abstract Task<StoreScope<IFamilyMemberStore>> CreateAsync();

    private static FamilyMember Member(string id, FamilyRole role = FamilyRole.Adult) => new()
    {
        Id = id,
        DisplayName = $"Member {id}",
        Role = role,
    };

    [Fact]
    public async Task UpsertAsync_RoundTripsPermissionsAndLlmControls()
    {
        await using var scope = await CreateAsync();
        var member = Member("kid", FamilyRole.Child);
        member.GuardianIds = ["parent"];
        member.BirthYear = 2016;
        member.CapabilityRules =
        [
            new CapabilityRule { Kind = CapabilityKind.Agent, CapabilityId = "climate-agent", Rule = ActionRule.AskGuardian },
        ];
        member.LlmControls = new MemberLlmControls
        {
            AllowedProviderIds = ["local-ollama"],
            PreferredProviderId = "local-ollama",
            DailyBudgetUsd = 0.25m,
            BlockedTopics = ["violence"],
            QuietHours = new QuietHoursWindow { Start = new TimeOnly(21, 0), End = new TimeOnly(7, 0), TimeZoneId = "America/New_York" },
        };

        await scope.Store.UpsertAsync(member);
        var loaded = await scope.Store.GetAsync("kid");

        Assert.NotNull(loaded);
        Assert.Equal(FamilyRole.Child, loaded.Role);
        Assert.Equal(["parent"], loaded.GuardianIds);
        Assert.Equal(2016, loaded.BirthYear);
        var rule = Assert.Single(loaded.CapabilityRules);
        Assert.Equal(ActionRule.AskGuardian, rule.Rule);
        Assert.Equal(0.25m, loaded.LlmControls.DailyBudgetUsd);
        Assert.Equal(["local-ollama"], loaded.LlmControls.AllowedProviderIds);
        Assert.Equal(new TimeOnly(21, 0), loaded.LlmControls.QuietHours!.Start);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesAndListReturnsEveryMember()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAsync(Member("a"));
        await scope.Store.UpsertAsync(Member("b"));
        var renamed = Member("a");
        renamed.DisplayName = "Alex";
        await scope.Store.UpsertAsync(renamed);

        var members = await scope.Store.ListAsync();

        Assert.Equal(2, members.Count);
        Assert.Equal("Alex", members.Single(m => m.Id == "a").DisplayName);
    }

    [Fact]
    public async Task GetAsync_UnknownMember_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetAsync("missing"));
    }

    [Fact]
    public async Task LinkIdentityAsync_ResolvesMemberAndRejectsSecondOwner()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAsync(Member("a"));
        await scope.Store.UpsertAsync(Member("b"));
        var link = new MemberIdentityLink
        {
            MemberId = "a",
            Kind = MemberIdentityKind.Oidc,
            Issuer = "https://id.example",
            Subject = "sub-1",
        };

        Assert.True(await scope.Store.LinkIdentityAsync(link));
        Assert.False(await scope.Store.LinkIdentityAsync(new MemberIdentityLink
        {
            MemberId = "b",
            Kind = MemberIdentityKind.Oidc,
            Issuer = "https://id.example",
            Subject = "sub-1",
        }));

        var found = await scope.Store.FindByIdentityAsync(MemberIdentityKind.Oidc, "https://id.example", "sub-1");
        Assert.Equal("a", found?.Id);
        Assert.Null(await scope.Store.FindByIdentityAsync(MemberIdentityKind.Oidc, "https://other.example", "sub-1"));
        Assert.Null(await scope.Store.FindByIdentityAsync(MemberIdentityKind.ApiKey, "https://id.example", "sub-1"));
        Assert.Single(await scope.Store.ListIdentitiesAsync("a"));
        Assert.Empty(await scope.Store.ListIdentitiesAsync("b"));
    }

    [Fact]
    public async Task UnlinkIdentityAsync_RemovesOnlyThatIdentity()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAsync(Member("a"));
        await scope.Store.LinkIdentityAsync(new MemberIdentityLink { MemberId = "a", Kind = MemberIdentityKind.ApiKey, Subject = "key-1" });
        await scope.Store.LinkIdentityAsync(new MemberIdentityLink { MemberId = "a", Kind = MemberIdentityKind.SpeakerProfile, Subject = "voice-1" });

        Assert.True(await scope.Store.UnlinkIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, "key-1"));
        Assert.False(await scope.Store.UnlinkIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, "key-1"));

        Assert.Null(await scope.Store.FindByIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, "key-1"));
        Assert.Equal("a", (await scope.Store.FindByIdentityAsync(MemberIdentityKind.SpeakerProfile, string.Empty, "voice-1"))?.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMemberAndItsIdentities()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAsync(Member("a"));
        await scope.Store.LinkIdentityAsync(new MemberIdentityLink { MemberId = "a", Kind = MemberIdentityKind.ApiKey, Subject = "key-1" });

        Assert.True(await scope.Store.DeleteAsync("a"));
        Assert.False(await scope.Store.DeleteAsync("a"));

        Assert.Null(await scope.Store.GetAsync("a"));
        Assert.Null(await scope.Store.FindByIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, "key-1"));
        Assert.Empty(await scope.Store.ListIdentitiesAsync("a"));
    }
}
