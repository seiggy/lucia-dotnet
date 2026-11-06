using lucia.AgentHost.Family;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Family;

public sealed class DevicePairingServiceTests : IDisposable
{
    private readonly SqliteStoreEnvironment _environment;
    private readonly SqliteFamilyMemberStore _members;
    private readonly SqliteDevicePairingStore _pairings;
    private readonly SqliteApiKeyService _keys;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly DevicePairingService _service;

    public DevicePairingServiceTests()
    {
        _environment = SqliteStoreEnvironment.CreateAsync().GetAwaiter().GetResult();
        _members = new SqliteFamilyMemberStore(_environment.Config);
        _pairings = new SqliteDevicePairingStore(_environment.Config);
        _keys = new SqliteApiKeyService(_environment.Config, NullLogger<SqliteApiKeyService>.Instance);
        _service = new DevicePairingService(_pairings, _members, _keys, _time);
        _members.UpsertAsync(new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child })
            .GetAwaiter().GetResult();
    }

    public void Dispose() => _environment.Dispose();

    [Fact]
    public async Task RedeemAsync_IssuesAMemberKeyLinkedToTheMemberOnce()
    {
        var created = await _service.CreateAsync("kid", lifetime: null);
        Assert.NotNull(created);
        Assert.NotEqual(created.Code, created.Pairing.CodeHash);
        Assert.Equal(_time.GetUtcNow().UtcDateTime + DevicePairingService.DefaultLifetime, created.Pairing.ExpiresAt);

        var redeemed = await _service.RedeemAsync(created.Code, "Sam's iPad");

        Assert.NotNull(redeemed);
        Assert.Equal("kid", redeemed.Member.Id);
        var entry = await _keys.ValidateKeyAsync(redeemed.ApiKey);
        Assert.NotNull(entry);
        Assert.Equal("Sam's iPad", entry.Name);
        Assert.Equal([ApiKeyScopes.Member], entry.Scopes);
        var owner = await _members.FindByIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, entry.Id);
        Assert.Equal("kid", owner?.Id);
        var pairing = Assert.Single(await _pairings.ListForMemberAsync("kid"));
        Assert.Equal(entry.Id, pairing.ApiKeyId);
        Assert.Null(await _service.RedeemAsync(created.Code, "Second device"));
    }

    [Fact]
    public async Task RedeemAsync_ExpiredOrUnknownCode_ReturnsNull()
    {
        var created = await _service.CreateAsync("kid", TimeSpan.FromMinutes(5));
        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(await _service.RedeemAsync(created!.Code, null));
        Assert.Null(await _service.RedeemAsync("not-a-code", null));
    }

    [Fact]
    public async Task RedeemAsync_MemberDeactivatedAfterPairing_IssuesNoKey()
    {
        var created = await _service.CreateAsync("kid", lifetime: null);
        var member = (await _members.GetAsync("kid"))!;
        member.IsActive = false;
        await _members.UpsertAsync(member);
        var keysBefore = (await _keys.ListKeysAsync()).Count;

        Assert.Null(await _service.RedeemAsync(created!.Code, null));
        Assert.Equal(keysBefore, (await _keys.ListKeysAsync()).Count);
    }

    [Fact]
    public async Task CreateAsync_UnknownOrInactiveMember_ReturnsNull()
    {
        Assert.Null(await _service.CreateAsync("missing", lifetime: null));
    }

    [Fact]
    public async Task CreateAsync_ClampsTheLifetime()
    {
        var created = await _service.CreateAsync("kid", TimeSpan.FromDays(30));

        Assert.Equal(_time.GetUtcNow().UtcDateTime + DevicePairingService.MaxLifetime, created!.Pairing.ExpiresAt);
    }

    [Fact]
    public async Task RedeemAsync_DefaultsAndTrimsTheDeviceName()
    {
        var first = await _service.CreateAsync("kid", lifetime: null);
        var second = await _service.CreateAsync("kid", lifetime: null);

        var unnamed = await _service.RedeemAsync(first!.Code, "  ");
        var longName = await _service.RedeemAsync(second!.Code, new string('x', 200));

        Assert.Equal("Sam's device", (await _keys.ValidateKeyAsync(unnamed!.ApiKey))!.Name);
        Assert.Equal(DevicePairingService.MaxDeviceNameLength, (await _keys.ValidateKeyAsync(longName!.ApiKey))!.Name.Length);
    }
}
