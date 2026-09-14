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

        var runner = new SqliteMigrationRunner(_connectionFactory, _connectionFactory, _connectionFactory, A.Fake<ILogger<SqliteMigrationRunner>>());
        runner.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _store = new SqliteMemoryStore(_connectionFactory);
    }

    [Fact]
    public Task SearchPersonalAsync_FiltersReservedHistoryBeforeTheLimit() =>
        PersonalMemorySearchAssertions.VerifyAsync(_store);

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
    public async Task SearchAsync_TreatsWildcardCharactersAsLiterals()
    {
        await PersonalMemorySearchAssertions.VerifyLiteralSearchSemanticsAsync(_store);
    }

    [Fact]
    public async Task StoreAsync_WithAbsoluteExpiresAt_PersistsExactDeadline()
    {
        var expiresAt = new DateTimeOffset(2030, 2, 3, 4, 5, 6, 987, TimeSpan.FromHours(-7));

        await _store.StoreAsync("user-1", "deadline", "value", expiresAt, CancellationToken.None);

        var entry = Assert.Single(await _store.GetAllAsync("user-1"));
        Assert.Equal(expiresAt.UtcDateTime, entry.ExpiresAt);

        await _store.StoreAsync("user-1", "deadline", "edited", new DateTimeOffset(entry.ExpiresAt!.Value), CancellationToken.None);
        var edited = Assert.Single(await _store.GetAllAsync("user-1"));
        Assert.Equal("edited", edited.Value);
        Assert.Equal(entry.ExpiresAt, edited.ExpiresAt);
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
