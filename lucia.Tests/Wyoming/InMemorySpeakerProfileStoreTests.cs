using lucia.Wyoming.Diarization;

namespace lucia.Tests.Wyoming;

public sealed class InMemorySpeakerProfileStoreTests
{
    [Fact]
    public async Task CreateAndGetAsync_CloneEmbeddings()
    {
        var store = new InMemorySpeakerProfileStore();
        var average = new[] { 1f, 2f };
        var embedding = new[] { 3f, 4f };
        var profile = new SpeakerProfile
        {
            Id = "speaker-1",
            Name = "Speaker 1",
            AverageEmbedding = average,
            Embeddings = [embedding],
        };

        await store.CreateAsync(profile, CancellationToken.None);

        average[0] = 99f;
        embedding[0] = 88f;

        var stored = await store.GetAsync(profile.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(1f, stored.AverageEmbedding[0]);
        Assert.Equal(3f, stored.Embeddings[0][0]);

        stored.AverageEmbedding[1] = 77f;
        stored.Embeddings[0][1] = 66f;

        var reloaded = await store.GetAsync(profile.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(2f, reloaded.AverageEmbedding[1]);
        Assert.Equal(4f, reloaded.Embeddings[0][1]);
    }

    [Fact]
    public async Task UpdateAtomicAsync_ClonesTransformInputAndStoredProfile()
    {
        var store = new InMemorySpeakerProfileStore();
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "speaker-1",
                Name = "Speaker 1",
                AverageEmbedding = [1f, 2f],
                Embeddings = [[1f, 2f]],
            },
            CancellationToken.None);

        var externalAverage = new[] { 10f, 20f };
        var externalEmbedding = new[] { 30f, 40f };

        var updated = await store.UpdateAtomicAsync(
            "speaker-1",
            profile =>
            {
                profile.AverageEmbedding[0] = 500f;
                profile.Embeddings[0][0] = 600f;

                return profile with
                {
                    AverageEmbedding = externalAverage,
                    Embeddings = [externalEmbedding],
                };
            },
            CancellationToken.None);

        externalAverage[0] = 99f;
        externalEmbedding[0] = 88f;

        Assert.NotNull(updated);
        Assert.Equal(10f, updated.AverageEmbedding[0]);
        Assert.Equal(30f, updated.Embeddings[0][0]);

        updated.AverageEmbedding[1] = 77f;
        updated.Embeddings[0][1] = 66f;

        var reloaded = await store.GetAsync("speaker-1", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(10f, reloaded.AverageEmbedding[0]);
        Assert.Equal(20f, reloaded.AverageEmbedding[1]);
        Assert.Equal(30f, reloaded.Embeddings[0][0]);
        Assert.Equal(40f, reloaded.Embeddings[0][1]);
    }
}
