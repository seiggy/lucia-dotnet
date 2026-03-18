using MongoDB.Driver;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// MongoDB-backed implementation of <see cref="IResponseTemplateRepository"/>.
/// </summary>
public sealed class MongoResponseTemplateRepository : IResponseTemplateRepository
{
    private const string DatabaseName = "luciaconfig";
    private const string CollectionName = "response_templates";

    private readonly IMongoCollection<ResponseTemplate> _collection;

    public MongoResponseTemplateRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(DatabaseName);
        _collection = db.GetCollection<ResponseTemplate>(CollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany([
            new CreateIndexModel<ResponseTemplate>(
                Builders<ResponseTemplate>.IndexKeys
                    .Ascending(t => t.SkillId)
                    .Ascending(t => t.Action),
                new CreateIndexOptions { Unique = true, Name = "idx_skillId_action" }),
        ]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResponseTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        var cursor = await _collection
            .FindAsync(Builders<ResponseTemplate>.Filter.Empty, cancellationToken: ct)
            .ConfigureAwait(false);

        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cursor = await _collection
            .FindAsync(Builders<ResponseTemplate>.Filter.Eq(t => t.Id, id), cancellationToken: ct)
            .ConfigureAwait(false);

        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate?> GetBySkillAndActionAsync(
        string skillId,
        string action,
        CancellationToken ct = default)
    {
        var filter = Builders<ResponseTemplate>.Filter.Eq(t => t.SkillId, skillId)
                   & Builders<ResponseTemplate>.Filter.Eq(t => t.Action, action);

        var cursor = await _collection
            .FindAsync(filter, cancellationToken: ct)
            .ConfigureAwait(false);

        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate> CreateAsync(ResponseTemplate template, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(template, cancellationToken: ct).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate> UpdateAsync(
        string id,
        ResponseTemplate template,
        CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;

        await _collection.ReplaceOneAsync(
            Builders<ResponseTemplate>.Filter.Eq(t => t.Id, id),
            template,
            new ReplaceOptions { IsUpsert = false },
            ct).ConfigureAwait(false);

        return template;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var result = await _collection.DeleteOneAsync(
            Builders<ResponseTemplate>.Filter.Eq(t => t.Id, id),
            ct).ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        await _collection.DeleteManyAsync(
            Builders<ResponseTemplate>.Filter.Empty,
            ct).ConfigureAwait(false);

        var defaults = DefaultResponseTemplates.GetDefaults();
        await _collection.InsertManyAsync(defaults, cancellationToken: ct).ConfigureAwait(false);
    }
}
