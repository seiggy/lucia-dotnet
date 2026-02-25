using FakeItEasy;
using lucia.Agents.Models;
using lucia.Agents.Services;
using MongoDB.Driver;

namespace lucia.Tests.Presence;

public sealed class MongoPresenceSensorRepositoryTests
{
    private readonly IMongoClient _mongoClient = A.Fake<IMongoClient>();
    private readonly IMongoDatabase _database = A.Fake<IMongoDatabase>();
    private readonly IMongoCollection<PresenceSensorMapping> _mappingsCollection = A.Fake<IMongoCollection<PresenceSensorMapping>>();
    private readonly IMongoIndexManager<PresenceSensorMapping> _indexManager = A.Fake<IMongoIndexManager<PresenceSensorMapping>>();

    private readonly MongoPresenceSensorRepository _repository;

    public MongoPresenceSensorRepositoryTests()
    {
        A.CallTo(() => _mongoClient.GetDatabase("luciaconfig", null)).Returns(_database);
        A.CallTo(() => _database.GetCollection<PresenceSensorMapping>("presence_sensor_mappings", null))
            .Returns(_mappingsCollection);
        A.CallTo(() => _mappingsCollection.Indexes).Returns(_indexManager);

        _repository = new MongoPresenceSensorRepository(_mongoClient);
    }

    [Fact]
    public void Constructor_CreatesIndexes()
    {
        A.CallTo(() => _indexManager.CreateMany(
            A<IEnumerable<CreateIndexModel<PresenceSensorMapping>>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetAllMappingsAsync_ReturnsMappingsFromCollection()
    {
        var expected = new List<PresenceSensorMapping>
        {
            new() { EntityId = "binary_sensor.office_presence", AreaId = "office", Confidence = PresenceConfidence.High },
        };

        var cursor = A.Fake<IAsyncCursor<PresenceSensorMapping>>();
        A.CallTo(() => cursor.MoveNextAsync(A<CancellationToken>._))
            .ReturnsNextFromSequence(true, false);
        A.CallTo(() => cursor.Current).Returns(expected);

        A.CallTo(() => _mappingsCollection.FindAsync(
            A<FilterDefinition<PresenceSensorMapping>>._,
            A<FindOptions<PresenceSensorMapping, PresenceSensorMapping>>._,
            A<CancellationToken>._)).Returns(cursor);

        var result = await _repository.GetAllMappingsAsync();

        Assert.Single(result);
        Assert.Equal("binary_sensor.office_presence", result[0].EntityId);
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_DeletesOldAndInsertsNew()
    {
        var newMappings = new List<PresenceSensorMapping>
        {
            new() { EntityId = "binary_sensor.new_sensor", AreaId = "kitchen", Confidence = PresenceConfidence.Medium },
        };

        await _repository.ReplaceAutoDetectedMappingsAsync(newMappings);

        A.CallTo(() => _mappingsCollection.DeleteManyAsync(
            A<FilterDefinition<PresenceSensorMapping>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        A.CallTo(() => _mappingsCollection.InsertManyAsync(
            A<IEnumerable<PresenceSensorMapping>>.That.Matches(list => list.Count() == 1),
            A<InsertManyOptions>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_EmptyList_SkipsInsert()
    {
        await _repository.ReplaceAutoDetectedMappingsAsync([]);

        A.CallTo(() => _mappingsCollection.DeleteManyAsync(
            A<FilterDefinition<PresenceSensorMapping>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        A.CallTo(() => _mappingsCollection.InsertManyAsync(
            A<IEnumerable<PresenceSensorMapping>>._,
            A<InsertManyOptions>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpsertMappingAsync_CallsReplaceOneWithUpsert()
    {
        var mapping = new PresenceSensorMapping
        {
            EntityId = "binary_sensor.custom",
            AreaId = "bedroom",
            IsUserOverride = true,
            Confidence = PresenceConfidence.High
        };

        await _repository.UpsertMappingAsync(mapping);

        A.CallTo(() => _mappingsCollection.ReplaceOneAsync(
            A<FilterDefinition<PresenceSensorMapping>>._,
            mapping,
            A<ReplaceOptions>.That.Matches(o => o.IsUpsert),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DeleteMappingAsync_CallsDeleteOne()
    {
        await _repository.DeleteMappingAsync("binary_sensor.old");

        A.CallTo(() => _mappingsCollection.DeleteOneAsync(
            A<FilterDefinition<PresenceSensorMapping>>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
