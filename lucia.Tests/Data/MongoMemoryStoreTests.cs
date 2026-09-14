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
}
