using System.Text;
using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEventParserTests
{
    private static readonly WyomingOptions DefaultOptions = new();

    [Fact]
    public async Task ParsesDescribeEvent()
    {
        var parser = CreateParser("{\"type\":\"describe\"}\n");

        var evt = await parser.ReadEventAsync();

        Assert.NotNull(evt);
        Assert.IsType<DescribeEvent>(evt);
        Assert.Equal("describe", evt.Type);
    }

    [Fact]
    public async Task ParsesAudioStartEvent()
    {
        var parser = CreateParser("{\"type\":\"audio-start\",\"data\":{\"rate\":16000,\"width\":2,\"channels\":1}}\n");

        var evt = await parser.ReadEventAsync();

        Assert.NotNull(evt);
        var audioStart = Assert.IsType<AudioStartEvent>(evt);
        Assert.Equal(16000, audioStart.Rate);
        Assert.Equal(2, audioStart.Width);
        Assert.Equal(1, audioStart.Channels);
    }

    [Fact]
    public async Task ParsesAudioChunkWithPayload()
    {
        var payload = new byte[640];
        Random.Shared.NextBytes(payload);

        var header = $"{{\"type\":\"audio-chunk\",\"data\":{{\"rate\":16000,\"width\":2,\"channels\":1}},\"payload_length\":{payload.Length}}}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var combined = new byte[headerBytes.Length + payload.Length];
        headerBytes.CopyTo(combined, 0);
        payload.CopyTo(combined, headerBytes.Length);

        var parser = new WyomingEventParser(new MemoryStream(combined), DefaultOptions);

        var evt = await parser.ReadEventAsync();

        Assert.NotNull(evt);
        var chunk = Assert.IsType<AudioChunkEvent>(evt);
        Assert.Equal(16000, chunk.Rate);
        Assert.Equal(2, chunk.Width);
        Assert.Equal(1, chunk.Channels);
        Assert.NotNull(chunk.Payload);
        Assert.Equal(640, chunk.Payload.Length);
        Assert.Equal(payload, chunk.Payload);
    }

    [Fact]
    public async Task ParsesDetectEvent()
    {
        var parser = CreateParser("{\"type\":\"detect\",\"data\":{\"names\":[\"hey_lucia\"]}}\n");

        var evt = await parser.ReadEventAsync();

        var detect = Assert.IsType<DetectEvent>(evt);
        Assert.NotNull(detect.Names);
        Assert.Contains("hey_lucia", detect.Names);
    }

    [Fact]
    public async Task ParsesSynthesizeEvent()
    {
        var parser = CreateParser("{\"type\":\"synthesize\",\"data\":{\"text\":\"Hello world\",\"voice\":\"ryan\",\"language\":\"en\"}}\n");

        var evt = await parser.ReadEventAsync();

        var synth = Assert.IsType<SynthesizeEvent>(evt);
        Assert.Equal("Hello world", synth.Text);
        Assert.Equal("ryan", synth.Voice);
        Assert.Equal("en", synth.Language);
    }

    [Fact]
    public async Task ReturnsNullOnStreamClosed()
    {
        var parser = new WyomingEventParser(new MemoryStream([]), DefaultOptions);

        var evt = await parser.ReadEventAsync();

        Assert.Null(evt);
    }

    [Fact]
    public async Task ThrowsOnUnknownEventType()
    {
        var parser = CreateParser("{\"type\":\"bogus_event\"}\n");

        await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());
    }

    [Fact]
    public async Task ParsesMultipleEventsSequentially()
    {
        var messages = "{\"type\":\"describe\"}\n{\"type\":\"audio-stop\"}\n";
        var parser = new WyomingEventParser(new MemoryStream(Encoding.UTF8.GetBytes(messages)), DefaultOptions);

        var evt1 = await parser.ReadEventAsync();
        var evt2 = await parser.ReadEventAsync();

        Assert.IsType<DescribeEvent>(evt1);
        Assert.IsType<AudioStopEvent>(evt2);
    }

    [Fact]
    public async Task ThrowsWhenDataLengthExceedsConfiguredMaximum()
    {
        var options = new WyomingOptions { MaxDataLength = 8 };
        var parser = CreateParser("{\"type\":\"audio-start\",\"data_length\":9}\n{}", options);

        var ex = await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());

        Assert.Equal("data_length 9 exceeds maximum 8", ex.Message);
    }

    [Fact]
    public async Task ThrowsWhenPayloadLengthExceedsConfiguredMaximum()
    {
        var options = new WyomingOptions { MaxPayloadLength = 8 };
        var parser = CreateParser("{\"type\":\"audio-chunk\",\"payload_length\":9}\n", options);

        var ex = await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());

        Assert.Equal("payload_length 9 exceeds maximum 8", ex.Message);
    }

    [Fact]
    public async Task RejectsOversizedPayloadLength()
    {
        var opts = new WyomingOptions { MaxPayloadLength = 1024 };
        var header = "{\"type\":\"audio-chunk\",\"data\":{\"rate\":16000,\"width\":2,\"channels\":1},\"payload_length\":999999}\n";
        var parser = CreateParser(header, opts);

        await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());
    }

    [Fact]
    public async Task RejectsOversizedDataLength()
    {
        var opts = new WyomingOptions { MaxDataLength = 100 };
        var parser = CreateParser("{\"type\":\"describe\",\"data_length\":999999}\n", opts);

        await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());
    }

    [Fact]
    public async Task ParsesEmptyJsonHeaderGracefully()
    {
        var parser = CreateParser("{}\n");

        await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());
    }

    [Fact]
    public async Task ThrowsWhenReadTimeoutExceeded()
    {
        var options = new WyomingOptions { ReadTimeoutSeconds = 1 };
        var parser = new WyomingEventParser(
            new DelayedReadStream(Encoding.UTF8.GetBytes("{\"type\":\"describe\"}\n"), TimeSpan.FromSeconds(2)),
            options);

        var ex = await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());

        Assert.Equal("Read timeout exceeded", ex.Message);
    }

    private static WyomingEventParser CreateParser(string input, WyomingOptions? options = null)
    {
        return new WyomingEventParser(
            new MemoryStream(Encoding.UTF8.GetBytes(input)),
            options ?? new WyomingOptions());
    }
}
