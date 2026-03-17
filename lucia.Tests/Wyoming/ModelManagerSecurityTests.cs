using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class ModelManagerSecurityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeleteModelAsync_RejectsPathTraversalModelId()
    {
        Directory.CreateDirectory(_tempRoot);
        var parentDirectory = _tempRoot;
        var modelsDirectory = Path.Combine(_tempRoot, "models");
        Directory.CreateDirectory(modelsDirectory);

        var manager = CreateManager(modelsDirectory);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.DeleteModelAsync(".."));

        Assert.Equal("modelId", exception.ParamName);
        Assert.True(Directory.Exists(parentDirectory));
        Assert.True(Directory.Exists(modelsDirectory));
    }

    [Fact]
    public async Task SwitchActiveModelAsync_RejectsNestedPathModelId()
    {
        var modelsDirectory = Path.Combine(_tempRoot, "models");
        Directory.CreateDirectory(modelsDirectory);
        var manager = CreateManager(modelsDirectory);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.SwitchActiveModelAsync("nested/model"));

        Assert.Equal("modelId", exception.ParamName);
    }

    [Fact]
    public async Task ValidateActiveModelAsync_RejectsInvalidConfiguredModelId()
    {
        var modelsDirectory = Path.Combine(_tempRoot, "models");
        Directory.CreateDirectory(modelsDirectory);
        var manager = CreateManager(modelsDirectory, activeModel: "..");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.ValidateActiveModelAsync());

        Assert.Equal("modelId", exception.ParamName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static ModelManager CreateManager(string modelBasePath, string activeModel = "valid-model")
    {
        var options = new OptionsMonitorStub<SttModelOptions>(new SttModelOptions
        {
            ModelBasePath = modelBasePath,
            ActiveModel = activeModel,
            AllowCustomModels = true,
            AutoDownloadDefault = false,
        });

        var vadMonitor = new OptionsMonitorStub<VadOptions>(new VadOptions());
        var wakeMonitor = new OptionsMonitorStub<WakeWordOptions>(new WakeWordOptions());
        var diarizationMonitor = new OptionsMonitorStub<DiarizationOptions>(new DiarizationOptions());
        var enhancementMonitor = new OptionsMonitorStub<SpeechEnhancementOptions>(new SpeechEnhancementOptions());

        var provider = new SherpaOnnxCatalogProvider(options, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor);
        var catalog = new ModelCatalogService(
            new IModelCatalogProvider[] { provider },
            options, vadMonitor, wakeMonitor, diarizationMonitor, enhancementMonitor,
            NullLogger<ModelCatalogService>.Instance);
        var hfDownloader = new HuggingFaceModelDownloader(
            new OptionsMonitorStub<HuggingFaceOptions>(new HuggingFaceOptions()),
            NullLogger<HuggingFaceModelDownloader>.Instance);
        var downloader = new ModelDownloader(new SimpleHttpClientFactory(), hfDownloader, NullLogger<ModelDownloader>.Instance);

        var hfOptions = new OptionsMonitorStub<HuggingFaceOptions>(new HuggingFaceOptions());
        var hfClient = new HuggingFaceClient(new SimpleHttpClientFactory(), hfOptions, NullLogger<HuggingFaceClient>.Instance);

        return new ModelManager(
            options,
            vadMonitor,
            wakeMonitor,
            diarizationMonitor,
            enhancementMonitor,
            new OptionsMonitorStub<HybridSttOptions>(new HybridSttOptions()),
            catalog,
            downloader,
            hfDownloader,
            hfClient,
            NullLogger<ModelManager>.Instance);
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
