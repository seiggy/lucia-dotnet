using lucia.Agents.Abstractions;
using lucia.Agents.Auth;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IApiKeyService"/> provider must share. Concurrency and
/// transaction edge cases stay in the provider-specific test classes.
/// </summary>
public abstract class ApiKeyServiceContract
{
    protected abstract Task<StoreScope<IApiKeyService>> CreateAsync();

    [Fact]
    public async Task CreateKeyAsync_ReturnsPrefixedKeyWithIdAndName()
    {
        await using var scope = await CreateAsync();

        var created = await scope.Store.CreateKeyAsync("test-key");

        Assert.StartsWith(AuthOptions.KeyPrefix, created.Key);
        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("test-key", created.Name);
    }

    [Fact]
    public async Task ValidateKeyAsync_ValidKey_ReturnsEntry()
    {
        await using var scope = await CreateAsync();
        var created = await scope.Store.CreateKeyAsync("validate-key");

        var entry = await scope.Store.ValidateKeyAsync(created.Key);

        Assert.NotNull(entry);
        Assert.Equal(created.Id, entry.Id);
        Assert.Equal("validate-key", entry.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lk_not-a-real-key")]
    public async Task ValidateKeyAsync_UnknownOrEmptyKey_ReturnsNull(string key)
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("existing");

        Assert.Null(await scope.Store.ValidateKeyAsync(key));
    }

    [Fact]
    public async Task ValidateKeyAsync_DoesNotUpdateLastUsedAt()
    {
        await using var scope = await CreateAsync();
        var created = await scope.Store.CreateKeyAsync("read-only-validate");

        await scope.Store.ValidateKeyAsync(created.Key);

        var summary = await scope.Store.GetKeyAsync(created.Id);
        Assert.NotNull(summary);
        Assert.Null(summary.LastUsedAt);
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Home Assistant")]
    public async Task CreateKeyAsync_DoesNotGrantAdministratorScopeFromName(string name)
    {
        await using var scope = await CreateAsync();

        var created = await scope.Store.CreateKeyAsync(name);

        var entry = await scope.Store.ValidateKeyAsync(created.Key);
        Assert.NotNull(entry);
        Assert.DoesNotContain(AuthOptions.AdministratorScope, entry.Scopes);
    }

    [Fact]
    public async Task CreateAdministratorKeyAsync_GrantsAdministratorScope()
    {
        await using var scope = await CreateAsync();

        var created = await scope.Store.CreateAdministratorKeyAsync("Dashboard");

        var entry = await scope.Store.ValidateKeyAsync(created.Key);
        Assert.NotNull(entry);
        Assert.Contains(AuthOptions.AdministratorScope, entry.Scopes);
    }

    [Fact]
    public async Task CreateAdministratorKeyIfNoneAsync_ExistingAdministrator_ReturnsNull()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAdministratorKeyAsync("Dashboard");

        Assert.Null(await scope.Store.CreateAdministratorKeyIfNoneAsync("Dashboard"));
    }

    [Fact]
    public async Task CreateKeyFromPlaintextAsync_DuplicateName_ReturnsNull()
    {
        await using var scope = await CreateAsync();
        Assert.NotNull(await scope.Store.CreateKeyFromPlaintextAsync("seeded", "lk_seed-one-0123456789"));

        Assert.Null(await scope.Store.CreateKeyFromPlaintextAsync("seeded", "lk_seed-two-0123456789"));
    }

    [Fact]
    public async Task RevokeKeyAsync_RevokesKeyAndRejectsItAfterwards()
    {
        await using var scope = await CreateAsync();
        var keep = await scope.Store.CreateKeyAsync("keep");
        var drop = await scope.Store.CreateKeyAsync("drop");

        Assert.True(await scope.Store.RevokeKeyAsync(drop.Id));

        Assert.Null(await scope.Store.ValidateKeyAsync(drop.Key));
        Assert.NotNull(await scope.Store.ValidateKeyAsync(keep.Key));
        var summary = await scope.Store.GetKeyAsync(drop.Id);
        Assert.NotNull(summary);
        Assert.True(summary.IsRevoked);
        Assert.NotNull(summary.RevokedAt);
    }

    [Fact]
    public async Task RevokeKeyAsync_UnknownKey_ReturnsFalse()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("only");

        Assert.False(await scope.Store.RevokeKeyAsync("does-not-exist"));
    }

    [Fact]
    public async Task RevokeKeyAsync_LastActiveKey_Throws()
    {
        await using var scope = await CreateAsync();
        var only = await scope.Store.CreateKeyAsync("only");

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RevokeKeyAsync(only.Id));

        Assert.NotNull(await scope.Store.ValidateKeyAsync(only.Key));
    }

    [Fact]
    public async Task RegenerateKeyAsync_RevokesOldKeyAndIssuesNewOneWithSameName()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("other");
        var old = await scope.Store.CreateKeyAsync("rotating");

        var replacement = await scope.Store.RegenerateKeyAsync(old.Id);

        Assert.Equal("rotating", replacement.Name);
        Assert.NotEqual(old.Key, replacement.Key);
        Assert.Null(await scope.Store.ValidateKeyAsync(old.Key));
        Assert.NotNull(await scope.Store.ValidateKeyAsync(replacement.Key));
    }

    [Fact]
    public async Task ListKeysAsync_ReturnsActiveAndRevokedWithoutSecrets()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("a");
        var b = await scope.Store.CreateKeyAsync("b");
        await scope.Store.CreateKeyAsync("c");
        await scope.Store.RevokeKeyAsync(b.Id);

        var keys = await scope.Store.ListKeysAsync();

        Assert.Equal(3, keys.Count);
        Assert.Single(keys, key => key.IsRevoked);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key.KeyPrefix)));
    }

    [Fact]
    public async Task HasAnyKeysAsync_ReflectsWhetherAKeyExists()
    {
        await using var scope = await CreateAsync();
        Assert.False(await scope.Store.HasAnyKeysAsync());

        await scope.Store.CreateKeyAsync("first");

        Assert.True(await scope.Store.HasAnyKeysAsync());
    }

    [Fact]
    public async Task GetActiveKeyCountAsync_CountsOnlyNonRevokedKeys()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("a");
        var b = await scope.Store.CreateKeyAsync("b");
        await scope.Store.CreateKeyAsync("c");
        await scope.Store.RevokeKeyAsync(b.Id);

        Assert.Equal(2, await scope.Store.GetActiveKeyCountAsync());
    }

    [Fact]
    public async Task OverrideKeyFromPlaintextAsync_FirstCall_CreatesWithoutRevoking()
    {
        await using var scope = await CreateAsync();

        var (created, revoked) = await scope.Store.OverrideKeyFromPlaintextAsync("seeded", "lk_override-one-0123456789");

        Assert.NotNull(created);
        Assert.Equal(0, revoked);
        Assert.NotNull(await scope.Store.ValidateKeyAsync("lk_override-one-0123456789"));
    }

    [Fact]
    public async Task OverrideKeyFromPlaintextAsync_SamePlaintext_IsNoOp()
    {
        await using var scope = await CreateAsync();
        await scope.Store.OverrideKeyFromPlaintextAsync("seeded", "lk_override-one-0123456789");

        var (created, revoked) = await scope.Store.OverrideKeyFromPlaintextAsync("seeded", "lk_override-one-0123456789");

        Assert.Null(created);
        Assert.Equal(0, revoked);
        Assert.Equal(1, await scope.Store.GetActiveKeyCountAsync());
    }

    [Fact]
    public async Task OverrideKeyFromPlaintextAsync_NewPlaintext_RevokesPreviousKeyWithThatName()
    {
        await using var scope = await CreateAsync();
        await scope.Store.OverrideKeyFromPlaintextAsync("seeded", "lk_override-one-0123456789");

        var (created, revoked) = await scope.Store.OverrideKeyFromPlaintextAsync("seeded", "lk_override-two-0123456789");

        Assert.NotNull(created);
        Assert.Equal(1, revoked);
        Assert.Null(await scope.Store.ValidateKeyAsync("lk_override-one-0123456789"));
        Assert.NotNull(await scope.Store.ValidateKeyAsync("lk_override-two-0123456789"));
    }

    [Fact]
    public async Task RegenerateKeyAsync_UnknownKey_Throws()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("only");

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RegenerateKeyAsync("does-not-exist"));
    }

    [Fact]
    public async Task RegenerateKeyAsync_AlreadyRevokedKey_DoesNotIssueReplacement()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateKeyAsync("keeper");
        var revoked = await scope.Store.CreateKeyAsync("revoked");
        await scope.Store.RevokeKeyAsync(revoked.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RegenerateKeyAsync(revoked.Id));

        Assert.Equal(1, await scope.Store.GetActiveKeyCountAsync());
    }}
