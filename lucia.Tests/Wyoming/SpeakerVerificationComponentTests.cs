using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class SpeakerVerificationComponentTests
{
    [Fact]
    public async Task UnknownTracker_FirstEncounter_CreatesProvisionalProfile()
    {
        var store = new InMemorySpeakerProfileStore();
        var tracker = CreateUnknownSpeakerTracker(store);
        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.25f, 0.0025f));

        var embedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);

        var result = await tracker.TrackUnknownSpeakerAsync(embedding, CancellationToken.None);
        Assert.NotNull(result);
        var (profile, shouldSuggestEnrollment) = result.Value;

        Assert.True(profile.IsProvisional);
        Assert.False(profile.IsAuthorized);
        Assert.Equal(1, profile.InteractionCount);
        Assert.Equal(embedding.Vector, profile.AverageEmbedding);
        Assert.False(shouldSuggestEnrollment);

        var storedProfile = await store.GetAsync(profile.Id, CancellationToken.None);
        Assert.NotNull(storedProfile);
        Assert.True(storedProfile.IsProvisional);
    }

    [Fact]
    public async Task UnknownTracker_RepeatedEncounter_UpdatesExistingProvisional()
    {
        var store = new InMemorySpeakerProfileStore();
        var tracker = CreateUnknownSpeakerTracker(store, provisionalMatchThreshold: 0.7f, suggestEnrollmentAfter: 3);
        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.4f, 0.001f));

        var firstEmbedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);
        var firstNullable = await tracker.TrackUnknownSpeakerAsync(firstEmbedding, CancellationToken.None);
        Assert.NotNull(firstNullable);
        var firstResult = firstNullable.Value;

        var secondEmbedding = engine.ExtractEmbedding(CreateAudioSamples(phase: 0.1f), 16_000);
        var secondNullable = await tracker.TrackUnknownSpeakerAsync(secondEmbedding, CancellationToken.None);
        Assert.NotNull(secondNullable);
        var secondResult = secondNullable.Value;

        Assert.Equal(firstResult.Profile.Id, secondResult.Profile.Id);
        Assert.Equal(2, secondResult.Profile.InteractionCount);
        Assert.False(secondResult.ShouldSuggestEnrollment);

        var provisionalProfiles = await store.GetProvisionalProfilesAsync(CancellationToken.None);
        Assert.Single(provisionalProfiles);
    }

    [Fact]
    public async Task UnknownTracker_ReachesThreshold_SuggestsEnrollment()
    {
        var store = new InMemorySpeakerProfileStore();
        var tracker = CreateUnknownSpeakerTracker(store, provisionalMatchThreshold: 0.7f, suggestEnrollmentAfter: 2);
        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.55f, 0.0005f));

        var firstEmbedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);
        await tracker.TrackUnknownSpeakerAsync(firstEmbedding, CancellationToken.None);

        var secondEmbedding = engine.ExtractEmbedding(CreateAudioSamples(phase: 0.2f), 16_000);
        var secondNullable = await tracker.TrackUnknownSpeakerAsync(secondEmbedding, CancellationToken.None);
        Assert.NotNull(secondNullable);
        var secondResult = secondNullable.Value;

        Assert.True(secondResult.ShouldSuggestEnrollment);
        Assert.Equal(2, secondResult.Profile.InteractionCount);
    }

    [Fact]
    public async Task AdaptiveUpdater_HighConfidence_UpdatesEmbedding()
    {
        var store = new InMemorySpeakerProfileStore();
        var originalEmbedding = CreateEmbeddingVector(0.2f, 0.001f);
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "alice",
                Name = "Alice",
                AverageEmbedding = originalEmbedding,
                Embeddings = [originalEmbedding],
                InteractionCount = 3,
            },
            CancellationToken.None);

        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.8f, 0.001f));
        var updater = CreateAdaptiveUpdater(store, adaptiveProfiles: true, adaptiveAlpha: 0.25f, highConfidenceThreshold: 0.85f);
        var embedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);

        await updater.TryUpdateAsync(
            new SpeakerIdentification
            {
                ProfileId = "alice",
                Name = "Alice",
                Similarity = 0.95f,
                IsAuthorized = true,
            },
            embedding,
            CancellationToken.None);

        var updatedProfile = await store.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(4, updatedProfile.InteractionCount);
        Assert.NotEqual(originalEmbedding, updatedProfile.AverageEmbedding);
        Assert.Equal(
            ((1 - 0.25f) * originalEmbedding[0]) + (0.25f * embedding.Vector[0]),
            updatedProfile.AverageEmbedding[0],
            precision: 4);
    }

    [Fact]
    public async Task AdaptiveUpdater_LowConfidence_SkipsUpdate()
    {
        var store = new InMemorySpeakerProfileStore();
        var originalEmbedding = CreateEmbeddingVector(0.3f, 0.001f);
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "alice",
                Name = "Alice",
                AverageEmbedding = originalEmbedding,
                Embeddings = [originalEmbedding],
                InteractionCount = 3,
            },
            CancellationToken.None);

        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.9f, 0.001f));
        var updater = CreateAdaptiveUpdater(store, adaptiveProfiles: true, adaptiveAlpha: 0.2f, highConfidenceThreshold: 0.9f);
        var embedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);

        await updater.TryUpdateAsync(
            new SpeakerIdentification
            {
                ProfileId = "alice",
                Name = "Alice",
                Similarity = 0.89f,
                IsAuthorized = true,
            },
            embedding,
            CancellationToken.None);

        var updatedProfile = await store.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(3, updatedProfile.InteractionCount);
        Assert.Equal(originalEmbedding, updatedProfile.AverageEmbedding);
    }

    [Fact]
    public async Task AdaptiveUpdater_Disabled_SkipsUpdate()
    {
        var store = new InMemorySpeakerProfileStore();
        var originalEmbedding = CreateEmbeddingVector(0.35f, 0.001f);
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "alice",
                Name = "Alice",
                AverageEmbedding = originalEmbedding,
                Embeddings = [originalEmbedding],
                InteractionCount = 2,
            },
            CancellationToken.None);

        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.85f, 0.001f));
        var updater = CreateAdaptiveUpdater(store, adaptiveProfiles: false);
        var embedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);

        await updater.TryUpdateAsync(
            new SpeakerIdentification
            {
                ProfileId = "alice",
                Name = "Alice",
                Similarity = 0.99f,
                IsAuthorized = true,
            },
            embedding,
            CancellationToken.None);

        var updatedProfile = await store.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(2, updatedProfile.InteractionCount);
        Assert.Equal(originalEmbedding, updatedProfile.AverageEmbedding);
    }

    [Fact]
    public async Task AdaptiveUpdater_ProvisionalProfile_SkipsUpdate()
    {
        var store = new InMemorySpeakerProfileStore();
        var originalEmbedding = CreateEmbeddingVector(0.45f, 0.001f);
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "unknown-1",
                Name = "Unknown Speaker 1",
                IsProvisional = true,
                IsAuthorized = false,
                AverageEmbedding = originalEmbedding,
                Embeddings = [originalEmbedding],
                InteractionCount = 1,
            },
            CancellationToken.None);

        var engine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.95f, 0.001f));
        var updater = CreateAdaptiveUpdater(store, adaptiveProfiles: true);
        var embedding = engine.ExtractEmbedding(CreateAudioSamples(), 16_000);

        await updater.TryUpdateAsync(
            new SpeakerIdentification
            {
                ProfileId = "unknown-1",
                Name = "Unknown Speaker 1",
                Similarity = 0.99f,
                IsAuthorized = false,
            },
            embedding,
            CancellationToken.None);

        var updatedProfile = await store.GetAsync("unknown-1", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(1, updatedProfile.InteractionCount);
        Assert.Equal(originalEmbedding, updatedProfile.AverageEmbedding);
    }

    [Fact]
    public void Filter_IgnoreDisabled_AllowsUnknown()
    {
        var filter = CreateSpeakerVerificationFilter(ignoreUnknownVoices: false);

        var shouldProcess = filter.ShouldProcessCommand(null);

        Assert.True(shouldProcess);
    }

    [Fact]
    public void Filter_IgnoreEnabled_BlocksUnknown()
    {
        var filter = CreateSpeakerVerificationFilter(ignoreUnknownVoices: true);

        var shouldProcess = filter.ShouldProcessCommand(null);

        Assert.False(shouldProcess);
    }

    [Fact]
    public void Filter_IgnoreEnabled_AllowsKnown()
    {
        var filter = CreateSpeakerVerificationFilter(ignoreUnknownVoices: true);

        var shouldProcess = filter.ShouldProcessCommand(
            new SpeakerIdentification
            {
                ProfileId = "alice",
                Name = "Alice",
                Similarity = 0.91f,
                IsAuthorized = true,
            });

        Assert.True(shouldProcess);
    }

    [Fact]
    public void Quality_LoudEnoughAudio_IsAcceptable()
    {
        var analyzer = CreateAudioQualityAnalyzer(minSampleDurationMs: 250);

        var report = analyzer.Analyze(CreateAudioSamples(length: 16_000, amplitude: 0.2f), 16_000);

        Assert.True(report.IsAcceptable);
        Assert.False(report.IsTooQuiet);
        Assert.False(report.IsTooShort);
    }

    [Fact]
    public void Quality_SilentAudio_IsTooQuiet()
    {
        var analyzer = CreateAudioQualityAnalyzer(minSampleDurationMs: 250);

        var report = analyzer.Analyze(new float[16_000], 16_000);

        Assert.True(report.IsTooQuiet);
        Assert.False(report.IsAcceptable);
    }

    [Fact]
    public void Quality_ShortAudio_IsTooShort()
    {
        var analyzer = CreateAudioQualityAnalyzer(minSampleDurationMs: 1_000);

        var report = analyzer.Analyze(CreateAudioSamples(length: 4_000, amplitude: 0.2f), 16_000);

        Assert.True(report.IsTooShort);
        Assert.False(report.IsAcceptable);
    }

    private static UnknownSpeakerTracker CreateUnknownSpeakerTracker(
        ISpeakerProfileStore store,
        float provisionalMatchThreshold = 0.75f,
        int suggestEnrollmentAfter = 3)
    {
        return new UnknownSpeakerTracker(
            store,
            Options.Create(
                new VoiceProfileOptions
                {
                    ProvisionalMatchThreshold = provisionalMatchThreshold,
                    SuggestEnrollmentAfter = suggestEnrollmentAfter,
                }),
            NullLogger<UnknownSpeakerTracker>.Instance);
    }

    private static AdaptiveProfileUpdater CreateAdaptiveUpdater(
        ISpeakerProfileStore store,
        bool adaptiveProfiles = true,
        float adaptiveAlpha = 0.2f,
        float highConfidenceThreshold = 0.85f)
    {
        return new AdaptiveProfileUpdater(
            store,
            Options.Create(
                new VoiceProfileOptions
                {
                    AdaptiveProfiles = adaptiveProfiles,
                    AdaptiveAlpha = adaptiveAlpha,
                    HighConfidenceThreshold = highConfidenceThreshold,
                }),
            NullLogger<AdaptiveProfileUpdater>.Instance);
    }

    private static SpeakerVerificationFilter CreateSpeakerVerificationFilter(bool ignoreUnknownVoices)
    {
        return new SpeakerVerificationFilter(
            Options.Create(new VoiceProfileOptions { IgnoreUnknownVoices = ignoreUnknownVoices }),
            NullLogger<SpeakerVerificationFilter>.Instance);
    }

    private static AudioQualityAnalyzer CreateAudioQualityAnalyzer(int minSampleDurationMs)
    {
        return new AudioQualityAnalyzer(
            Options.Create(new VoiceProfileOptions { MinSampleDurationMs = minSampleDurationMs }));
    }

    private static float[] CreateEmbeddingVector(float startValue, float increment)
    {
        return Enumerable.Range(0, 128)
            .Select(i => startValue + (i * increment))
            .ToArray();
    }

    private static float[] CreateAudioSamples(int length = 16_000, float amplitude = 0.15f, float phase = 0f)
    {
        return Enumerable.Range(0, length)
            .Select(i => MathF.Sin(((2 * MathF.PI * i) / 128) + phase) * amplitude)
            .ToArray();
    }
}
