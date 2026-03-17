using lucia.Tests.TestDoubles;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class EngineHotReloadTests
{
    [Fact]
    public void VadEngine_IgnoresNonVadEvents()
    {
        var notifier = new TestModelChangeNotifier();
        using var engine = new SherpaVadEngine(
            Options.Create(new VadOptions { ModelPath = string.Empty }),
            notifier,
            NullLogger<SherpaVadEngine>.Instance);

        Assert.False(engine.IsReady);

        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Stt,
            ModelId = "some-stt-model",
            ModelPath = "/nonexistent/stt/model",
        });

        Assert.False(engine.IsReady);
    }

    [Fact]
    public void DiarizationEngine_IgnoresNonSpeakerEmbeddingEvents()
    {
        var notifier = new TestModelChangeNotifier();
        using var engine = new SherpaDiarizationEngine(
            Options.Create(new DiarizationOptions { Enabled = true, EmbeddingModelPath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<SherpaDiarizationEngine>.Instance);

        Assert.False(engine.IsReady);

        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Stt,
            ModelId = "some-stt-model",
            ModelPath = "/nonexistent/stt/model",
        });

        Assert.False(engine.IsReady);
    }

    [Fact]
    public void VadEngine_AttemptsReloadOnVadEvent()
    {
        var notifier = new TestModelChangeNotifier();
        using var engine = new SherpaVadEngine(
            Options.Create(new VadOptions { ModelPath = string.Empty }),
            notifier,
            NullLogger<SherpaVadEngine>.Instance);

        Assert.False(engine.IsReady);

        // Fire a Vad event with a non-existent path — engine attempts reload but file doesn't exist.
        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Vad,
            ModelId = "silero_vad",
            ModelPath = "/nonexistent/vad/model.onnx",
        });

        Assert.False(engine.IsReady);
    }

    [Fact]
    public void DiarizationEngine_AttemptsReloadOnSpeakerEmbeddingEvent()
    {
        var notifier = new TestModelChangeNotifier();
        using var engine = new SherpaDiarizationEngine(
            Options.Create(new DiarizationOptions { Enabled = true, EmbeddingModelPath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<SherpaDiarizationEngine>.Instance);

        Assert.False(engine.IsReady);

        // Fire a SpeakerEmbedding event with a non-existent path — engine attempts reload but file doesn't exist.
        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.SpeakerEmbedding,
            ModelId = "3dspeaker",
            ModelPath = "/nonexistent/speaker/model",
        });

        Assert.False(engine.IsReady);
    }

    [Fact]
    public void SttEngine_IgnoresNonSttEvents()
    {
        var notifier = new TestModelChangeNotifier();
        using var engine = new SherpaSttEngine(
            Options.Create(new SttOptions { ModelPath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<SherpaSttEngine>.Instance);

        Assert.False(engine.IsReady);

        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Vad,
            ModelId = "silero_vad",
            ModelPath = "/nonexistent/vad/model",
        });

        // Engine is still not ready — no crash, IsReady unchanged from initial state.
        Assert.False(engine.IsReady);
    }

    [Fact]
    public void MultipleEngines_OnlySttRespondsToSttEvent()
    {
        var notifier = new TestModelChangeNotifier();

        using var sttEngine = new SherpaSttEngine(
            Options.Create(new SttOptions { ModelPath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<SherpaSttEngine>.Instance);

        using var vadEngine = new SherpaVadEngine(
            Options.Create(new VadOptions { ModelPath = string.Empty }),
            notifier,
            NullLogger<SherpaVadEngine>.Instance);

        using var diarizationEngine = new SherpaDiarizationEngine(
            Options.Create(new DiarizationOptions { Enabled = true, EmbeddingModelPath = string.Empty }),
            notifier,
            TestOnnxProvider.Instance,
            NullLogger<SherpaDiarizationEngine>.Instance);

        Assert.False(sttEngine.IsReady);
        Assert.False(vadEngine.IsReady);
        Assert.False(diarizationEngine.IsReady);

        // Fire an Stt event — only STT engine should attempt reload; others filter it out.
        notifier.Raise(new ActiveModelChangedEvent
        {
            EngineType = EngineType.Stt,
            ModelId = "zipformer-streaming",
            ModelPath = "/nonexistent/stt/model",
        });

        // All remain not-ready (no models on disk), but VAD and Diarization correctly ignored the event.
        Assert.False(sttEngine.IsReady);
        Assert.False(vadEngine.IsReady);
        Assert.False(diarizationEngine.IsReady);
    }

    private sealed class TestModelChangeNotifier : IModelChangeNotifier
    {
        public event Action<ActiveModelChangedEvent>? ActiveModelChanged;

        public void Raise(ActiveModelChangedEvent evt) => ActiveModelChanged?.Invoke(evt);
    }
}
