using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEventRoundTripTests
{
    [Fact]
    public async Task RoundTripsAudioStartEvent()
    {
        var evt = new AudioStartEvent
        {
            Rate = 16000,
            Width = 2,
            Channels = 1,
        };

        var roundTripped = await RoundTripAsync(evt);

        var audioStart = Assert.IsType<AudioStartEvent>(roundTripped);
        Assert.Equal(evt.Rate, audioStart.Rate);
        Assert.Equal(evt.Width, audioStart.Width);
        Assert.Equal(evt.Channels, audioStart.Channels);
    }

    [Fact]
    public async Task RoundTripsAudioChunkEventWithPayload()
    {
        var payload = new byte[640];
        Random.Shared.NextBytes(payload);

        var evt = new AudioChunkEvent
        {
            Rate = 16000,
            Width = 2,
            Channels = 1,
            Payload = payload,
        };

        var roundTripped = await RoundTripAsync(evt);

        var chunk = Assert.IsType<AudioChunkEvent>(roundTripped);
        Assert.Equal(evt.Rate, chunk.Rate);
        Assert.Equal(evt.Width, chunk.Width);
        Assert.Equal(evt.Channels, chunk.Channels);
        Assert.Equal(evt.Payload, chunk.Payload);
    }

    [Fact]
    public async Task RoundTripsTranscriptEvent()
    {
        var evt = new TranscriptEvent
        {
            Text = "hello world",
            Confidence = 0.85f,
        };

        var roundTripped = await RoundTripAsync(evt);

        var transcript = Assert.IsType<TranscriptEvent>(roundTripped);
        Assert.Equal(evt.Text, transcript.Text);
        Assert.Equal(evt.Confidence, transcript.Confidence);
    }

    [Fact]
    public async Task RoundTripsDetectionEvent()
    {
        var evt = new DetectionEvent
        {
            Name = "hey_lucia",
            Timestamp = 987654321L,
        };

        var roundTripped = await RoundTripAsync(evt);

        var detection = Assert.IsType<DetectionEvent>(roundTripped);
        Assert.Equal(evt.Name, detection.Name);
        Assert.Equal(evt.Timestamp, detection.Timestamp);
    }

    [Fact]
    public async Task RoundTripsErrorEvent()
    {
        var evt = new ErrorEvent
        {
            Text = "unable to synthesize",
            Code = "synthesis_failed",
        };

        var roundTripped = await RoundTripAsync(evt);

        var error = Assert.IsType<ErrorEvent>(roundTripped);
        Assert.Equal(evt.Text, error.Text);
        Assert.Equal(evt.Code, error.Code);
    }

    private static async Task<WyomingEvent> RoundTripAsync(WyomingEvent evt)
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);

        await writer.WriteEventAsync(evt);

        stream.Position = 0;
        var parser = new WyomingEventParser(stream);

        return Assert.IsAssignableFrom<WyomingEvent>(await parser.ReadEventAsync());
    }
}
