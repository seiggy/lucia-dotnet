using FakeItEasy;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class ModelManagerMultiEngineTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetActiveModelId_Stt_ReturnsConfiguredDefault()
    {
        var manager = CreateManager(sttActiveModel: "my-stt-model");

        Assert.Equal("my-stt-model", manager.GetActiveModelId(EngineType.Stt));
    }

    [Fact]
    public void GetActiveModelId_Vad_ReturnsConfiguredDefault()
    {
        var manager = CreateManager(vadActiveModel: "my-vad-model");

        Assert.Equal("my-vad-model", manager.GetActiveModelId(EngineType.Vad));
    }

    [Fact]
    public void GetActiveModelId_WakeWord_ReturnsConfiguredDefault()
    {
        var manager = CreateManager(wakeWordActiveModel: "my-wake-model");

        Assert.Equal("my-wake-model", manager.GetActiveModelId(EngineType.WakeWord));
    }

    [Fact]
    public void GetActiveModelId_SpeakerEmbedding_ReturnsConfiguredDefault()
    {
        var manager = CreateManager(diarizationActiveModel: "my-diarization-model");

        Assert.Equal("my-diarization-model", manager.GetActiveModelId(EngineType.SpeakerEmbedding));
    }

    [Fact]
    public async Task SwitchActiveModel_EngineType_FiresEventWithCorrectEngineType()
    {
        var vadBasePath = Path.Combine(_tempRoot, "vad");
        const string modelId = "test-vad-model";
        CreateInstalledModel(vadBasePath, modelId);

        var manager = CreateManager(vadBasePath: vadBasePath);

        ActiveModelChangedEvent? receivedEvent = null;
        manager.ActiveModelChanged += e => receivedEvent = e;

        await manager.SwitchActiveModelAsync(EngineType.Vad, modelId);

        Assert.NotNull(receivedEvent);
        Assert.Equal(EngineType.Vad, receivedEvent.EngineType);
        Assert.Equal(modelId, receivedEvent.ModelId);
    }

    [Fact]
    public async Task SwitchActiveModel_DoesNotAffectOtherEngines()
    {
        var sttBasePath = Path.Combine(_tempRoot, "stt");
        const string switchedModelId = "switched-stt-model";
        CreateInstalledModel(sttBasePath, switchedModelId);

        var manager = CreateManager(
            sttBasePath: sttBasePath,
            sttActiveModel: "original-stt-model",
            vadActiveModel: "original-vad-model");

        await manager.SwitchActiveModelAsync(EngineType.Stt, switchedModelId);

        Assert.Equal(switchedModelId, manager.GetActiveModelId(EngineType.Stt));
        Assert.Equal("original-vad-model", manager.GetActiveModelId(EngineType.Vad));
    }

    [Fact]
    public async Task DeleteModel_EngineType_ClearsOverride()
    {
        var sttBasePath = Path.Combine(_tempRoot, "stt");
        const string modelId = "deletable-model";
        CreateInstalledModel(sttBasePath, modelId);

        var manager = CreateManager(sttBasePath: sttBasePath, sttActiveModel: "config-default-model");

        await manager.SwitchActiveModelAsync(EngineType.Stt, modelId);
        Assert.Equal(modelId, manager.GetActiveModelId(EngineType.Stt));

        await manager.DeleteModelAsync(EngineType.Stt, modelId);

        Assert.Equal("config-default-model", manager.GetActiveModelId(EngineType.Stt));
    }

    [Fact]
    public async Task SwitchActiveModel_NonExistentModel_ThrowsDirectoryNotFound()
    {
        var sttBasePath = Path.Combine(_tempRoot, "stt");
        Directory.CreateDirectory(sttBasePath);

        var manager = CreateManager(sttBasePath: sttBasePath);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => manager.SwitchActiveModelAsync(EngineType.Stt, "non-existent-model"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void CreateInstalledModel(string basePath, string modelId)
    {
        var modelDir = Path.Combine(basePath, modelId);
        Directory.CreateDirectory(modelDir);
        File.WriteAllBytes(Path.Combine(modelDir, "model.onnx"), [0x00]);
    }

    private ModelManager CreateManager(
        string? sttBasePath = null,
        string? vadBasePath = null,
        string? wakeBasePath = null,
        string? diarizationBasePath = null,
        string sttActiveModel = "default-stt-model",
        string vadActiveModel = "default-vad-model",
        string wakeWordActiveModel = "default-wake-model",
        string diarizationActiveModel = "default-diarization-model")
    {
        var sttOptions = new SttModelOptions
        {
            ActiveModel = sttActiveModel,
            ModelBasePath = sttBasePath ?? Path.Combine(_tempRoot, "stt"),
            AllowCustomModels = true,
            AutoDownloadDefault = false,
        };
        var vadOptions = new VadOptions
        {
            ActiveModel = vadActiveModel,
            ModelBasePath = vadBasePath ?? Path.Combine(_tempRoot, "vad"),
            AutoDownloadDefault = false,
        };
        var wakeOptions = new WakeWordOptions
        {
            ActiveModel = wakeWordActiveModel,
            ModelBasePath = wakeBasePath ?? Path.Combine(_tempRoot, "wake"),
            AutoDownloadDefault = false,
        };
        var diarizationOpts = new DiarizationOptions
        {
            ActiveModel = diarizationActiveModel,
            ModelBasePath = diarizationBasePath ?? Path.Combine(_tempRoot, "speaker"),
            AutoDownloadDefault = false,
        };

        var sttMonitor = new OptionsMonitorStub<SttModelOptions>(sttOptions);
        var vadMonitor = new OptionsMonitorStub<VadOptions>(vadOptions);
        var wakeMonitor = new OptionsMonitorStub<WakeWordOptions>(wakeOptions);
        var diarizationMonitor = new OptionsMonitorStub<DiarizationOptions>(diarizationOpts);
        var enhancementMonitor = new OptionsMonitorStub<SpeechEnhancementOptions>(new SpeechEnhancementOptions());

        var catalog = new ModelCatalogService(sttMonitor, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor);
        var downloader = new ModelDownloader(A.Fake<IHttpClientFactory>(), NullLogger<ModelDownloader>.Instance);

        return new ModelManager(
            sttMonitor,
            vadMonitor,
            wakeMonitor,
            diarizationMonitor,
            enhancementMonitor,
            catalog,
            downloader,
            NullLogger<ModelManager>.Instance);
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
