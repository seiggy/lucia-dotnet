using FakeItEasy;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Data;

public sealed class SqliteMemoryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteMemoryStore _store;

    public SqliteMemoryStoreTests()
    {
        _dbPath = Path.Combine(AppContext.BaseDirectory, $"memory-store-{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);

        var runner = new SqliteMigrationRunner(_connectionFactory, A.Fake<ILogger<SqliteMigrationRunner>>());
        runner.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _store = new SqliteMemoryStore(_connectionFactory);
    }

    [Fact]
    public async Task StoreAsync_And_RetrieveAsync_RoundTrips()
    {
        await _store.StoreAsync("user-1", "timezone", "America/Chicago");

        var value = await _store.RetrieveAsync("user-1", "timezone");

        Assert.Equal("America/Chicago", value);
    }

    [Fact]
    public async Task SearchAsync_FiltersExpiredEntries()
    {
        await _store.StoreAsync("user-1", "diet", "vegetarian");
        await _store.StoreAsync("user-1", "expired", "vegetarian", TimeSpan.FromMilliseconds(-1));

        var results = await _store.SearchAsync("user-1", "vegetarian");

        Assert.Single(results);
        Assert.Equal("diet", results[0].Key);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredEntry()
    {
        await _store.StoreAsync("user-1", "nickname", "Sam");

        await _store.DeleteAsync("user-1", "nickname");

        var value = await _store.RetrieveAsync("user-1", "nickname");
        Assert.Null(value);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
