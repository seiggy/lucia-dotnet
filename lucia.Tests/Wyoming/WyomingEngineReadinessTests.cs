using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEngineReadinessTests
{
    [Fact]
    public void SherpaSttEngine_EmptyModelPath_DoesNotThrow_AndIsNotReady()
    {
        var exception = Record.Exception(() => new SherpaSttEngine(
            Options.Create(new SttOptions { ModelPath = string.Empty }),
            new TestModelChangeNotifier(),
            NullLogger<SherpaSttEngine>.Instance));

        Assert.Null(exception);

        using var engine = new SherpaSttEngine(
            Options.Create(new SttOptions { ModelPath = string.Empty }),
            new TestModelChangeNotifier(),
            NullLogger<SherpaSttEngine>.Instance);
        Assert.False(engine.IsReady);
    }

    [Fact]
    public void SherpaWakeWordDetector_EmptyModelPath_DoesNotThrow_AndIsNotReady()
    {
        var exception = Record.Exception(() => new SherpaWakeWordDetector(
            Options.Create(new WakeWordOptions { ModelPath = string.Empty }),
            changeNotifier: null,
            NullLogger<SherpaWakeWordDetector>.Instance));

        Assert.Null(exception);

        using var detector = new SherpaWakeWordDetector(
            Options.Create(new WakeWordOptions { ModelPath = string.Empty }),
            changeNotifier: null,
            NullLogger<SherpaWakeWordDetector>.Instance);
        Assert.False(detector.IsReady);
    }

    [Fact]
    public void SherpaDiarizationEngine_EmptyModelPath_DoesNotThrow_AndIsNotReady()
    {
        var exception = Record.Exception(() => new SherpaDiarizationEngine(
            Options.Create(new DiarizationOptions { Enabled = true, EmbeddingModelPath = string.Empty }),
            NullLogger<SherpaDiarizationEngine>.Instance));

        Assert.Null(exception);

        using var engine = new SherpaDiarizationEngine(
            Options.Create(new DiarizationOptions { Enabled = true, EmbeddingModelPath = string.Empty }),
            NullLogger<SherpaDiarizationEngine>.Instance);
        Assert.False(engine.IsReady);
    }

    private sealed class TestModelChangeNotifier : IModelChangeNotifier
    {
        public event Action<ActiveModelChangedEvent>? ActiveModelChanged;

        public void Raise(ActiveModelChangedEvent evt) => ActiveModelChanged?.Invoke(evt);
    }
}
