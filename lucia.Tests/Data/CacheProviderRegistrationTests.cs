using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using lucia.Data.Extensions;
using lucia.Data.InMemory;
using lucia.Data.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace lucia.Tests.Data;

/// <summary>
/// The host picks one cache provider: Redis or in-memory. Each registers the same set of services.
/// </summary>
public sealed class CacheProviderRegistrationTests
{
    [Theory]
    [InlineData(typeof(IDeviceCacheService), typeof(RedisDeviceCacheService))]
    [InlineData(typeof(ISessionCacheService), typeof(RedisSessionCacheService))]
    [InlineData(typeof(IPromptCacheService), typeof(RedisPromptCacheService))]
    [InlineData(typeof(IKeyValueCache), typeof(RedisKeyValueCache))]
    [InlineData(typeof(IEntityLocationService), typeof(EntityLocationService))]
    [InlineData(typeof(ILocationSnapshotStore), typeof(RedisLocationSnapshotStore))]
    [InlineData(typeof(IVisibilityConfigStore), typeof(RedisVisibilityConfigStore))]
    public void AddRedisCacheProviders_RegistersTheRedisImplementation(Type service, Type implementation)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis"] = "localhost:6379",
        });

        builder.AddRedisCacheProviders();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == service && descriptor.ImplementationType == implementation);
    }

    [Fact]
    public void AddRedisCacheProviders_RegistersTheTaskStoreAndItsIndex()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis"] = "localhost:6379",
        });

        builder.AddRedisCacheProviders();

        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(A2A.ITaskStore));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(ITaskIdIndex));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(RedisTaskStore));
    }

    [Theory]
    [InlineData(typeof(IDeviceCacheService), typeof(InMemoryDeviceCacheService))]
    [InlineData(typeof(ISessionCacheService), typeof(InMemorySessionCacheService))]
    [InlineData(typeof(IPromptCacheService), typeof(InMemoryPromptCacheService))]
    [InlineData(typeof(IKeyValueCache), typeof(InMemoryKeyValueCache))]
    [InlineData(typeof(IEntityLocationService), typeof(EntityLocationService))]
    [InlineData(typeof(ILocationSnapshotStore), typeof(InProcessLocationSnapshotStore))]
    [InlineData(typeof(IVisibilityConfigStore), typeof(ConfigStoreVisibilityConfigStore))]
    public void AddInMemoryCacheProviders_RegistersTheSameServices(Type service, Type implementation)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.AddInMemoryCacheProviders();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == service && descriptor.ImplementationType == implementation);
    }
}