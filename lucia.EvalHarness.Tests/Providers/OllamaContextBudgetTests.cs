using System.Text.Json;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace lucia.EvalHarness.Tests.Providers;

public sealed class OllamaContextBudgetTests
{
    [Fact]
    public void MissingBudget_UsesHardcodedFallbackWithoutMutatingSharedBuiltInProfile()
    {
        var profile = ModelParameterProfile.Default;
        var resolved = new HarnessConfiguration().GetAllProfiles()["default"];

        Assert.Equal(16384, resolved.NumContext);
        Assert.Null(profile.NumContext);
    }

    [Fact]
    public void ProfileOverride_WinsOverConfiguredDefault()
    {
        var config = new HarnessConfiguration
        {
            Ollama = new() { DefaultNumContext = 32768 },
            ParameterProfiles = new() { ["test"] = new ModelParameterProfile { Name = "test", NumContext = 8192 } }
        };
        Assert.Equal(32768, config.GetAllProfiles()["default"].NumContext);
        Assert.Equal(8192, config.GetAllProfiles()["test"].NumContext);
    }

    [Fact]
    public void BlankConfiguration_UsesHardcodedFallback()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Harness:Ollama:DefaultNumContext"] = "",
            ["Harness:ParameterProfiles:default:Name"] = "default",
            ["Harness:ParameterProfiles:default:NumContext"] = ""
        }).Build();
        var config = new HarnessConfiguration();
        root.GetSection("Harness").Bind(config);

        config.Validate();
        Assert.Equal(16384, config.GetAllProfiles()["default"].NumContext);
    }

    [Fact]
    public void OllamaFactory_MissingBudgetUsesFallback()
    {
        using var client = BackendChatClientFactory.CreateChatClient(
            new InferenceBackend { Name = "Ollama", Endpoint = "http://localhost:11434" },
            "test", ModelParameterProfile.Default);
        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigurationValidation_RejectsInvalidDefaultBudget(int budget)
    {
        var config = new HarnessConfiguration { Ollama = new() { DefaultNumContext = budget } };
        Assert.Contains("DefaultNumContext", Assert.Throws<InvalidOperationException>(config.Validate).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_ReceiveConfiguredContextBudget(bool streaming)
    {
        var capture = new CapturingChatClient();
        using var client = new ParameterInjectingChatClient(capture,
            new ModelParameterProfile { Name = "test", NumContext = 16384 });

        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) { }
        }
        else
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        }

        Assert.Equal(16384, capture.CapturedOptions!.AdditionalProperties!["num_ctx"]);
    }

    [Fact]
    public async Task CallerContext_TakesPrecedence()
    {
        var capture = new CapturingChatClient();
        using var client = new ParameterInjectingChatClient(capture,
            new ModelParameterProfile { Name = "test", NumContext = 16384 });
        var options = new ChatOptions { AdditionalProperties = new() { ["num_ctx"] = 8192 } };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        Assert.Equal(8192, capture.CapturedOptions!.AdditionalProperties!["num_ctx"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerIntegerRepresentations_AreNormalizedForTheAdapter(bool jsonNumber)
    {
        var capture = new CapturingChatClient();
        using var client = new ParameterInjectingChatClient(capture,
            new ModelParameterProfile { Name = "test", NumContext = 16384 });
        object budget = jsonNumber ? JsonSerializer.SerializeToElement(8192) : 8192L;

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { AdditionalProperties = new() { ["num_ctx"] = budget } });

        Assert.Equal(8192, Assert.IsType<int>(capture.CapturedOptions!.AdditionalProperties!["num_ctx"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OllamaFactory_RejectsInvalidBudget(int? budget)
    {
        var backend = new InferenceBackend { Name = "Ollama", Endpoint = "http://localhost:11434" };

        var error = Assert.Throws<InvalidOperationException>(() => BackendChatClientFactory.CreateChatClient(
            backend, "test", new ModelParameterProfile { Name = "test", NumContext = budget }));

        Assert.Contains("NumContext", error.Message);
    }

    [Fact]
    public void OtherBackends_DoNotRequireOllamaBudget()
    {
        var backend = new InferenceBackend
        {
            Name = "compat",
            Endpoint = "http://localhost:1234",
            Type = InferenceBackendType.OpenAICompat
        };
        using var client = BackendChatClientFactory.CreateChatClient(backend, "test", ModelParameterProfile.Default);
        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData("8192")]
    public async Task InvalidCallerBudget_IsRejectedBeforeInference(object budget)
    {
        var capture = new CapturingChatClient();
        using var client = new ParameterInjectingChatClient(capture,
            new ModelParameterProfile { Name = "test", NumContext = 16384 });
        var options = new ChatOptions { AdditionalProperties = new() { ["num_ctx"] = budget } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options));
        Assert.Null(capture.CapturedOptions);
    }

    [Fact]
    public async Task ContextMustLeaveRoomBeyondOutputReserve()
    {
        var capture = new CapturingChatClient();
        using var client = new ParameterInjectingChatClient(capture,
            new ModelParameterProfile { Name = "test", NumContext = 1024, NumPredict = 512 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 1024 }));
        Assert.Null(capture.CapturedOptions);
    }

    [Fact]
    public void ConfigurationBindingAndSummary_PreserveRequestedBudget()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Harness:ParameterProfiles:precise:Name"] = "precise",
            ["Harness:ParameterProfiles:precise:NumContext"] = "16384"
        }).Build();
        var config = new HarnessConfiguration();
        root.GetSection("Harness").Bind(config);
        var profile = config.GetAllProfiles()["precise"];

        Assert.Equal(16384, profile.NumContext);
        Assert.Contains("num_ctx=16384", profile.ToSummary());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigurationValidation_RejectsNonPositiveBudget(int budget)
    {
        var config = new HarnessConfiguration
        {
            ParameterProfiles = new() { ["test"] = new ModelParameterProfile { Name = "test", NumContext = budget } }
        };

        Assert.Contains("NumContext", Assert.Throws<InvalidOperationException>(config.Validate).Message);
    }
}
