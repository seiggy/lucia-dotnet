using System.Collections.Concurrent;
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
    public async Task GtcrnSpeechEnhancer_ActiveProcessReloadsWithoutDisposingOldSession()
    {
        var modelPath = GetTestModelPath();
        var notifier = new TestModelChangeNotifier();
        var sessions = new ConcurrentQueue<InferenceSession>();
        var disposeCounts = new ConcurrentDictionary<InferenceSession, int>();
        using var runEntered = new ManualResetEventSlim();
        using var releaseRun = new ManualResetEventSlim();
        var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance,
            (path, options) => CreateTrackedHolder(path, options, sessions, disposeCounts));
        var oldStream = enhancer.CreateSession(() =>
        {
            runEntered.Set();
            releaseRun.Wait();
        });
        var oldSession = GetInferenceSession(oldStream);
        var processTask = Task.Run(() => oldStream.Process(new float[512]));

        try
        {
            Assert.True(runEntered.Wait(TimeSpan.FromSeconds(10)), "Process did not reach OrtRun.");

            notifier.Raise(CreateReloadEvent(modelPath));
            var newStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
            var newSession = GetInferenceSession(newStream);

            Assert.NotSame(oldSession, newSession);
            Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));
            var expectedOutput = newStream.Process(new float[512]);

            releaseRun.Set();
            Assert.Equal(expectedOutput, await processTask);
            Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));

            oldStream.Dispose();
            oldStream.Dispose();
            Assert.Equal(1, GetDisposeCount(disposeCounts, oldSession));

            enhancer.Dispose();
            Assert.Equal(0, GetDisposeCount(disposeCounts, newSession));
            newStream.Dispose();
            Assert.Equal(1, GetDisposeCount(disposeCounts, newSession));
        }
        finally
        {
            releaseRun.Set();
            await processTask;
            oldStream.Dispose();
            enhancer.Dispose();
        }
    }

    [Fact]
    public void GtcrnSpeechEnhancer_FailedReloadRollsBackAndDisposesLoadResources()
    {
        var modelPath = GetTestModelPath();
        var notifier = new TestModelChangeNotifier();
        var sessions = new ConcurrentQueue<InferenceSession>();
        var disposeCounts = new ConcurrentDictionary<InferenceSession, int>();
        SessionOptions? failedOptions = null;
        var loadCount = 0;
        var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance,
            (path, options) =>
            {
                if (Interlocked.Increment(ref loadCount) == 2)
                {
                    failedOptions = options;
                    throw new InvalidOperationException("Injected model-load failure.");
                }

                return CreateTrackedHolder(path, options, sessions, disposeCounts);
            });
        var oldStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        var oldSession = GetInferenceSession(oldStream);

        notifier.Raise(CreateReloadEvent(modelPath));

        Assert.True(enhancer.IsReady);
        Assert.Equal(2, loadCount);
        Assert.Single(sessions);
        Assert.NotNull(failedOptions);
        Assert.True(failedOptions.IsClosed);

        var currentStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        Assert.Same(oldSession, GetInferenceSession(currentStream));
        Assert.Equal(256, oldStream.Process(new float[512]).Length);
        Assert.Equal(256, currentStream.Process(new float[512]).Length);
        Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));

        enhancer.Dispose();
        oldStream.Dispose();
        Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));
        currentStream.Dispose();
        enhancer.Dispose();

        Assert.Equal(1, GetDisposeCount(disposeCounts, oldSession));
    }

    [Fact]
    public async Task GtcrnSpeechEnhancer_DisposeAndReloadAreLinearizableAcrossBothOutcomes()
    {
        await VerifyReloadWinsDisposeRaceAsync();
        await VerifyDisposeWinsReloadRaceAsync();
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

    private static InferenceSession GetInferenceSession(GtcrnStreamingSession stream)
    {
        var field = typeof(GtcrnStreamingSession).GetField(
            "_session",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<InferenceSession>(field?.GetValue(stream));
    }

    private static async Task VerifyReloadWinsDisposeRaceAsync()
    {
        var modelPath = GetTestModelPath();
        var notifier = new TestModelChangeNotifier();
        var sessions = new ConcurrentQueue<InferenceSession>();
        var disposeCounts = new ConcurrentDictionary<InferenceSession, int>();
        using var runEntered = new ManualResetEventSlim();
        using var releaseRun = new ManualResetEventSlim();
        using var reloadEntered = new ManualResetEventSlim();
        using var releaseReload = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        var loadCount = 0;
        var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance,
            (path, options) =>
            {
                if (Interlocked.Increment(ref loadCount) == 2)
                {
                    reloadEntered.Set();
                    releaseReload.Wait();
                }

                return CreateTrackedHolder(path, options, sessions, disposeCounts);
            });
        var oldStream = enhancer.CreateSession(() =>
        {
            runEntered.Set();
            releaseRun.Wait();
        });
        var oldSession = GetInferenceSession(oldStream);
        var processTask = Task.Run(() => oldStream.Process(new float[512]));

        try
        {
            Assert.True(runEntered.Wait(TimeSpan.FromSeconds(10)), "Process did not reach OrtRun.");
            var reloadTask = Task.Run(() => notifier.Raise(CreateReloadEvent(modelPath)));
            Assert.True(reloadEntered.Wait(TimeSpan.FromSeconds(10)), "Reload did not reach model load.");
            var disposeTask = Task.Run(() =>
            {
                disposeStarted.Set();
                enhancer.Dispose();
            });
            Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(10)), "Dispose did not start.");
            Assert.False(disposeTask.IsCompleted, "Dispose passed a reload holding the enhancer gate.");

            releaseReload.Set();
            await Task.WhenAll(reloadTask, disposeTask);

            Assert.Equal(2, sessions.Count);
            var newSession = sessions.Last();
            Assert.Equal(1, GetDisposeCount(disposeCounts, newSession));
            Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));
            Assert.Throws<InvalidOperationException>(enhancer.CreateSession);

            releaseRun.Set();
            Assert.Equal(256, (await processTask).Length);
            Assert.Equal(256, oldStream.Process(new float[256]).Length);
            oldStream.Dispose();
            oldStream.Dispose();

            Assert.Equal(1, GetDisposeCount(disposeCounts, oldSession));
            Assert.Equal(1, GetDisposeCount(disposeCounts, newSession));
        }
        finally
        {
            releaseReload.Set();
            releaseRun.Set();
            await processTask;
            oldStream.Dispose();
            enhancer.Dispose();
        }
    }

    private static async Task VerifyDisposeWinsReloadRaceAsync()
    {
        var modelPath = GetTestModelPath();
        var notifier = new TestModelChangeNotifier();
        var sessions = new ConcurrentQueue<InferenceSession>();
        var disposeCounts = new ConcurrentDictionary<InferenceSession, int>();
        var loadCount = 0;
        var enhancer = new GtcrnSpeechEnhancer(
            Options.Create(new SpeechEnhancementOptions { Enabled = true, ModelBasePath = modelPath }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<GtcrnSpeechEnhancer>.Instance,
            (path, options) =>
            {
                Interlocked.Increment(ref loadCount);
                return CreateTrackedHolder(path, options, sessions, disposeCounts);
            });
        var oldStream = Assert.IsType<GtcrnStreamingSession>(enhancer.CreateSession());
        var oldSession = GetInferenceSession(oldStream);
        var capturedReloadHandler = notifier.CaptureHandler();
        using var reloadReady = new ManualResetEventSlim();
        using var invokeReload = new ManualResetEventSlim();
        var reloadTask = Task.Run(() =>
        {
            reloadReady.Set();
            invokeReload.Wait();
            capturedReloadHandler(CreateReloadEvent(modelPath));
        });

        Assert.True(reloadReady.Wait(TimeSpan.FromSeconds(10)), "Reload callback was not captured.");
        await Task.Run(enhancer.Dispose);
        Assert.False(enhancer.IsReady);
        Assert.Equal(0, GetDisposeCount(disposeCounts, oldSession));

        invokeReload.Set();
        await reloadTask;

        Assert.Equal(1, loadCount);
        Assert.Single(sessions);
        Assert.Throws<InvalidOperationException>(enhancer.CreateSession);
        Assert.Equal(256, oldStream.Process(new float[512]).Length);
        oldStream.Dispose();
        enhancer.Dispose();

        Assert.Equal(1, GetDisposeCount(disposeCounts, oldSession));
    }

    private static InferenceSessionHolder CreateTrackedHolder(
        string modelPath,
        SessionOptions options,
        ConcurrentQueue<InferenceSession> sessions,
        ConcurrentDictionary<InferenceSession, int> disposeCounts)
    {
        var session = new InferenceSession(modelPath, options);
        sessions.Enqueue(session);
        return new InferenceSessionHolder(session, value =>
        {
            disposeCounts.AddOrUpdate(value, 1, static (_, count) => count + 1);
            value.Dispose();
        });
    }

    private static ActiveModelChangedEvent CreateReloadEvent(string modelPath) => new()
    {
        EngineType = EngineType.SpeechEnhancement,
        ModelId = "replacement",
        ModelPath = modelPath,
    };

    private static int GetDisposeCount(
        ConcurrentDictionary<InferenceSession, int> disposeCounts,
        InferenceSession session)
        => disposeCounts.GetValueOrDefault(session);

    private static string GetTestModelPath()
        => Path.Combine(FindRepoRoot(), "lucia.Tests/TestData/gtcrn-streaming-test.onnx");

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
