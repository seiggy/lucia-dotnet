using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration provider that reads key-value pairs from MongoDB.
/// Supports periodic polling for change detection to enable IOptionsMonitor hot-reload.
/// </summary>
public sealed class MongoConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private DateTime _lastLoadTime = DateTime.MinValue;

    public MongoConfigurationProvider(string connectionString, string databaseName, TimeSpan? pollInterval = null)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public override void Load()
    {
        try
        {
            var client = new MongoClient(_connectionString);
            var database = client.GetDatabase(_databaseName);
            var collection = database.GetCollection<ConfigEntry>(ConfigEntry.CollectionName);

            var entries = collection.Find(FilterDefinition<ConfigEntry>.Empty).ToList();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                data[entry.Key] = entry.Value;
            }

            Data = data;
            _lastLoadTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // If MongoDB is unavailable, fall through to lower-priority config sources
            Console.WriteLine($"[lucia] MongoConfigurationProvider: Failed to load from MongoDB — {ex.Message}");
        }

        // Start polling for changes after initial load
        _pollTimer ??= new Timer(PollForChanges, null, _pollInterval, _pollInterval);
    }

    private void PollForChanges(object? state)
    {
        try
        {
            var client = new MongoClient(_connectionString);
            var database = client.GetDatabase(_databaseName);
            var collection = database.GetCollection<ConfigEntry>(ConfigEntry.CollectionName);

            // Check if any documents have been updated since last load
            var filter = Builders<ConfigEntry>.Filter.Gt(e => e.UpdatedAt, _lastLoadTime);
            var hasChanges = collection.Find(filter).Any();

            if (hasChanges)
            {
                Load();
                OnReload();
            }
        }
        catch
        {
            // Silently ignore polling failures — config stays at last known good state
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}
