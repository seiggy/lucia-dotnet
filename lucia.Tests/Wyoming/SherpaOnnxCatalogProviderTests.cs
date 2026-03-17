using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class SherpaOnnxCatalogProviderTests
{
    private readonly SherpaOnnxCatalogProvider _provider = CreateProvider();

    [Fact]
    public void Source_IsSherpaOnnx()
    {
        Assert.Equal(ModelSource.SherpaOnnx, _provider.Source);
    }

    [Fact]
    public async Task GetModelsAsync_Stt_ReturnsNonEmptyList()
    {
        var models = await _provider.GetModelsAsync(EngineType.Stt);

        Assert.NotEmpty(models);
    }

    [Fact]
    public async Task GetModelsAsync_Vad_ReturnsSingleSileroModel()
    {
        var models = await _provider.GetModelsAsync(EngineType.Vad);

        Assert.Single(models);
        Assert.Equal("silero_vad_v5", models[0].Id);
    }

    [Fact]
    public async Task GetModelsAsync_WakeWord_ReturnsTwoModels()
    {
        var models = await _provider.GetModelsAsync(EngineType.WakeWord);

        Assert.Equal(2, models.Count);
    }

    [Fact]
    public async Task GetModelsAsync_SpeakerEmbedding_ReturnsThreeModels()
    {
        var models = await _provider.GetModelsAsync(EngineType.SpeakerEmbedding);

        Assert.Equal(3, models.Count);
    }

    [Fact]
    public async Task GetModelsAsync_SpeechEnhancement_ReturnsTwoModels()
    {
        var models = await _provider.GetModelsAsync(EngineType.SpeechEnhancement);

        Assert.Equal(2, models.Count);
    }

    [Fact]
    public async Task GetModelsAsync_OfflineStt_ReturnsThreeModels()
    {
        var models = await _provider.GetModelsAsync(EngineType.OfflineStt);

        Assert.Equal(3, models.Count);
    }

    [Fact]
    public async Task GetModelByIdAsync_ExistingVadModel_ReturnsModel()
    {
        var model = await _provider.GetModelByIdAsync(EngineType.Vad, "silero_vad_v5");

        Assert.NotNull(model);
        Assert.Equal("silero_vad_v5", model.Id);
    }

    [Fact]
    public async Task GetModelByIdAsync_ExistingSttModel_ReturnsModel()
    {
        var model = await _provider.GetModelByIdAsync(
            EngineType.Stt, "sherpa-onnx-streaming-zipformer-en-2023-06-26");

        Assert.NotNull(model);
        Assert.Equal("sherpa-onnx-streaming-zipformer-en-2023-06-26", model.Id);
    }

    [Fact]
    public async Task GetModelByIdAsync_UnknownId_ReturnsNull()
    {
        var model = await _provider.GetModelByIdAsync(EngineType.Stt, "does-not-exist");

        Assert.Null(model);
    }

    [Fact]
    public async Task GetModelsAsync_AllModels_HaveDownloadUrlSet()
    {
        var engineTypes = new[]
        {
            EngineType.Stt,
            EngineType.Vad,
            EngineType.WakeWord,
            EngineType.SpeakerEmbedding,
            EngineType.SpeechEnhancement,
            EngineType.OfflineStt,
        };

        foreach (var engineType in engineTypes)
        {
            var models = await _provider.GetModelsAsync(engineType);

            foreach (var model in models)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(model.DownloadUrl),
                    $"Model '{model.Id}' ({engineType}) has no DownloadUrl");
            }
        }
    }

    [Fact]
    public async Task GetModelsAsync_UnknownEngineType_ReturnsEmptyList()
    {
        var models = await _provider.GetModelsAsync((EngineType)999);

        Assert.Empty(models);
    }

    private static SherpaOnnxCatalogProvider CreateProvider() =>
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
