using lucia.Data.Redis;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Services;
using lucia.Data.Extensions;
using lucia.Data.InMemory;
using lucia.Data.PostgreSQL;
using lucia.Data.Sqlite;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace lucia.Tests.Data;

/// <summary>
/// The API key and speaker profile stores are wrapped by their caching decorators for every
/// persistent provider and cache provider (in-memory or Redis).
/// </summary>
public sealed class StoreProviderCachingRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sqlite_WrapsApiKeyServiceAndSpeakerStoreInCaches(bool withRedis)
    {
        using var provider = BuildSqlite(withRedis);

        Assert.IsType<CachedApiKeyService>(provider.GetRequiredService<IApiKeyService>());
        Assert.IsType<CachedSpeakerProfileStore>(provider.GetRequiredService<ISpeakerProfileStore>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Postgres_WrapsApiKeyServiceAndSpeakerStoreInCaches(bool withRedis)
    {
        using var provider = BuildPostgres(withRedis);

        Assert.IsType<CachedApiKeyService>(provider.GetRequiredService<IApiKeyService>());
        Assert.IsType<CachedSpeakerProfileStore>(provider.GetRequiredService<ISpeakerProfileStore>());
    }

    [Fact]
    public void WithoutAnotherCacheProvider_FallsBackToTheInMemoryCache()
    {
        using var provider = BuildSqlite(withRedis: false);

        Assert.IsType<InMemoryKeyValueCache>(provider.GetRequiredService<IKeyValueCache>());
    }

    [Fact]
    public void ARegisteredCacheProvider_IsKeptOverTheInMemoryFallback()
    {
        using var provider = BuildSqlite(withRedis: true);

        Assert.IsType<RedisKeyValueCache>(provider.GetRequiredService<IKeyValueCache>());
    }
    private static ServiceProvider BuildSqlite(bool withRedis)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProvider:SqlitePath"] = Path.Combine(Path.GetTempPath(), $"lucia-cache-reg-{Guid.NewGuid():N}.db"),
        });
        builder.Services.AddLogging();
        builder.AddSqliteStoreProviders();
        return Finish(builder, withRedis);
    }

    private static ServiceProvider BuildPostgres(bool withRedis)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:luciaconfig"] = "Host=localhost;Database=luciaconfig;Username=test;Password=test",
            ["ConnectionStrings:luciatraces"] = "Host=localhost;Database=luciatraces;Username=test;Password=test",
            ["ConnectionStrings:luciatasks"] = "Host=localhost;Database=luciatasks;Username=test;Password=test",
        });
        builder.Services.AddLogging();
        builder.AddPostgresStoreProviders();
        return Finish(builder, withRedis);
    }

    private static ServiceProvider Finish(HostApplicationBuilder builder, bool withRedis)
    {
        if (withRedis)
        {
            builder.Services.AddSingleton(A.Fake<IConnectionMultiplexer>());
            builder.Services.AddSingleton<IKeyValueCache, RedisKeyValueCache>();
        }

        return builder.Services.BuildServiceProvider();
    }
}