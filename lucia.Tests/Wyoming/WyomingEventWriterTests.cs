using System.Text;
using System.Text.Json;
using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEventWriterTests
{
    private static readonly WyomingOptions DefaultOptions = new();

    [Fact]
    public async Task WritesTranscriptEventHeader()
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var evt = new TranscriptEvent
        {
            Text = "Hello world",
            Confidence = 0.92f,
        };

        await writer.WriteEventAsync(evt);

        var (header, data) = ReadHeaderAndData(stream);
        using (header)
        {
            Assert.Equal("transcript", header.RootElement.GetProperty("type").GetString());
            Assert.Equal(0, header.RootElement.GetProperty("payload_length").GetInt32());
            Assert.NotNull(data);
            using (data)
            {
                Assert.Equal("Hello world", data.RootElement.GetProperty("text").GetString());
                Assert.Equal(0.92f, data.RootElement.GetProperty("confidence").GetSingle());
            }
        }
    }

    [Fact]
    public async Task WritesAudioChunkEventWithBinaryPayload()
    {
        var payload = new byte[640];
        Random.Shared.NextBytes(payload);

        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var evt = new AudioChunkEvent
        {
            Rate = 16000,
            Width = 2,
            Channels = 1,
            Payload = payload,
        };

        await writer.WriteEventAsync(evt);

        var bytes = stream.ToArray();
        var newlineIndex = Array.IndexOf(bytes, (byte)'\n');
        Assert.True(newlineIndex >= 0);

        using var header = JsonDocument.Parse(bytes.AsMemory(0, newlineIndex));
        Assert.Equal("audio-chunk", header.RootElement.GetProperty("type").GetString());
        Assert.Equal(payload.Length, header.RootElement.GetProperty("payload_length").GetInt32());

        var dataLength = header.RootElement.GetProperty("data_length").GetInt32();
        Assert.True(dataLength > 0);

        var dataStart = newlineIndex + 1;
        using var data = JsonDocument.Parse(bytes.AsMemory(dataStart, dataLength));
        Assert.Equal(16000, data.RootElement.GetProperty("rate").GetInt32());
        Assert.Equal(2, data.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(1, data.RootElement.GetProperty("channels").GetInt32());

        var payloadBytes = bytes[(dataStart + dataLength)..];
        Assert.Equal(payload, payloadBytes);
    }

    [Fact]
    public async Task WritesInfoEventWithNestedArrays()
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var evt = new InfoEvent
        {
            Asr =
            [
                new AsrInfo
                {
                    Name = "whisper",
                    Description = "Speech recognition",
                    Version = "1.0.0",
                    Languages = ["en", "sv"],
                    Installed = true,
                },
            ],
            Tts =
            [
                new TtsInfo
                {
                    Name = "piper",
                    Description = "Speech synthesis",
                    Version = "2.0.0",
                    Languages = ["en"],
                    Installed = true,
                    Voices =
                    [
                        new TtsVoiceInfo
                        {
                            Name = "ryan",
                            Language = "en",
                            Description = "English voice",
                        },
                    ],
                },
            ],
            Wake =
            [
                new WakeInfo
                {
                    Name = "hey_lucia",
                    Description = "Wake word",
                    Version = "3.0.0",
                    Languages = ["en"],
                    Installed = false,
                },
            ],
            Version = "2025.03.13",
        };

        await writer.WriteEventAsync(evt);

        var (header, dataDoc) = ReadHeaderAndData(stream);
        using (header)
        {
            Assert.Equal("info", header.RootElement.GetProperty("type").GetString());
        }

        Assert.NotNull(dataDoc);
        using (dataDoc)
        {
            var data = dataDoc.RootElement;

            var asr = Assert.Single(data.GetProperty("asr").EnumerateArray());
            Assert.Equal("whisper", asr.GetProperty("name").GetString());
            Assert.Equal("Speech recognition", asr.GetProperty("description").GetString());
            Assert.Equal("1.0.0", asr.GetProperty("version").GetString());
            var languages = asr.GetProperty("languages")
                .EnumerateArray()
                .Select(static item => item.GetString() ?? string.Empty)
                .ToArray();
            Assert.Equal(new[] { "en", "sv" }, languages);
            Assert.True(asr.GetProperty("installed").GetBoolean());

            var tts = Assert.Single(data.GetProperty("tts").EnumerateArray());
            Assert.Equal("piper", tts.GetProperty("name").GetString());
            var voices = Assert.Single(tts.GetProperty("voices").EnumerateArray());
            Assert.Equal("ryan", voices.GetProperty("name").GetString());
            Assert.Equal("en", voices.GetProperty("language").GetString());
            Assert.Equal("English voice", voices.GetProperty("description").GetString());

            var wake = Assert.Single(data.GetProperty("wake").EnumerateArray());
            Assert.Equal("hey_lucia", wake.GetProperty("name").GetString());
            Assert.False(wake.GetProperty("installed").GetBoolean());
            Assert.Equal("2025.03.13", data.GetProperty("version").GetString());
        }
    }

    [Fact]
    public async Task WritesErrorEvent()
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var evt = new ErrorEvent
        {
            Text = "Something went wrong",
            Code = "bad_request",
        };

        await writer.WriteEventAsync(evt);

        var (header, dataDoc) = ReadHeaderAndData(stream);
        using (header)
        {
            Assert.Equal("error", header.RootElement.GetProperty("type").GetString());
        }

        Assert.NotNull(dataDoc);
        using (dataDoc)
        {
            Assert.Equal("Something went wrong", dataDoc.RootElement.GetProperty("text").GetString());
            Assert.Equal("bad_request", dataDoc.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task WritesDetectionEvent()
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var evt = new DetectionEvent
        {
            Name = "hey_lucia",
            Timestamp = 123456789L,
        };

        await writer.WriteEventAsync(evt);

        var (header, dataDoc) = ReadHeaderAndData(stream);
        using (header)
        {
            Assert.Equal("detection", header.RootElement.GetProperty("type").GetString());
        }

        Assert.NotNull(dataDoc);
        using (dataDoc)
        {
            Assert.Equal("hey_lucia", dataDoc.RootElement.GetProperty("name").GetString());
            Assert.Equal(123456789L, dataDoc.RootElement.GetProperty("timestamp").GetInt64());
        }
    }

    [Fact]
    public async Task ConcurrentWritesDoNotCorruptOutput()
    {
        var stream = new MemoryStream();
        var writer = new WyomingEventWriter(stream);
        var events = Enumerable.Range(1, 8)
            .Select(index => new TranscriptEvent
            {
                Text = $"message-{index}",
                Confidence = index / 10f,
            })
            .ToArray();

        var writeTasks = Array.ConvertAll(events, evt => writer.WriteEventAsync(evt));

        await Task.WhenAll(writeTasks);

        stream.Position = 0;
        var parser = new WyomingEventParser(stream, DefaultOptions);
        var actualTexts = new List<string>();

        while (await parser.ReadEventAsync() is TranscriptEvent transcript)
        {
            actualTexts.Add(transcript.Text);
        }

        Assert.Equal(events.Length, actualTexts.Count);
        Assert.Equal(
            events.Select(static item => item.Text).OrderBy(static item => item),
            actualTexts.OrderBy(static item => item));
    }

    private static (JsonDocument Header, JsonDocument? Data) ReadHeaderAndData(MemoryStream stream)
    {
        var bytes = stream.ToArray();
        var newlineIndex = Array.IndexOf(bytes, (byte)'\n');
        Assert.True(newlineIndex >= 0);

        var header = JsonDocument.Parse(bytes.AsMemory(0, newlineIndex));

        JsonDocument? data = null;
        if (header.RootElement.TryGetProperty("data_length", out var dl) && dl.GetInt32() > 0)
        {
            var dataLength = dl.GetInt32();
            var dataStart = newlineIndex + 1;
            data = JsonDocument.Parse(bytes.AsMemory(dataStart, dataLength));
        }

        return (header, data);
    }
}
