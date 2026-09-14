using lucia.Agents.DataStores;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace lucia.Tests.Data;

public sealed class MongoMemoryStoreTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public Task SearchPersonalAsync_FiltersReservedHistoryBeforeTheLimit() =>
        PersonalMemorySearchAssertions.VerifyAsync(new MongoMemoryStore(new MongoClient(_container.GetConnectionString())));

    [Fact, Trait("Category", "Integration")]
    public async Task SearchAsync_TreatsWildcardCharactersAsLiterals()
    {
        var store = new MongoMemoryStore(new MongoClient(_container.GetConnectionString()));
        await PersonalMemorySearchAssertions.VerifyLiteralSearchSemanticsAsync(store);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task StoreAsync_WithAbsoluteExpiresAt_PersistsExactDeadline()
    {
        var store = new MongoMemoryStore(new MongoClient(_container.GetConnectionString()));
        var expiresAt = new DateTimeOffset(2030, 2, 3, 4, 5, 6, 987, TimeSpan.FromHours(-7));

        await store.StoreAsync("user-1", "deadline", "value", expiresAt, CancellationToken.None);

        var entry = Assert.Single(await store.GetAllAsync("user-1"));
        Assert.Equal(expiresAt.UtcDateTime, entry.ExpiresAt);

        await store.StoreAsync("user-1", "deadline", "edited", new DateTimeOffset(entry.ExpiresAt!.Value), CancellationToken.None);
        var edited = Assert.Single(await store.GetAllAsync("user-1"));
        Assert.Equal("edited", edited.Value);
        Assert.Equal(entry.ExpiresAt, edited.ExpiresAt);
    }
}
