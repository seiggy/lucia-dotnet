using System.Text.Json;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingStatusApiTests
{
    [Fact]
    public async Task GetWyomingStatus_ReturnsReadinessForAllServices()
    {
        var result = WyomingStatusApi.GetWyomingStatus(
            new TestSttEngine(new TestSttSession(new SttResult())),
            null,
            new TestWakeWordDetector(new TestWakeWordSession(null)),
            new TestDiarizationEngine(),
            null,
            CreateReadyManager(),
            CreateModelManager());

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
            null,
            new UnreadyWakeWordDetector(),
            new UnreadyDiarizationEngine(),
            null,
            CreateUnreadyManager(),
            CreateModelManager());

        var payload = await ExecuteResultAsync(result);

        Assert.False(payload.GetProperty("stt").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("wakeWord").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("diarization").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("customWakeWords").GetProperty("ready").GetBoolean());
        Assert.False(payload.GetProperty("configured").GetBoolean());
    }

    private static ModelManager CreateModelManager()
    {
        var sttMonitor = new OptionsMonitorStub<SttModelOptions>(new SttModelOptions());
        var vadMonitor = new OptionsMonitorStub<VadOptions>(new VadOptions());
        var wakeMonitor = new OptionsMonitorStub<WakeWordOptions>(new WakeWordOptions());
        var diarizationMonitor = new OptionsMonitorStub<DiarizationOptions>(new DiarizationOptions());
        var enhancementMonitor = new OptionsMonitorStub<SpeechEnhancementOptions>(new SpeechEnhancementOptions());
        var catalog = new ModelCatalogService(sttMonitor, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor);
        var downloader = new ModelDownloader(
            A.Fake<IHttpClientFactory>(), NullLogger<ModelDownloader>.Instance);

        return new ModelManager(
            sttMonitor, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor,
            catalog, downloader, NullLogger<ModelManager>.Instance);
    }

    private static CustomWakeWordManager CreateReadyManager()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "tokens.txt"), "▁HEY 0");

        return new CustomWakeWordManager(
            new InMemoryWakeWordStore(),
            new WakeWordTokenizer(),
            Options.Create(new WakeWordOptions
            {
                ModelPath = modelPath,
                KeywordsFile = "keywords.txt",
            }),
            NullLogger<CustomWakeWordManager>.Instance);
    }

    private static CustomWakeWordManager CreateUnreadyManager() =>
        new(
            new InMemoryWakeWordStore(),
            new WakeWordTokenizer(),
            Options.Create(new WakeWordOptions
            {
                ModelPath = string.Empty,
                KeywordsFile = "keywords.txt",
            }),
            NullLogger<CustomWakeWordManager>.Instance);

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

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
