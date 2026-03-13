using System.Text.Json;
using lucia.AgentHost.Apis;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
using lucia.Wyoming.WakeWord;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace lucia.Tests.Wyoming;

public sealed class WyomingStatusApiTests
{
    [Fact]
    public async Task GetWyomingStatus_ReturnsReadinessForAllServices()
    {
        var result = WyomingStatusApi.GetWyomingStatus(
            new TestSttEngine(new TestSttSession(new SttResult())),
            new TestWakeWordDetector(new TestWakeWordSession(null)),
            new TestDiarizationEngine(),
            CreateReadyManager());

        var payload = await ExecuteResultAsync(result);

        Assert.True(payload.GetProperty("stt").GetProperty("ready").GetBoolean());
        Assert.True(payload.GetProperty("wakeWord").GetProperty("ready").GetBoolean());
        Assert.True(payload.GetProperty("diarization").GetProperty("ready").GetBoolean());
        Assert.True(payload.GetProperty("customWakeWords").GetProperty("ready").GetBoolean());
        Assert.True(payload.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task GetWyomingStatus_ReturnsFalseWhenServicesAreNotConfigured()
    {
        var result = WyomingStatusApi.GetWyomingStatus(
            new UnreadySttEngine(),
            new UnreadyWakeWordDetector(),
            new UnreadyDiarizationEngine(),
            CreateUnreadyManager());

        var payload = await ExecuteResultAsync(result);

        Assert.False(payload.GetProperty("stt").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("wakeWord").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("diarization").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("customWakeWords").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("configured").GetBoolean());
    }

    private static CustomWakeWordManager CreateReadyManager()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "tokens.txt"), "▁HEY 0");

        return new CustomWakeWordManager(
            new InMemoryWakeWordStore(),
            new WakeWordTokenizer(),
            Microsoft.Extensions.Options.Options.Create(new WakeWordOptions
            {
                ModelPath = modelPath,
                KeywordsFile = "keywords.txt",
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomWakeWordManager>.Instance);
    }

    private static CustomWakeWordManager CreateUnreadyManager() =>
        new(
            new InMemoryWakeWordStore(),
            new WakeWordTokenizer(),
            Microsoft.Extensions.Options.Options.Create(new WakeWordOptions
            {
                ModelPath = string.Empty,
                KeywordsFile = "keywords.txt",
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomWakeWordManager>.Instance);

    private static async Task<JsonElement> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    private sealed class UnreadySttEngine : ISttEngine
    {
        public bool IsReady => false;

        public ISttSession CreateSession() => throw new InvalidOperationException();

        public void Dispose()
        {
        }
    }

    private sealed class UnreadyWakeWordDetector : IWakeWordDetector
    {
        public bool IsReady => false;

        public IWakeWordSession CreateSession() => throw new InvalidOperationException();

        public void Dispose()
        {
        }
    }

    private sealed class UnreadyDiarizationEngine : IDiarizationEngine
    {
        public bool IsReady => false;

        public SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate) =>
            throw new InvalidOperationException();

        public SpeakerIdentification? IdentifySpeaker(
            SpeakerEmbedding embedding,
            IReadOnlyList<SpeakerProfile> enrolledProfiles,
            float threshold = 0.7f) =>
            throw new InvalidOperationException();

        public void Dispose()
        {
        }
    }
}
