using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace lucia.Tests.Wyoming;

public sealed class MongoSpeakerProfileStoreTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();
    private MongoSpeakerProfileStore? _store;
    private IMongoDatabase? _database;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var client = new MongoClient(_container.GetConnectionString());
        var databaseName = $"lucia_speaker_tests_{Guid.NewGuid():N}";
        _database = client.GetDatabase(databaseName);
        _store = new MongoSpeakerProfileStore(
            client,
            Options.Create(new DiarizationOptions { ProfileStoreDatabaseName = databaseName }),
            NullLogger<MongoSpeakerProfileStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UpdateAtomicAsync_UpdatesLegacyProfileWithoutRevision()
    {
        var collection = Database.GetCollection<BsonDocument>("speaker_profiles");
        await collection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy",
            ["Name"] = "Legacy",
        });

        var updated = await Store.UpdateAtomicAsync(
            "legacy",
            static profile => profile with { Name = "Updated" },
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal(1, updated.Revision);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UpdateAtomicAsync_RetriesCompetingReplacement()
    {
        await Store.CreateAsync(
            new SpeakerProfile { Id = "profile-1", Name = "Test" },
            CancellationToken.None);
        using var firstAttemptBarrier = new Barrier(2);

        Task<SpeakerProfile?> UpdateAsync() => Store.UpdateAtomicAsync(
            "profile-1",
            profile =>
            {
                if (profile.Revision == 0)
                {
                    firstAttemptBarrier.SignalAndWait();
                }

                return profile with { InteractionCount = profile.InteractionCount + 1 };
            },
            CancellationToken.None);

        await Task.WhenAll(UpdateAsync(), UpdateAsync());
        var updated = await Store.GetAsync("profile-1", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.InteractionCount);
        Assert.Equal(2, updated.Revision);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UpdateAsync_AllowsSequentialReplacementWithSameProfileInstance()
    {
        var profile = new SpeakerProfile { Id = "profile-1", Name = "Original" };
        await Store.CreateAsync(profile, CancellationToken.None);

        await Store.UpdateAsync(profile with { Name = "First" }, CancellationToken.None);
        await Store.UpdateAsync(profile with { Name = "Second" }, CancellationToken.None);

        var updated = await Store.GetAsync("profile-1", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("Second", updated.Name);
        Assert.Equal(2, updated.Revision);
    }

    private MongoSpeakerProfileStore Store =>
        _store ?? throw new InvalidOperationException("The MongoDB fixture has not started.");

    private IMongoDatabase Database =>
        _database ?? throw new InvalidOperationException("The MongoDB fixture has not started.");
}
