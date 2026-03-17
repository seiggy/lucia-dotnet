using lucia.Tests.TestDoubles;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
}
