using Azure.Core;
using FakeItEasy;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.EvalHarness.Tui;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.Providers;

public sealed class BackendModelDiscoveryTests
{
    [Fact]
    public async Task OpenRouter_ConvertsTokenPricesAndContextThresholdsWithoutListingImageOnlyModels()
    {
        using var handler = new CapturingHttpMessageHandler("""
            {"data":[
              {"id":"vendor/chat","architecture":{"output_modalities":["text"]},
               "pricing":{"prompt":"0.0000025","completion":"0.000015","input_cache_read":"0.00000025",
                          "request":"0.001","overrides":[{"min_prompt_tokens":272000,"prompt":"0.000005","completion":"0.0000225"}]}},
              {"id":"vendor/image","architecture":{"output_modalities":["image"]},"pricing":{}}
            ]}
            """);
        using var http = new HttpClient(handler);

        var models = await new BackendModelDiscovery(http).ListModelsAsync(new InferenceBackend
        {
            Name = "OpenRouter", Endpoint = "https://openrouter.ai/api/v1", Type = InferenceBackendType.OpenRouter
        });

        var model = Assert.Single(models);
        Assert.Equal("vendor/chat", model.Name);
        Assert.Equal(2.5m, model.Pricing?.InputUsdPerMillionTokens);
        Assert.Equal(15m, model.Pricing?.OutputUsdPerMillionTokens);
        Assert.Equal(0.25m, model.Pricing?.CachedInputUsdPerMillionTokens);
        Assert.Equal(0.001m, model.Pricing?.RequestUsd);
        var tier = Assert.Single(model.Pricing!.ContextTiers);
        Assert.Equal(272001, tier.MinimumInputTokens);
        Assert.Equal(5m, tier.InputUsdPerMillionTokens);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/models"), handler.CapturedUri);
    }

    [Fact]
    public void BackendSelections_DoNotSendModelsToOtherProviders()
    {
        IReadOnlyList<string> all = ["local-model", "cloud-deployment"];
        var selection = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Remote"] = ["local-model"],
            ["Foundry"] = ["cloud-deployment"]
        };

        Assert.Equal(["local-model"], EvalProgressDisplay.GetModelsForBackend("Remote", all, selection));
        Assert.Equal(["cloud-deployment"], EvalProgressDisplay.GetModelsForBackend("Foundry", all, selection));
        Assert.Throws<InvalidOperationException>(() =>
            EvalProgressDisplay.GetModelsForBackend("Missing", all, selection));
    }

    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat, "http://remote:8000/v1", "http://remote:8000/v1/chat/completions")]
    [InlineData(InferenceBackendType.OpenRouter, "https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1/chat/completions")]
    public async Task ChatClient_UsesBackendEndpointModelAndKey(
        InferenceBackendType type, string endpoint, string expectedUrl)
    {
        using var handler = new CapturingHttpMessageHandler("""
            {"id":"chatcmpl-1","object":"chat.completion","created":1700000000,"model":"remote-model",
             "choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":10,"completion_tokens":2,"total_tokens":12}}
            """);
        using var http = new HttpClient(handler);
        using var client = BackendChatClientFactory.CreateRawClient(new InferenceBackend
        {
            Name = "Remote", Type = type, Endpoint = endpoint, ApiKey = "test-key"
        }, "remote-model", http);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        Assert.NotNull(client.GetService<InferenceCostChatClient>());
        Assert.Equal(new Uri(expectedUrl), handler.CapturedUri);
        Assert.Equal("Bearer test-key", handler.CapturedAuthorization);
        Assert.Equal("remote-model", handler.CapturedRoot.GetProperty("model").GetString());
        Assert.Equal("hello", response.Text);
    }

    [Theory]
    [InlineData("http://remote:8000")]
    [InlineData("http://remote:8000/")]
    [InlineData("http://remote:8000/v1")]
    [InlineData("http://remote:8000/v1/")]
    public async Task RemoteRouter_NormalizesEndpointAndExcludesEmbeddingModels(string endpoint)
    {
        using var handler = new CapturingHttpMessageHandler("""
            {"data":[
              {"id":"chat-loaded","status":{"value":"loaded","args":[]}},
              {"id":"chat-unloaded","status":{"value":"unloaded","args":["--model","chat.gguf"]}},
              {"id":"embed","status":{"args":["--embeddings"]}}
            ]}
            """);
        using var http = new HttpClient(handler);
        var discovery = new BackendModelDiscovery(http);

        var models = await discovery.ListModelsAsync(new InferenceBackend
        {
            Name = "Remote",
            Endpoint = endpoint,
            Type = InferenceBackendType.OpenAICompat,
            ApiKey = "test-key"
        });

        Assert.Equal(["chat-loaded", "chat-unloaded"], models.Select(model => model.Name));
        Assert.Equal(new Uri("http://remote:8000/v1/models"), handler.CapturedUri);
        Assert.Equal("Bearer test-key", handler.CapturedAuthorization);
    }

    [Fact]
    public async Task Foundry_ListsDeploymentNamesRatherThanCatalogModelIds()
    {
        using var handler = new CapturingHttpMessageHandler("""
            {"value":[
              {"name":"my-deployment","properties":{"provisioningState":"Succeeded","model":{"name":"gpt-model"},"capabilities":{"responses":"true"}}},
              {"name":"embedding","properties":{"provisioningState":"Succeeded","capabilities":{"embeddings":"true"}}},
              {"name":"pending","properties":{"provisioningState":"Creating","capabilities":{"responses":"true"}}}
            ]}
            """);
        using var http = new HttpClient(handler);
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .Returns(new AccessToken("management-token", DateTimeOffset.UtcNow.AddHours(1)));
        var discovery = new BackendModelDiscovery(http, credential);

        var models = await discovery.ListModelsAsync(new InferenceBackend
        {
            Name = "Foundry",
            Endpoint = "https://test.openai.azure.com/",
            Type = InferenceBackendType.AzureFoundry,
            AzureResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/test"
        });

        Assert.Equal("my-deployment", Assert.Single(models).Name);
        Assert.Equal("management.azure.com", handler.CapturedUri?.Host);
        Assert.EndsWith("/accounts/test/deployments", handler.CapturedUri?.AbsolutePath);
        Assert.Equal("Bearer management-token", handler.CapturedAuthorization);
    }

    [Fact]
    public async Task Foundry_DoesNotForwardCredentialsToAnExternalContinuation()
    {
        using var handler = new CapturingHttpMessageHandler("""
            {"value":[],"nextLink":"https://untrusted.example/deployments"}
            """);
        using var http = new HttpClient(handler);
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .Returns(new AccessToken("management-token", DateTimeOffset.UtcNow.AddHours(1)));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new BackendModelDiscovery(http, credential).ListModelsAsync(new InferenceBackend
            {
                Name = "Foundry",
                Endpoint = "https://test.openai.azure.com/",
                Type = InferenceBackendType.AzureFoundry,
                AzureResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/test"
            }));

        Assert.Equal("management.azure.com", handler.CapturedUri?.Host);
    }

    [Fact]
    public async Task Availability_PropagatesCallerCancellation()
    {
        using var handler = new CapturingHttpMessageHandler("""{"data":[]}""");
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BackendModelDiscovery(http).IsAvailableAsync(new InferenceBackend
            {
                Name = "Remote", Endpoint = "http://remote:8000", Type = InferenceBackendType.OpenAICompat
            }, cancellation.Token));
    }

    [Fact]
    public async Task MalformedModelList_FailsInsteadOfPretendingNoModelsExist()
    {
        using var handler = new CapturingHttpMessageHandler("""{"unexpected":[]}""");
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new BackendModelDiscovery(http).ListModelsAsync(new InferenceBackend
            {
                Name = "Remote", Endpoint = "http://remote:8000", Type = InferenceBackendType.OpenAICompat
            }));
    }
}
