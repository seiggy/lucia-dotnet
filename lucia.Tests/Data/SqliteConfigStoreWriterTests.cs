using lucia.Agents.Configuration.UserConfiguration;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

public sealed class SqliteConfigStoreWriterTests : IDisposable
{
    private readonly SqliteTestHelper _helper;
    private readonly SqliteConfigStoreWriter _writer;

    public SqliteConfigStoreWriterTests()
    {
        _helper = new SqliteTestHelper();
        _writer = new SqliteConfigStoreWriter(_helper.ConnectionFactory);
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_RoundTrips()
    {
        await _writer.SetAsync("HomeAssistant:BaseUrl", "http://localhost:8123");

        var result = await _writer.GetAsync("HomeAssistant:BaseUrl");

        Assert.Equal("http://localhost:8123", result);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForMissingKey()
    {
        var result = await _writer.GetAsync("nonexistent:key");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_UpsertsExistingKey()
    {
        await _writer.SetAsync("Config:Key1", "value-1");
        await _writer.SetAsync("Config:Key1", "value-2");

        var result = await _writer.GetAsync("Config:Key1");

        Assert.Equal("value-2", result);
    }

    [Fact]
    public async Task GetEntryCountAsync_ReturnsCorrectCount()
    {
        await _writer.SetAsync("Count:Key1", "v1");
        await _writer.SetAsync("Count:Key2", "v2");
        await _writer.SetAsync("Count:Key3", "v3");

        var count = await _writer.GetEntryCountAsync();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetAllKeysAsync_ReturnsAllKeys()
    {
        await _writer.SetAsync("Keys:Alpha", "a");
        await _writer.SetAsync("Keys:Beta", "b");

        var keys = await _writer.GetAllKeysAsync();

        Assert.Contains("Keys:Alpha", keys);
        Assert.Contains("Keys:Beta", keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task InsertManyAsync_InsertsMultipleEntries()
    {
        var entries = new List<ConfigEntry>
        {
            new()
            {
                Key = "Bulk:One",
                Value = "1",
                Section = "Bulk",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "test"
            },
            new()
            {
                Key = "Bulk:Two",
                Value = "2",
                Section = "Bulk",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "test"
            },
            new()
            {
                Key = "Bulk:Three",
                Value = "3",
                Section = "Bulk",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "test"
            }
        };

        await _writer.InsertManyAsync(entries);

        var count = await _writer.GetEntryCountAsync();
        Assert.Equal(3, count);

        var value = await _writer.GetAsync("Bulk:Two");
        Assert.Equal("2", value);
    }

    [Fact]
    public async Task InsertManyAsync_EmptyList_DoesNothing()
    {
        await _writer.InsertManyAsync([]);

        var count = await _writer.GetEntryCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SetAsync_StoresNullValue()
    {
        await _writer.SetAsync("Nullable:Key", null);

        var result = await _writer.GetAsync("Nullable:Key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllEntriesAsync_PreservesUtcKindOnUpdatedAt()
    {
        // Fixed instant avoids timing-precision issues; Kind=Utc is the critical invariant.
        var expectedUtc = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);
        await _writer.InsertManyAsync(
        [
            new ConfigEntry
            {
                Key = "Utc:RoundTrip",
                Value = "v",
                Section = "Utc",
                UpdatedAt = expectedUtc,
                UpdatedBy = "test"
            }
        ]);

        var entries = await _writer.GetAllEntriesAsync();
        var entry = Assert.Single(entries, e => e.Key == "Utc:RoundTrip");

        Assert.Equal(DateTimeKind.Utc, entry.UpdatedAt.Kind);
        Assert.Equal(expectedUtc, entry.UpdatedAt);
    }

    [Fact]
    public async Task GetAllEntriesAsync_LegacyOffsetlessTimestamp_ReturnsUtcKind()
    {
        // Simulate a legacy row whose updated_at was written by SQLite datetime('now')
        // — no offset, space separator. AssumeUniversal must treat it as UTC.
        using var connection = _helper.ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
            VALUES ('Legacy:Key', 'v', 'Legacy', '2025-01-01 12:00:00', 'system', 0);
            """;
        cmd.ExecuteNonQuery();

        var entries = await _writer.GetAllEntriesAsync();
        var entry = Assert.Single(entries, e => e.Key == "Legacy:Key");

        Assert.Equal(DateTimeKind.Utc, entry.UpdatedAt.Kind);
        Assert.Equal(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), entry.UpdatedAt);
    }

    [Fact]
    public async Task GetAllEntriesAsync_ExplicitPlusOffset_ConvertsToUtc()
    {
        // Row stored with +02:00 offset must be shifted to UTC on read.
        using var connection = _helper.ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
            VALUES ('Offset:Key', 'v', 'Offset', '2025-01-01T14:00:00.0000000+02:00', 'system', 0);
            """;
        cmd.ExecuteNonQuery();

        var entries = await _writer.GetAllEntriesAsync();
        var entry = Assert.Single(entries, e => e.Key == "Offset:Key");

        Assert.Equal(DateTimeKind.Utc, entry.UpdatedAt.Kind);
        // 14:00 +02:00 = 12:00 UTC
        Assert.Equal(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), entry.UpdatedAt);
    }

    public void Dispose()
    {
        _helper.Dispose();
    }
}
