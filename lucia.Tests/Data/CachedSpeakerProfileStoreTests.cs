using FakeItEasy;
using lucia.Data.InMemory;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data;

public sealed class CachedSpeakerProfileStoreTests
{
    private const string EnrolledKey = "lucia:speaker-profiles:enrolled";

    private readonly ISpeakerProfileStore _inner = A.Fake<ISpeakerProfileStore>();
    private readonly InMemoryKeyValueCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    private CachedSpeakerProfileStore CreateStore()
        => new(_inner, _cache, NullLogger<CachedSpeakerProfileStore>.Instance);

    [Fact]
    public async Task GetEnrolledProfilesAsync_SecondRead_ComesFromTheCache()
    {
        A.CallTo(() => _inner.GetEnrolledProfilesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<SpeakerProfile>>([Profile("speaker-1")]));
        var store = CreateStore();

        await store.GetEnrolledProfilesAsync(CancellationToken.None);
        await WaitForCacheAsync();
        var second = await store.GetEnrolledProfilesAsync(CancellationToken.None);

        Assert.Equal("speaker-1", Assert.Single(second).Id);
        A.CallTo(() => _inner.GetEnrolledProfilesAsync(A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateAsync_InvalidatesTheEnrolledCache()
    {
        A.CallTo(() => _inner.GetEnrolledProfilesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<SpeakerProfile>>([Profile("speaker-1")]));
        var store = CreateStore();
        await store.GetEnrolledProfilesAsync(CancellationToken.None);
        await WaitForCacheAsync();

        await store.CreateAsync(Profile("speaker-2"), CancellationToken.None);

        Assert.Null(await _cache.GetAsync(EnrolledKey));
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesTheEnrolledCache()
    {
        A.CallTo(() => _inner.GetEnrolledProfilesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<SpeakerProfile>>([Profile("speaker-1")]));
        var store = CreateStore();
        await store.GetEnrolledProfilesAsync(CancellationToken.None);
        await WaitForCacheAsync();

        await store.DeleteAsync("speaker-1", CancellationToken.None);

        Assert.Null(await _cache.GetAsync(EnrolledKey));
    }

    [Fact]
    public async Task GetEnrolledProfilesAsync_CacheThrows_FallsBackToTheStore()
    {
        var failing = A.Fake<lucia.Agents.Abstractions.IKeyValueCache>();
        A.CallTo(() => failing.GetAsync(A<string>._, A<CancellationToken>._)).ThrowsAsync(new InvalidOperationException("cache down"));
        A.CallTo(() => _inner.GetEnrolledProfilesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<SpeakerProfile>>([Profile("speaker-1")]));
        var store = new CachedSpeakerProfileStore(_inner, failing, NullLogger<CachedSpeakerProfileStore>.Instance);

        var result = await store.GetEnrolledProfilesAsync(CancellationToken.None);

        Assert.Equal("speaker-1", Assert.Single(result).Id);
    }

    // The decorator fills the cache in the background, so wait for the write.
    private async Task WaitForCacheAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (await _cache.GetAsync(EnrolledKey) is null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The enrolled-profile cache was never filled.");
            await Task.Delay(20);
        }
    }

    private static SpeakerProfile Profile(string id) => new()
    {
        Id = id,
        Name = id,
        IsProvisional = false,
        IsAuthorized = true,
        AllowedSkills = [],
        Embeddings = [[1.0f, 2.0f]],
        AverageEmbedding = [1.0f, 2.0f],
        EnrolledAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastSeenAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };
}