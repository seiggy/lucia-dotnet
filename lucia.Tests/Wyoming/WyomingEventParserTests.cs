using System.Text;
using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEventParserTests
{
    [Fact]
    public async Task ParsesDescribeEvent()
    {
        var input = BuildMessage("{\"type\":\"describe\"}\n");
        var parser = new WyomingEventParser(input);

        var evt = await parser.ReadEventAsync();

        Assert.NotNull(evt);
        Assert.IsType<DescribeEvent>(evt);
        Assert.Equal("describe", evt.Type);
    }

    [Fact]
    public async Task ParsesAudioStartEvent()
    {
        var input = BuildMessage("{\"type\":\"audio-start\",\"data\":{\"rate\":16000,\"width\":2,\"channels\":1}}\n");
        var parser = new WyomingEventParser(input);

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

        var parser = new WyomingEventParser(new MemoryStream(combined));

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
        var input = BuildMessage("{\"type\":\"detect\",\"data\":{\"names\":[\"hey_lucia\"]}}\n");
        var parser = new WyomingEventParser(input);

        var evt = await parser.ReadEventAsync();

        var detect = Assert.IsType<DetectEvent>(evt);
        Assert.NotNull(detect.Names);
        Assert.Contains("hey_lucia", detect.Names);
    }

    [Fact]
    public async Task ParsesSynthesizeEvent()
    {
        var input = BuildMessage("{\"type\":\"synthesize\",\"data\":{\"text\":\"Hello world\",\"voice\":\"ryan\",\"language\":\"en\"}}\n");
        var parser = new WyomingEventParser(input);

        var evt = await parser.ReadEventAsync();

        var synth = Assert.IsType<SynthesizeEvent>(evt);
        Assert.Equal("Hello world", synth.Text);
        Assert.Equal("ryan", synth.Voice);
        Assert.Equal("en", synth.Language);
    }

    [Fact]
    public async Task ReturnsNullOnStreamClosed()
    {
        var parser = new WyomingEventParser(new MemoryStream([]));

        var evt = await parser.ReadEventAsync();

        Assert.Null(evt);
    }

    [Fact]
    public async Task ThrowsOnUnknownEventType()
    {
        var input = BuildMessage("{\"type\":\"bogus_event\"}\n");
        var parser = new WyomingEventParser(input);

        await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync());
    }

    [Fact]
    public async Task ParsesMultipleEventsSequentially()
    {
        var messages = "{\"type\":\"describe\"}\n{\"type\":\"audio-stop\"}\n";
        var parser = new WyomingEventParser(new MemoryStream(Encoding.UTF8.GetBytes(messages)));

        var evt1 = await parser.ReadEventAsync();
        var evt2 = await parser.ReadEventAsync();

        Assert.IsType<DescribeEvent>(evt1);
        Assert.IsType<AudioStopEvent>(evt2);
    }

    private static MemoryStream BuildMessage(string headerLine)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(headerLine));
    }
}
