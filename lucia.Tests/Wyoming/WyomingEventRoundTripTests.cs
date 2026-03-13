using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEventRoundTripTests
{
    private static readonly WyomingOptions DefaultOptions = new();

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
    public async Task RoundTripsPartialTranscriptEvent()
    {
        var evt = new PartialTranscriptEvent
        {
            Text = "hello",
            Confidence = 0.42f,
            IsFinal = true,
        };

        var roundTripped = await RoundTripAsync(evt);

        var transcript = Assert.IsType<PartialTranscriptEvent>(roundTripped);
        Assert.Equal(evt.Text, transcript.Text);
        Assert.Equal(evt.Confidence, transcript.Confidence);
        Assert.Equal(evt.IsFinal, transcript.IsFinal);
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
    public async Task RoundTripsVoiceStartedEventWithTimestamp()
    {
        var evt = new VoiceStartedEvent { Timestamp = 123456789L };

        var roundTripped = await RoundTripAsync(evt);

        var voiceStarted = Assert.IsType<VoiceStartedEvent>(roundTripped);
        Assert.Equal(evt.Timestamp, voiceStarted.Timestamp);
    }

    [Fact]
    public async Task RoundTripsVoiceStartedEventWithoutTimestamp()
    {
        var evt = new VoiceStartedEvent();

        var roundTripped = await RoundTripAsync(evt);

        var voiceStarted = Assert.IsType<VoiceStartedEvent>(roundTripped);
        Assert.Null(voiceStarted.Timestamp);
    }

    [Fact]
    public async Task RoundTripsVoiceStoppedEvent()
    {
        var evt = new VoiceStoppedEvent { Timestamp = 222222222L };

        var roundTripped = await RoundTripAsync(evt);

        var voiceStopped = Assert.IsType<VoiceStoppedEvent>(roundTripped);
        Assert.Equal(evt.Timestamp, voiceStopped.Timestamp);
    }

    [Fact]
    public async Task RoundTripsNotDetectedEvent()
    {
        var roundTripped = await RoundTripAsync(new NotDetectedEvent());

        Assert.IsType<NotDetectedEvent>(roundTripped);
    }

    [Fact]
    public async Task RoundTripsSynthesizeEvent()
    {
        var evt = new SynthesizeEvent
        {
            Text = "hello from lucia",
            Voice = "ryan",
            Language = "en",
        };

        var roundTripped = await RoundTripAsync(evt);

        var synthesize = Assert.IsType<SynthesizeEvent>(roundTripped);
        Assert.Equal(evt.Text, synthesize.Text);
        Assert.Equal(evt.Voice, synthesize.Voice);
        Assert.Equal(evt.Language, synthesize.Language);
    }

    [Fact]
    public async Task RoundTripsTranscribeEvent()
    {
        var evt = new TranscribeEvent
        {
            Name = "default",
            Language = "en",
        };

        var roundTripped = await RoundTripAsync(evt);

        var transcribe = Assert.IsType<TranscribeEvent>(roundTripped);
        Assert.Equal(evt.Name, transcribe.Name);
        Assert.Equal(evt.Language, transcribe.Language);
    }

    [Fact]
    public async Task RoundTripsDetectEventWithNullNames()
    {
        var evt = new DetectEvent { Names = null };

        var roundTripped = await RoundTripAsync(evt);

        var detect = Assert.IsType<DetectEvent>(roundTripped);
        Assert.Null(detect.Names);
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
        var parser = new WyomingEventParser(stream, DefaultOptions);

        return Assert.IsAssignableFrom<WyomingEvent>(await parser.ReadEventAsync());
    }
}
