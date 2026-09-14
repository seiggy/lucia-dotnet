using AgentEval.Core;
using AgentEval.Metrics.Agentic;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class JudgeClientFactoryTests
{
    private const string ValidResponse = """
        {
          "id": "resp-1", "object": "response", "created_at": 1700000000,
          "status": "completed", "model": "judge-model", "store": false,
          "output": [{
            "type": "message", "id": "msg-1", "role": "assistant", "status": "completed",
            "content": [{"type": "output_text", "text": "{\"score\":100,\"reasoning\":\"complete\"}", "annotations": []}]
          }],
          "usage": {"input_tokens": 10, "output_tokens": 20, "total_tokens": 30}
        }
        """;

    [Fact]
    public void Create_WhollyAbsentConfiguration_ReturnsNull()
    {
        Assert.Null(JudgeClientFactory.Create(new AzureOpenAIJudgeSettings()));
    }

    [Fact]
    public void Create_ApiKeyWithoutEndpoint_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings { ApiKey = "configured" };

        var exception = Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));

        Assert.Contains("Endpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_DeploymentWithoutEndpoint_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings { JudgeDeployment = "judge" };

        var exception = Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));

        Assert.Contains("Endpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_EndpointWithoutDeployment_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings
        {
            Endpoint = "https://example.openai.azure.com/"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));

        Assert.Contains("JudgeDeployment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_MalformedEndpoint_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings { Endpoint = "not a URI" };

        Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));
    }

    [Theory]
    [InlineData("https://example.openai.azure.com", "configured")]
    [InlineData("https://example.services.ai.azure.com/", null)]
    [InlineData("https://example.openai.azure.com/openai/v1", "configured")]
    [InlineData("https://example.openai.azure.com/openai/v1/", null)]
    public void Create_DefaultJudge_UsesResponsesEndpoint(string endpoint, string? apiKey)
    {
        using var client = JudgeClientFactory.Create(new AzureOpenAIJudgeSettings
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            JudgeDeployment = "judge-deployment"
        });

        Assert.NotNull(client);
        var metadata = client.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal(new Uri(new Uri(endpoint).GetLeftPart(UriPartial.Authority) + "/openai/v1/"),
            metadata.ProviderUri);
        Assert.Equal("judge-deployment", metadata.DefaultModelId);
        Assert.Null(client.GetService<OpenAI.Chat.ChatClient>());
    }

    [Theory]
    [InlineData("file:///tmp/endpoint")]
    [InlineData("https://example.services.ai.azure.com/api/projects/project")]
    [InlineData("https://example.openai.azure.com/openai/v1/responses")]
    [InlineData("https://example.openai.azure.com/?api-version=preview")]
    public void Create_UnsupportedEndpoint_FailsClearly(string endpoint)
    {
        var settings = new AzureOpenAIJudgeSettings
        {
            Endpoint = endpoint,
            ApiKey = "configured",
            JudgeDeployment = "judge"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));

        Assert.Contains("Endpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Responses_SendsModernRequestWithoutMutatingCallerOptions()
    {
        using var handler = new CapturingHttpMessageHandler(ValidResponse);
        using var httpClient = new HttpClient(handler);
        using var client = JudgeClientFactory.Create(new AzureOpenAIJudgeSettings
        {
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "test-key",
            JudgeDeployment = "custom-deployment"
        }, httpClient);
        Assert.NotNull(client);
        var options = new ChatOptions
        {
            Temperature = 0.3f,
            TopP = 0.9f,
            MaxOutputTokens = 4096,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Return a JSON score.")], options);

        Assert.Equal(new Uri("https://example.openai.azure.com/openai/v1/responses"), handler.CapturedUri);
        Assert.Equal("Bearer test-key", handler.CapturedAuthorization);
        var body = handler.CapturedRoot;
        Assert.Equal("custom-deployment", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("store").GetBoolean());
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.False(body.TryGetProperty("top_p", out _));
        Assert.False(body.TryGetProperty("max_tokens", out _));
        Assert.Equal(4096, body.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("json_object", body.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.Contains("JSON score", body.GetProperty("input").ToString(), StringComparison.Ordinal);
        Assert.Equal(0.3f, options.Temperature);
        Assert.Equal(0.9f, options.TopP);
        Assert.Null(options.RawRepresentationFactory);
        Assert.Equal("""{"score":100,"reasoning":"complete"}""", response.Text);
        Assert.Equal(30, response.Usage?.TotalTokenCount);
        Assert.Null(response.ConversationId);
    }

    [Fact]
    public async Task Create_Responses_WorksWithInstalledTaskCompletionMetric()
    {
        using var handler = new CapturingHttpMessageHandler(ValidResponse);
        using var httpClient = new HttpClient(handler);
        using var client = JudgeClientFactory.Create(new AzureOpenAIJudgeSettings
        {
            Endpoint = "https://example.services.ai.azure.com/openai/v1/",
            ApiKey = "test-key",
            JudgeDeployment = "judge"
        }, httpClient);
        Assert.NotNull(client);
        var metric = new TaskCompletionMetric(client);

        var result = await metric.EvaluateAsync(new EvaluationContext
        {
            Input = "Say hello.",
            Output = "Hello!"
        });

        Assert.Equal(100, result.Score);
        Assert.Equal("/openai/v1/responses", handler.CapturedUri?.AbsolutePath);
        Assert.False(handler.CapturedRoot.TryGetProperty("temperature", out _));
    }

    [Fact]
    public void Create_LegacyOptOut_PreservesChatCompletions()
    {
        using var client = JudgeClientFactory.Create(new AzureOpenAIJudgeSettings
        {
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "configured",
            JudgeDeployment = "legacy-judge",
            UseResponsesApi = false
        });

        Assert.NotNull(client);
        Assert.NotNull(client.GetService<OpenAI.Chat.ChatClient>());
        Assert.Equal("legacy-judge", client.GetService<ChatClientMetadata>()?.DefaultModelId);
    }
}
