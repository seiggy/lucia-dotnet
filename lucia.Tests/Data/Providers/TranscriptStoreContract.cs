using lucia.Wyoming.Telemetry;

namespace lucia.Tests.Data.Providers;

public abstract class TranscriptStoreContract
{
    protected abstract Task<StoreScope<ITranscriptStore>> CreateAsync();

    [Fact]
    public async Task GetAsync_EmptyStore_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetAsync("missing-id", CancellationToken.None));
        Assert.Empty(await scope.Store.QueryAsync(limit: 10, ct: CancellationToken.None));
        Assert.Empty(await scope.Store.GetRecentAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_AndGetAsync_RoundTripAllMeaningfulFields()
    {
        await using var scope = await CreateAsync();

        var record = CreateRecord(
            "tx-1",
            "session-a",
            new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
            "speaker-1",
            "hello world");

        await scope.Store.SaveAsync(record, CancellationToken.None);

        var stored = await scope.Store.GetAsync(record.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(record.Id, stored.Id);
        Assert.Equal(record.SessionId, stored.SessionId);
        Assert.Equal(record.Timestamp, stored.Timestamp);
        Assert.Equal(record.Text, stored.Text);
        Assert.Equal(record.Confidence, stored.Confidence);
        Assert.Equal(record.AudioDurationMs, stored.AudioDurationMs);
        Assert.Equal(record.SampleRate, stored.SampleRate);
        Assert.Equal(record.SampleCount, stored.SampleCount);
        Assert.Equal(record.SttModelId, stored.SttModelId);
        Assert.Equal(record.VadModelId, stored.VadModelId);
        Assert.Equal(record.VadActive, stored.VadActive);
        Assert.Equal(record.DiarizationModelId, stored.DiarizationModelId);
        Assert.Equal(record.DiarizationActive, stored.DiarizationActive);
        Assert.Equal(record.SpeakerId, stored.SpeakerId);
        Assert.Equal(record.SpeakerName, stored.SpeakerName);
        Assert.Equal(record.SpeakerSimilarity, stored.SpeakerSimilarity);
        Assert.Equal(record.IsProvisionalSpeaker, stored.IsProvisionalSpeaker);
        Assert.Equal(record.NewProfileCreated, stored.NewProfileCreated);
        Assert.Equal(record.RouteResult, stored.RouteResult);
        Assert.Equal(record.MatchedSkill, stored.MatchedSkill);
        Assert.Equal(record.RouteConfidence, stored.RouteConfidence);
        Assert.Equal(record.CommandFiltered, stored.CommandFiltered);
        Assert.Equal(record.AudioSource, stored.AudioSource);
        Assert.Equal(record.Stages, stored.Stages);
        Assert.Equal(record.ResponseText, stored.ResponseText);
    }

    [Fact]
    public async Task QueryAsync_FiltersBySessionAndOrdersByTimestampDescending()
    {
        await using var scope = await CreateAsync();

        var earlier = CreateRecord("tx-1", "session-a", new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), "speaker-1", "earlier");
        var later = CreateRecord("tx-2", "session-a", new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), "speaker-2", "later");
        var other = CreateRecord("tx-3", "session-b", new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero), "speaker-1", "other");

        await scope.Store.SaveAsync(earlier, CancellationToken.None);
        await scope.Store.SaveAsync(later, CancellationToken.None);
        await scope.Store.SaveAsync(other, CancellationToken.None);

        var bySession = await scope.Store.QueryAsync(sessionId: "session-a", ct: CancellationToken.None);
        var idsBySession = bySession.Select(r => r.Id).ToArray();
        Assert.Equal(["tx-2", "tx-1"], idsBySession);

        var bySpeaker = await scope.Store.QueryAsync(speakerId: "speaker-1", ct: CancellationToken.None);
        var idsBySpeaker = bySpeaker.Select(r => r.Id).ToArray();
        Assert.Equal(["tx-3", "tx-1"], idsBySpeaker);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsLimitAndReturnsNewestFirst()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SaveAsync(CreateRecord("tx-1", "session-a", new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero), "speaker-1", "one"), CancellationToken.None);
        await scope.Store.SaveAsync(CreateRecord("tx-2", "session-a", new DateTimeOffset(2025, 1, 2, 3, 0, 0, TimeSpan.Zero), "speaker-1", "two"), CancellationToken.None);
        await scope.Store.SaveAsync(CreateRecord("tx-3", "session-a", new DateTimeOffset(2025, 1, 2, 5, 0, 0, TimeSpan.Zero), "speaker-1", "three"), CancellationToken.None);

        var recent = await scope.Store.GetRecentAsync(2, CancellationToken.None);

        Assert.Equal(["tx-3", "tx-2"], recent.Select(r => r.Id).ToArray());
    }

    private static TranscriptRecord CreateRecord(
        string id,
        string sessionId,
        DateTimeOffset timestamp,
        string speakerId,
        string text)
    {
        return new TranscriptRecord
        {
            Id = id,
            SessionId = sessionId,
            Timestamp = timestamp,
            Text = text,
            Confidence = 0.98f,
            AudioDurationMs = 1234.5,
            SampleRate = 16000,
            SampleCount = 24000,
            SttModelId = "stt-model",
            VadModelId = "vad-model",
            VadActive = true,
            DiarizationModelId = "diarization-model",
            DiarizationActive = true,
            SpeakerId = speakerId,
            SpeakerName = "Speaker One",
            SpeakerSimilarity = 0.92f,
            IsProvisionalSpeaker = true,
            NewProfileCreated = true,
            RouteResult = "assistant",
            MatchedSkill = "light-control",
            RouteConfidence = 0.88f,
            CommandFiltered = false,
            AudioSource = "enhanced",
            Stages =
            [
                new PipelineStageTiming { Name = "stt", DurationMs = 45.0, Success = true },
                new PipelineStageTiming { Name = "tts", DurationMs = 30.0, Success = true },
            ],
            ResponseText = "ok",
        };
    }
}
