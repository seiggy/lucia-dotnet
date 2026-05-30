using System.Security.Claims;
using System.Security.Cryptography;
using FakeItEasy;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Auth;

public class HmacSessionServiceTests
{
    private readonly HmacSessionService _service;
    private readonly IConfigStoreWriter _configStore;

    public HmacSessionServiceTests()
    {
        var signingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SessionSigningKey"] = Convert.ToBase64String(signingKey),
            })
            .Build();

        var options = Options.Create(new AuthOptions());
        var logger = A.Fake<ILogger<HmacSessionService>>();
        _configStore = A.Fake<IConfigStoreWriter>();

        _service = new HmacSessionService(config, options, logger, _configStore);
        // InitializeAsync must run before Create/Validate (fail-fast contract)
        _service.InitializeAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void CreateSession_ProducesSignedCookieWithTwoParts()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");

        var parts = cookie.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.False(string.IsNullOrWhiteSpace(parts[0]));
        Assert.False(string.IsNullOrWhiteSpace(parts[1]));
    }

    [Fact]
    public void ValidateSession_ReturnsClaimsForValidCookie()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");

        var claims = _service.ValidateSession(cookie);

        Assert.NotNull(claims);
        var list = claims.ToList();
        Assert.Contains(list, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "key-1");
        Assert.Contains(list, c => c.Type == ClaimTypes.Name && c.Value == "Test Key");
        Assert.Contains(list, c => c.Type == "auth_method" && c.Value == "session");
    }

    [Fact]
    public void ValidateSession_ReturnsNullForTamperedCookie()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");
        var tampered = cookie + "x";

        var claims = _service.ValidateSession(tampered);

        Assert.Null(claims);
    }

    [Fact]
    public async Task ValidateSession_ReturnsNullForExpiredCookie()
    {
        // Use a negative lifetime so the token is already expired at creation time
        var signingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SessionSigningKey"] = Convert.ToBase64String(signingKey),
            })
            .Build();

        var options = Options.Create(new AuthOptions
        {
            SessionLifetime = TimeSpan.FromSeconds(-5),
        });

        var shortLived = new HmacSessionService(
            config,
            options,
            A.Fake<ILogger<HmacSessionService>>(),
            A.Fake<IConfigStoreWriter>());
        await shortLived.InitializeAsync();
        var cookie = shortLived.CreateSession("key-1", "Test Key");

        var claims = shortLived.ValidateSession(cookie);

        Assert.Null(claims);
    }

    [Fact]
    public void RoundTrip_CreateThenValidateReturnsOriginalClaims()
    {
        var keyId = "key-42";
        var keyName = "My Device";

        var cookie = _service.CreateSession(keyId, keyName);
        var claims = _service.ValidateSession(cookie);

        Assert.NotNull(claims);
        var list = claims.ToList();
        Assert.Equal(keyId, list.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(keyName, list.First(c => c.Type == ClaimTypes.Name).Value);
    }

    [Fact]
    public async Task InitializeAsync_LoadsKeyFromConfiguration()
    {
        var signingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SessionSigningKey"] = Convert.ToBase64String(signingKey),
            })
            .Build();

        var configStore = A.Fake<IConfigStoreWriter>();
        var svc = new HmacSessionService(
            config,
            Options.Create(new AuthOptions()),
            A.Fake<ILogger<HmacSessionService>>(),
            configStore);

        await svc.InitializeAsync();

        // Config store should NOT be queried — key was already in IConfiguration
        A.CallTo(() => configStore.GetAsync(A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // Sessions should round-trip correctly using the loaded key
        var cookie = svc.CreateSession("k1", "Test");
        Assert.NotNull(svc.ValidateSession(cookie));
    }

    [Fact]
    public async Task InitializeAsync_LoadsKeyFromConfigStore_WhenAbsentFromConfiguration()
    {
        var existingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder().Build(); // no key in config

        var configStore = A.Fake<IConfigStoreWriter>();
        A.CallTo(() => configStore.GetAsync("Auth:SessionSigningKey", A<CancellationToken>._))
            .Returns(Convert.ToBase64String(existingKey));

        var svc = new HmacSessionService(
            config,
            Options.Create(new AuthOptions()),
            A.Fake<ILogger<HmacSessionService>>(),
            configStore);

        await svc.InitializeAsync();

        // SetAsync should NOT be called — the key already existed in the store
        A.CallTo(() => configStore.SetAsync(
                A<string>._, A<string?>._, A<string>._, A<bool>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        var cookie = svc.CreateSession("k1", "Test");
        Assert.NotNull(svc.ValidateSession(cookie));
    }

    [Fact]
    public async Task InitializeAsync_GeneratesAndPersistsKey_WhenAbsentFromBothSources()
    {
        var config = new ConfigurationBuilder().Build(); // no key in config

        var configStore = A.Fake<IConfigStoreWriter>();
        A.CallTo(() => configStore.GetAsync("Auth:SessionSigningKey", A<CancellationToken>._))
            .Returns((string?)null);

        var svc = new HmacSessionService(
            config,
            Options.Create(new AuthOptions()),
            A.Fake<ILogger<HmacSessionService>>(),
            configStore);

        await svc.InitializeAsync();

        // A new key must have been written to the config store
        A.CallTo(() => configStore.SetAsync(
                "Auth:SessionSigningKey",
                A<string>.Ignored,
                "system-init",
                true,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // Sessions should be functional after initialization
        var cookie = svc.CreateSession("k1", "Test");
        Assert.NotNull(svc.ValidateSession(cookie));
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WhenCalledMultipleTimes()
    {
        var config = new ConfigurationBuilder().Build();

        var configStore = A.Fake<IConfigStoreWriter>();
        A.CallTo(() => configStore.GetAsync(A<string>._, A<CancellationToken>._))
            .Returns((string?)null);

        var svc = new HmacSessionService(
            config,
            Options.Create(new AuthOptions()),
            A.Fake<ILogger<HmacSessionService>>(),
            configStore);

        await svc.InitializeAsync();
        await svc.InitializeAsync(); // second call should be a no-op

        A.CallTo(() => configStore.SetAsync(
                A<string>._, A<string?>._, A<string>._, A<bool>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_PersistKeyExactlyOnce()
    {
        // This test verifies that the SemaphoreSlim double-check enforces a single write
        // even when multiple callers race through InitializeAsync simultaneously.
        var config = new ConfigurationBuilder().Build();

        var configStore = A.Fake<IConfigStoreWriter>();
        A.CallTo(() => configStore.GetAsync(A<string>._, A<CancellationToken>._))
            .Returns((string?)null);

        var svc = new HmacSessionService(
            config,
            Options.Create(new AuthOptions()),
            A.Fake<ILogger<HmacSessionService>>(),
            configStore);

        await Task.WhenAll(
            svc.InitializeAsync(CancellationToken.None),
            svc.InitializeAsync(CancellationToken.None),
            svc.InitializeAsync(CancellationToken.None));

        // The semaphore + double-check must ensure exactly one write reaches the store
        A.CallTo(() => configStore.SetAsync(
                A<string>._, A<string?>._, A<string>._, A<bool>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
