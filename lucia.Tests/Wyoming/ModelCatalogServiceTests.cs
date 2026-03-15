using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class ModelCatalogServiceTests
{
    private readonly ModelCatalogService _catalog = CreateCatalog();

    [Fact]
    public void GetAvailableModels_Stt_ReturnsNonEmpty()
    {
        var models = _catalog.GetAvailableModels(EngineType.Stt);

        Assert.NotEmpty(models);
    }

    [Fact]
    public void GetAvailableModels_Vad_ReturnsSileroVad()
    {
        var models = _catalog.GetAvailableModels(EngineType.Vad);

        var model = Assert.Single(models);
        Assert.Equal("silero_vad_v5", model.Id);
    }

    [Fact]
    public void GetAvailableModels_WakeWord_ReturnsExpectedModels()
    {
        var models = _catalog.GetAvailableModels(EngineType.WakeWord);

        Assert.Equal(2, models.Count);
    }

    [Fact]
    public void GetAvailableModels_SpeakerEmbedding_ReturnsExpectedModels()
    {
        var models = _catalog.GetAvailableModels(EngineType.SpeakerEmbedding);

        Assert.Equal(3, models.Count);
    }

    [Fact]
    public void GetModelById_Stt_FindsExistingModel()
    {
        var model = _catalog.GetModelById(
            EngineType.Stt,
            "sherpa-onnx-streaming-zipformer-en-2023-06-26");

        Assert.NotNull(model);
        Assert.Equal("Streaming Zipformer English", model.Name);
    }

    [Fact]
    public void GetModelById_Vad_FindsExistingModel()
    {
        var model = _catalog.GetModelById(EngineType.Vad, "silero_vad_v5");

        Assert.NotNull(model);
        Assert.Equal("Silero VAD v5", model.Name);
    }

    [Fact]
    public void GetModelById_UnknownId_ReturnsNull()
    {
        var model = _catalog.GetModelById(EngineType.Stt, "nonexistent-model-id");

        Assert.Null(model);
    }

    [Theory]
    [InlineData(EngineType.Stt)]
    [InlineData(EngineType.Vad)]
    [InlineData(EngineType.WakeWord)]
    [InlineData(EngineType.SpeakerEmbedding)]
    public void GetAvailableModels_AllEngineTypes_HaveCorrectEngineType(EngineType engineType)
    {
        var models = _catalog.GetAvailableModels(engineType);

        Assert.All(models, model => Assert.Equal(engineType, model.EngineType));
    }

    [Theory]
    [InlineData(EngineType.Stt)]
    [InlineData(EngineType.Vad)]
    [InlineData(EngineType.WakeWord)]
    [InlineData(EngineType.SpeakerEmbedding)]
    public void GetAvailableModels_AllModels_HaveDownloadUrls(EngineType engineType)
    {
        var models = _catalog.GetAvailableModels(engineType);

        Assert.All(models, model => Assert.False(
            string.IsNullOrWhiteSpace(model.DownloadUrl),
            $"Model '{model.Id}' has no DownloadUrl"));
    }

    [Fact]
    public void VadModel_IsNotArchive()
    {
        var model = _catalog.GetModelById(EngineType.Vad, "silero_vad_v5");

        Assert.NotNull(model);
        Assert.False(model.IsArchive);
    }

    [Fact]
    public void SpeakerEmbeddingModels_AreNotArchives()
    {
        var models = _catalog.GetAvailableModels(EngineType.SpeakerEmbedding);

        Assert.All(models, model => Assert.False(
            model.IsArchive,
            $"Speaker embedding model '{model.Id}' should not be an archive"));
    }

    [Fact]
    public void WakeWordModels_AreArchives()
    {
        var models = _catalog.GetAvailableModels(EngineType.WakeWord);

        Assert.All(models, model => Assert.True(
            model.IsArchive,
            $"Wake word model '{model.Id}' should be an archive"));
    }

    private static ModelCatalogService CreateCatalog() =>
        new(
            new OptionsMonitorStub<SttModelOptions>(new SttModelOptions()),
            new OptionsMonitorStub<VadOptions>(new VadOptions()),
            new OptionsMonitorStub<WakeWordOptions>(new WakeWordOptions()),
            new OptionsMonitorStub<DiarizationOptions>(new DiarizationOptions()),
            new OptionsMonitorStub<SpeechEnhancementOptions>(new SpeechEnhancementOptions()));

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
