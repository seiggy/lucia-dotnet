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

    /// <summary>
    /// Loads all persisted model overrides and returns them as a dictionary.
    /// </summary>
    public async Task<Dictionary<EngineType, string>> LoadOverridesAsync(CancellationToken ct = default)
    {
        try
        {
            var cursor = await _collection
                .FindAsync(FilterDefinition<ActiveModelPreference>.Empty, cancellationToken: ct)
                .ConfigureAwait(false);

            var prefs = await cursor.ToListAsync(ct).ConfigureAwait(false);

            var result = new Dictionary<EngineType, string>();
            foreach (var pref in prefs)
            {
                if (Enum.TryParse<EngineType>(pref.EngineType, ignoreCase: true, out var engineType))
                {
                    result[engineType] = pref.ModelId;
                }
            }

            _logger.LogInformation("Loaded {Count} persisted model preference(s)", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model preferences from MongoDB — starting with defaults");
            return [];
        }
    }

    /// <summary>
    /// Persists a model override for the given engine type.
    /// </summary>
    public async Task SaveOverrideAsync(EngineType engineType, string modelId, CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<ActiveModelPreference>.Filter.Eq(p => p.EngineType, engineType.ToString());
            var pref = new ActiveModelPreference
            {
                EngineType = engineType.ToString(),
                ModelId = modelId,
                UpdatedAt = DateTime.UtcNow,
            };

            await _collection.ReplaceOneAsync(
                filter, pref, new ReplaceOptions { IsUpsert = true }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist model preference for {EngineType}/{ModelId}", engineType, modelId);
        }
    }

    /// <summary>
    /// Removes a persisted model override.
    /// </summary>
    public async Task RemoveOverrideAsync(EngineType engineType, CancellationToken ct = default)
    {
        try
        {
            await _collection.DeleteOneAsync(
                Builders<ActiveModelPreference>.Filter.Eq(p => p.EngineType, engineType.ToString()), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove model preference for {EngineType}", engineType);
        }
    }
}
