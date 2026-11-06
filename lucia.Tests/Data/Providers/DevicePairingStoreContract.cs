using lucia.Agents.Abstractions;
using lucia.Agents.Family;

namespace lucia.Tests.Data.Providers;

public abstract class DevicePairingStoreContract
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    protected abstract Task<StoreScope<IDevicePairingStore>> CreateAsync();

    private static DevicePairing Pairing(string id, string codeHash, DateTime expiresAt) => new()
    {
        Id = id,
        MemberId = "kid",
        CodeHash = codeHash,
        CreatedAt = Now.AddMinutes(-1),
        ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task TryRedeemAsync_RedeemsAValidCodeOnce()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Pairing("p1", "hash-1", Now.AddMinutes(10)));

        var redeemed = await scope.Store.TryRedeemAsync("hash-1", Now);

        Assert.NotNull(redeemed);
        Assert.Equal("kid", redeemed.MemberId);
        Assert.Equal(Now, redeemed.RedeemedAt);
        Assert.Null(await scope.Store.TryRedeemAsync("hash-1", Now));
    }

    [Fact]
    public async Task TryRedeemAsync_RejectsUnknownAndExpiredCodes()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Pairing("p1", "hash-1", Now.AddSeconds(-1)));

        Assert.Null(await scope.Store.TryRedeemAsync("hash-1", Now));
        Assert.Null(await scope.Store.TryRedeemAsync("unknown", Now));
    }

    [Fact]
    public async Task TryRedeemAsync_ConcurrentRedemptionsSucceedOnce()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Pairing("p1", "hash-1", Now.AddMinutes(10)));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => scope.Store.TryRedeemAsync("hash-1", Now)));

        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task SetApiKeyAsync_IsVisibleInTheMembersPairings()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Pairing("p1", "hash-1", Now.AddMinutes(10)));
        await scope.Store.TryRedeemAsync("hash-1", Now);

        await scope.Store.SetApiKeyAsync("p1", "key-1");

        var pairing = Assert.Single(await scope.Store.ListForMemberAsync("kid"));
        Assert.Equal("key-1", pairing.ApiKeyId);
        Assert.Empty(await scope.Store.ListForMemberAsync("someone-else"));
    }

    [Fact]
    public async Task DeleteAsync_CancelsAPairing()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Pairing("p1", "hash-1", Now.AddMinutes(10)));

        Assert.True(await scope.Store.DeleteAsync("p1"));
        Assert.False(await scope.Store.DeleteAsync("p1"));
        Assert.Null(await scope.Store.TryRedeemAsync("hash-1", Now));
    }
}
