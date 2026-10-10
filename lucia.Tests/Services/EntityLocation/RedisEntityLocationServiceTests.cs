using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace lucia.Tests.Services.EntityLocation;

[Trait("Category", "Integration")]
public sealed class RedisEntityLocationServiceTests : EntityLocationServiceContract
{
    private static readonly Lazy<Task<IConnectionMultiplexer>> SharedRedis = new(StartRedisAsync);

    private static async Task<IConnectionMultiplexer> StartRedisAsync()
    {
        var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        return await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString() + ",allowAdmin=true");
    }

    protected override async Task<StoreScope<IEntityLocationService>> CreateAsync(SampleHomeAssistant home)
    {
        var redis = await SharedRedis.Value;
        await redis.GetServer(redis.GetEndPoints()[0]).FlushAllDatabasesAsync();

        var service = new EntityLocationService(
            home.Client,
            redis,
            EmbeddingResolver(),
            Search(),
            NullLogger<EntityLocationService>.Instance);
        return new StoreScope<IEntityLocationService>(service, () => ValueTask.CompletedTask);
    }

    [Fact]
    public async Task SecondInstance_LoadsTheSnapshotFromRedisWithoutCallingHomeAssistant()
    {
        var home = new SampleHomeAssistant();
        await using var first = await CreateAsync(home);
        await LoadAsync(first.Store);
        var redis = await SharedRedis.Value;

        var secondHome = new SampleHomeAssistant();
        var second = new EntityLocationService(
            secondHome.Client, redis, EmbeddingResolver(), Search(), NullLogger<EntityLocationService>.Instance);
        await second.InitializeAsync();

        Assert.Equal(0, secondHome.RegistryLoadCount);
        Assert.Equal(7, (await second.GetEntitiesAsync()).Count);
    }

    [Fact]
    public async Task Visibility_PersistsAcrossInstances()
    {
        var home = new SampleHomeAssistant();
        await using var first = await CreateAsync(home);
        await LoadAsync(first.Store);
        await first.Store.SetEntityAgentsAsync(new Dictionary<string, List<string>?> { ["light.porch"] = ["light-agent"] });
        var redis = await SharedRedis.Value;

        var second = new EntityLocationService(
            new SampleHomeAssistant().Client, redis, EmbeddingResolver(), Search(), NullLogger<EntityLocationService>.Instance);
        await second.InitializeAsync();

        Assert.Equal(["light-agent"], (await second.GetVisibilityConfigAsync()).EntityAgentMap["light.porch"]);
    }
}