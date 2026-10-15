using lucia.Data.Redis;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace lucia.Tests.Data.Providers;

[Trait("Category", "Integration")]
public sealed class RedisKeyValueCacheTests : KeyValueCacheContract, IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    protected override IKeyValueCache CreateCache() => new RedisKeyValueCache(_redis!);
}