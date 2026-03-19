using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Wyoming.Models;

/// <summary>
/// Persists active model overrides and the preferred STT engine type to MongoDB
/// so user selections survive process restarts.
/// </summary>
public sealed class MongoModelPreferenceStore : IModelPreferenceStore
{
    private const string DatabaseName = "luciawyoming";
    private const string CollectionName = "model_preferences";

    private readonly IMongoCollection<ActiveModelPreference> _collection;
    private readonly ILogger<MongoModelPreferenceStore> _logger;

    public MongoModelPreferenceStore(IMongoClient mongoClient, ILogger<MongoModelPreferenceStore> logger)
    {
        _collection = mongoClient
            .GetDatabase(DatabaseName)
            .GetCollection<ActiveModelPreference>(CollectionName);
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
    {
        try
        {
            var cursor = await _collection
                .FindAsync(FilterDefinition<ActiveModelPreference>.Empty, cancellationToken: ct)
                .ConfigureAwait(false);

            var prefs = await cursor.ToListAsync(ct).ConfigureAwait(false);
            var result = prefs.ToDictionary(p => p.EngineType, p => p.ModelId);

            _logger.LogInformation("Loaded {Count} persisted model preference(s)", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model preferences from MongoDB — starting with defaults");
            return [];
        }
    }

    public async Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<ActiveModelPreference>.Filter.Eq(p => p.EngineType, key);
            var pref = new ActiveModelPreference
            {
                EngineType = key,
                ModelId = value,
                UpdatedAt = DateTime.UtcNow,
            };

            await _collection.ReplaceOneAsync(
                filter, pref, new ReplaceOptions { IsUpsert = true }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist model preference for {Key}/{Value}", key, value);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _collection.DeleteOneAsync(
                Builders<ActiveModelPreference>.Filter.Eq(p => p.EngineType, key), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove model preference for {Key}", key);
        }
    }
}
