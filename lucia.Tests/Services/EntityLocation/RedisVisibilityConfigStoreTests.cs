using FakeItEasy;
using lucia.Agents.Models.HomeAssistant;
using lucia.Data.Redis;
using StackExchange.Redis;

namespace lucia.Tests.Services.EntityLocation;

public sealed class RedisVisibilityConfigStoreTests
{
    private static RedisVisibilityConfigStore CreateStoreWithFailingRedis()
    {
        var database = A.Fake<IDatabase>();
        A.CallTo(database)
            .Where(call => call.Method.Name == "StringGetAsync" || call.Method.Name == "StringSetAsync")
            .WithNonVoidReturnType()
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis is down"));
        var redis = A.Fake<IConnectionMultiplexer>();
        A.CallTo(() => redis.GetDatabase(A<int>._, A<object>._)).Returns(database);
        return new RedisVisibilityConfigStore(redis);
    }

    [Fact]
    public async Task LoadAsync_RedisIsDown_Throws()
    {
        // Returning null here would read as "nothing saved" and reset the policy to its defaults.
        await Assert.ThrowsAsync<RedisConnectionException>(() => CreateStoreWithFailingRedis().LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_RedisIsDown_Throws()
    {
        await Assert.ThrowsAsync<RedisConnectionException>(
            () => CreateStoreWithFailingRedis().SaveAsync(new EntityVisibilityConfig()));
    }
}