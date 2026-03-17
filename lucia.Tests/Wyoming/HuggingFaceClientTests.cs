using System.Net;

using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class HuggingFaceClientTests
{
    private const string SearchResponseJson = """
        [
          {
            "_id": "abc",
            "id": "onnx-community/whisper-tiny",
            "modelId": "onnx-community/whisper-tiny",
            "pipeline_tag": "automatic-speech-recognition",
            "tags": ["onnx", "en"],
            "downloads": 1000,
            "likes": 5,
            "private": false
          }
        ]
        """;

    private const string ModelInfoJson = """
        {
          "_id": "abc",
          "id": "onnx-community/whisper-tiny",
          "modelId": "onnx-community/whisper-tiny",
          "pipeline_tag": "automatic-speech-recognition",
          "tags": ["onnx", "en"],
          "downloads": 1000,
          "likes": 5,
          "private": false
        }
        """;

    [Fact]
    public async Task SearchModelsAsync_Stt_ReturnsDeserializedModels()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SearchResponseJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await client.SearchModelsAsync(EngineType.Stt, CancellationToken.None);

        Assert.Single(models);
        Assert.Equal("onnx-community/whisper-tiny", models[0].Id);
        Assert.Equal(1000, models[0].Downloads);
    }

    [Fact]
    public async Task SearchModelsAsync_Vad_ReturnsEmptyForUnsupportedPipeline()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
        });

        var models = await client.SearchModelsAsync(EngineType.Vad, CancellationToken.None);

        Assert.Empty(models);
    }

    [Fact]
    public async Task SearchModelsAsync_HttpError_ReturnsEmptyList()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var models = await client.SearchModelsAsync(EngineType.Stt, CancellationToken.None);

        Assert.Empty(models);
    }

    [Fact]
    public async Task GetModelInfoAsync_Success_ReturnsDeserializedInfo()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ModelInfoJson, System.Text.Encoding.UTF8, "application/json"),
        });

        var info = await client.GetModelInfoAsync("onnx-community/whisper-tiny", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("onnx-community/whisper-tiny", info.Id);
        Assert.Equal("automatic-speech-recognition", info.PipelineTag);
    }

    [Fact]
    public async Task GetModelInfoAsync_HttpError_ReturnsNull()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var info = await client.GetModelInfoAsync("nonexistent/model", CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task IsAuthenticatedAsync_Returns_True_On200()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await client.IsAuthenticatedAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAuthenticatedAsync_Returns_False_On401()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await client.IsAuthenticatedAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizationHeader_SetWhenApiTokenConfigured()
    {
        HttpRequestMessage? capturedRequest = null;
        var hfOptions = new HuggingFaceOptions { ApiToken = "hf_test_token_123" };

        var client = CreateClient(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            };
        }, hfOptions);

        await client.SearchModelsAsync(EngineType.Stt, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("hf_test_token_123", capturedRequest.Headers.Authorization?.Parameter);
    }

    private static HuggingFaceClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        HuggingFaceOptions? hfOptions = null)
    {
        var fakeHandler = new FakeHttpMessageHandler(handler);
        var factory = new StubHttpClientFactory(fakeHandler);
        var options = new OptionsMonitorStub<HuggingFaceOptions>(hfOptions ?? new HuggingFaceOptions());

        return new HuggingFaceClient(factory, options, NullLogger<HuggingFaceClient>.Instance);
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
