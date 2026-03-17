using System.Net;

using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class HuggingFaceCatalogProviderTests
{
    private const string SearchResponseJson = """
        [
          {
            "_id": "abc",
            "id": "onnx-community/whisper-tiny",
            "modelId": "onnx-community/whisper-tiny",
            "pipeline_tag": "automatic-speech-recognition",
            "tags": ["onnx", "en", "fr"],
            "downloads": 5000,
            "likes": 10,
            "private": false
          },
          {
            "_id": "def",
            "id": "onnx-community/whisper-small",
            "modelId": "onnx-community/whisper-small",
            "pipeline_tag": "automatic-speech-recognition",
            "tags": ["onnx", "de"],
            "downloads": 3000,
            "likes": 8,
            "private": false
          }
        ]
        """;

    private const string SingleModelJson = """
        {
          "_id": "abc",
          "id": "onnx-community/whisper-tiny",
          "modelId": "onnx-community/whisper-tiny",
          "pipeline_tag": "automatic-speech-recognition",
          "tags": ["onnx", "en"],
          "downloads": 5000,
          "likes": 10,
          "private": false
        }
        """;

    [Fact]
    public void Source_IsHuggingFace()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
        });

        Assert.Equal(ModelSource.HuggingFace, provider.Source);
    }

    [Fact]
    public async Task GetModelsAsync_MapsHuggingFaceModelsToDefinitions()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.Equal(2, models.Count);
        Assert.Equal("onnx-community/whisper-tiny", models[0].Id);
        Assert.Equal("onnx-community/whisper-small", models[1].Id);
    }

    [Fact]
    public async Task GetModelsAsync_SetsRepoIdOnReturnedModels()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.All(models, m => Assert.Equal(m.Id, m.RepoId));
    }

    [Fact]
    public async Task GetModelsAsync_ExtractsLanguagesFromTags()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.Contains("en", models[0].Languages);
        Assert.Contains("fr", models[0].Languages);
        Assert.Contains("de", models[1].Languages);
    }

    [Fact]
    public async Task GetModelsAsync_EmptyClientResponse_ReturnsEmptyList()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.Empty(models);
    }

    [Fact]
    public async Task GetModelsAsync_AllModels_HaveHuggingFaceSource()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.All(models, m => Assert.Equal(ModelSource.HuggingFace, m.Source));
    }

    [Fact]
    public async Task GetModelByIdAsync_ReturnsMappedModel()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleModelJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var model = await provider.GetModelByIdAsync(EngineType.Stt, "onnx-community/whisper-tiny");

        Assert.NotNull(model);
        Assert.Equal("onnx-community/whisper-tiny", model.Id);
        Assert.Equal("onnx-community/whisper-tiny", model.RepoId);
        Assert.Equal(ModelSource.HuggingFace, model.Source);
    }

    [Fact]
    public async Task GetModelByIdAsync_HttpError_ReturnsNull()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var model = await provider.GetModelByIdAsync(EngineType.Stt, "nonexistent/model");

        Assert.Null(model);
    }

    [Fact]
    public async Task GetModelsAsync_SetsDownloadUrlFromRepoId()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await provider.GetModelsAsync(EngineType.Stt);

        Assert.Equal("https://huggingface.co/onnx-community/whisper-tiny", models[0].DownloadUrl);
        Assert.Equal("https://huggingface.co/onnx-community/whisper-small", models[1].DownloadUrl);
    }

    private static HuggingFaceCatalogProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var fakeHandler = new FakeHttpMessageHandler(handler);
        var factory = new StubHttpClientFactory(fakeHandler);
        var hfOptions = new OptionsMonitorStub<HuggingFaceOptions>(new HuggingFaceOptions());
        var hfClient = new HuggingFaceClient(factory, hfOptions, NullLogger<HuggingFaceClient>.Instance);

        return new HuggingFaceCatalogProvider(hfClient, NullLogger<HuggingFaceCatalogProvider>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
