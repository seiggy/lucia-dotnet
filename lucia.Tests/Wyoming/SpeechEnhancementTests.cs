using System.Reflection;
using lucia.Tests.TestDoubles;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace lucia.Tests.Wyoming;

public sealed class SpeechEnhancementTests
{
    [Fact]
    public void GtcrnSpeechEnhancer_EmptyModelPath_IsNotReady()
    {
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = string.Empty }),
            new TestModelChangeNotifier(),
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);

        Assert.False(enhancer.IsReady);
    }

    [Fact]
    public void GtcrnSpeechEnhancer_CreateSession_ThrowsWhenNotReady()
    {
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = string.Empty }),
            new TestModelChangeNotifier(),
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);

        Assert.Throws<InvalidOperationException>(() => enhancer.CreateSession());
    }

    [Fact]
    public void GtcrnSpeechEnhancer_IgnoresNonEnhancementEvents()
    {
        var notifier = new TestModelChangeNotifier();
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);

        Assert.False(enhancer.IsReady);

        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Stt,
            ModelId = "some-stt-model",
            ModelPath = "/tmp/nonexistent-stt-model",
        });

        Assert.False(enhancer.IsReady);
    }

    [Fact]
    public void GtcrnSpeechEnhancer_AttemptsReloadOnEnhancementEvent()
    {
        var notifier = new TestModelChangeNotifier();
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);

        Assert.False(enhancer.IsReady);

        var exception = Record.Exception(() =>
            notifier.Raise(new ActiveModelChangedEvent
            {
                EngineType = EngineType.SpeechEnhancement,
                ModelId = "gtcrn_simple",
                ModelPath = "/tmp/nonexistent-enhancement-model",
            }));

        Assert.Null(exception);
        Assert.False(enhancer.IsReady);
    }

    [Fact]
    public void GtcrnSpeechEnhancer_ReloadKeepsOldStreamAndNewStreamUsesNewSession()
    {
        var modelPath = Path.Combine(
            FindRepoRoot(),
            "lucia.Tests/TestData/gtcrn-streaming-test.onnx");
        var notifier = new TestModelChangeNotifier();
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);
        using var oldStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        var oldSession = GetInferenceSession(oldStream);

        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.SpeechEnhancement,
            ModelId = "replacement",
            ModelPath = modelPath,
        });

        using var newStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        var newSession = GetInferenceSession(newStream);

        Assert.NotSame(oldSession, newSession);
        Assert.Equal(256, oldStream.Process(new float[512]).Length);
        Assert.Equal(256, newStream.Process(new float[512]).Length);
    }

    [Fact]
    public async Task GtcrnSpeechEnhancer_ConcurrentReloadsAndStreamDisposal_LeaveCurrentModelUsable()
    {
        var modelPath = Path.Combine(
            FindRepoRoot(),
            "lucia.Tests/TestData/gtcrn-streaming-test.onnx");
        var notifier = new TestModelChangeNotifier();
        using var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance);
        var oldStreams = Enumerable.Range(0, 8)
            .Select(_ => Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession()))
            .ToArray();
        using var start = new ManualResetEventSlim();

        var reloadTasks = Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            start.Wait();
            notifier.Raise(new ActiveModelChangedEvent
            {
                EngineType = EngineType.SpeechEnhancement,
                ModelId = $"replacement-{index}",
                ModelPath = modelPath,
            });
        }));
        var disposeTasks = oldStreams.Select(stream => Task.Run(() =>
        {
            start.Wait();
            stream.Dispose();
        }));

        start.Set();
        await Task.WhenAll(reloadTasks.Concat(disposeTasks));

        using var currentStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        Assert.Equal(256, currentStream.Process(new float[512]).Length);
        Assert.All(oldStreams, stream =>
            Assert.Throws<ObjectDisposedException>(() => stream.Process([0.1f])));
    }

    [Fact]
    public void SpeechEnhancementCatalog_HasExpectedModels()
    {
        var sttMonitor = new OptionsMonitorStub<SttModelOptions>(new SttModelOptions());
        var vadMonitor = new OptionsMonitorStub<VadOptions>(new VadOptions());
        var wakeMonitor = new OptionsMonitorStub<WakeWordOptions>(new WakeWordOptions());
        var diarizationMonitor = new OptionsMonitorStub<DiarizationOptions>(new DiarizationOptions());
        var enhancementMonitor = new OptionsMonitorStub<SpeechEnhancementOptions>(new SpeechEnhancementOptions());

        var provider = new SherpaOnnxCatalogProvider(sttMonitor, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor);
        var catalog = new ModelCatalogService(
            new IModelCatalogProvider[] { provider },
            sttMonitor, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor,
            NullLogger<ModelCatalogService>.Instance);

        var models = catalog.GetAvailableModels(EngineType.SpeechEnhancement);

        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "gtcrn_simple");
        Assert.Contains(models, m => m.Id == "gtcrn");
        Assert.All(models, m => Assert.False(m.IsArchive,
            $"Speech enhancement model '{m.Id}' should not be an archive"));
    }

    private sealed class TestModelChangeNotifier : IModelChangeNotifier
    {
        public event Action<ActiveModelChangedEvent>? ActiveModelChanged;

        public void Raise(ActiveModelChangedEvent evt) => ActiveModelChanged?.Invoke(evt);
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static InferenceSession GetInferenceSession(GtcrnStreamingSession stream)
    {
        var field = typeof(GtcrnStreamingSession).GetField(
            "_session",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<InferenceSession>(field?.GetValue(stream));
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "lucia-dotnet.slnx")))
        {
            directory = Directory.GetParent(directory)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        return directory;
    }
}
