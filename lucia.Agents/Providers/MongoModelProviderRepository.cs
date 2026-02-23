using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Agents.Providers;

/// <summary>
/// MongoDB-backed repository for model provider configurations.
/// </summary>
public sealed class MongoModelProviderRepository : IModelProviderRepository
{
    private readonly IMongoCollection<ModelProvider> _collection;
    private readonly ILogger<MongoModelProviderRepository> _logger;

    public MongoModelProviderRepository(
        IMongoClient mongoClient,
        ILogger<MongoModelProviderRepository> logger)
    {
        _logger = logger;
        var database = mongoClient.GetDatabase("luciaconfig");
        _collection = database.GetCollection<ModelProvider>(ModelProvider.CollectionName);

        // Ensure unique index on Name
        var indexKeys = Builders<ModelProvider>.IndexKeys.Ascending(p => p.Name);
        var indexOptions = new CreateIndexOptions { Unique = true, Background = true };
        _collection.Indexes.CreateOne(new CreateIndexModel<ModelProvider>(indexKeys, indexOptions));
    }

    public async Task<List<ModelProvider>> GetAllProvidersAsync(CancellationToken ct = default)
    {
        return await _collection.Find(FilterDefinition<ModelProvider>.Empty)
            .SortBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ModelProvider>> GetEnabledProvidersAsync(CancellationToken ct = default)
    {
        return await _collection.Find(p => p.Enabled)
            .SortBy(p => p.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<ModelProvider?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        return await _collection.Find(p => p.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        provider.UpdatedAt = DateTime.UtcNow;
        var options = new ReplaceOptions { IsUpsert = true };
        await _collection.ReplaceOneAsync(p => p.Id == provider.Id, provider, options, ct).ConfigureAwait(false);
        _logger.LogInformation("Upserted model provider {ProviderId} ({ProviderName})", provider.Id, provider.Name);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(p => p.Id == id, ct).ConfigureAwait(false);
        _logger.LogInformation("Deleted model provider {ProviderId}", id);
    }
}
