using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using OpenAI;

namespace lucia.EvalHarness.Tests.Providers;

/// <summary>
/// Wire-level tests that run the real installed OpenAI SDK <see cref="ChatClient"/> adapter against
/// a fake transport, asserting on the exact JSON body of the <c>/chat/completions</c> request. This
/// proves determinism knobs are wired via typed fields for OpenAI-compatible backends and that the
/// Ollama-specific additional properties never leak onto an OpenAI request.
/// </summary>
public class OpenAIWireParameterTests
{
    private const string ValidChatResponse =
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1700000000," +
        "\"model\":\"test-model\",\"choices\":[{\"index\":0," +
        "\"message\":{\"role\":\"assistant\",\"content\":\"hi\"},\"finish_reason\":\"stop\"}]," +
        "\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":1}}";

    private static ModelParameterProfile Profile(
        int numPredict = -1,
        double repeatPenalty = 1.1,
        int? seed = 42) => new()
    {
        Name = "test",
        NumPredict = numPredict,
        RepeatPenalty = repeatPenalty,
        Seed = seed
    };

    private static async Task<JsonElement> SendAndCaptureBodyAsync(
        ModelParameterProfile profile,
        ChatOptions? caller = null)
    {
        var handler = new CapturingHttpMessageHandler(ValidChatResponse);
        using var http = new HttpClient(handler);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://localhost:1234/v1"),
            Transport = new HttpClientPipelineTransport(http)
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential("not-needed"), options);
        var inner = openAiClient.GetChatClient("test-model").AsIChatClient();
        var client = new ParameterInjectingChatClient(inner, profile);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], caller);

        return handler.CapturedRoot;
    }

    [Fact]
    public async Task TypedSeed_IsWiredToOpenAISeed()
    {
        var body = await SendAndCaptureBodyAsync(Profile(seed: 42));

        Assert.Equal(42, body.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task CallerSeed_TakesPrecedenceOnWire()
    {
        var body = await SendAndCaptureBodyAsync(Profile(seed: 42), new ChatOptions { Seed = 999 });

        Assert.Equal(999, body.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task PositiveNumPredict_IsWiredToMaxCompletionTokens()
    {
        var body = await SendAndCaptureBodyAsync(Profile(numPredict: 128));

        Assert.Equal(128, body.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task UnlimitedNumPredict_DoesNotEmitAnyTokenLimit()
    {
        var body = await SendAndCaptureBodyAsync(Profile(numPredict: -1));

        // -1 is Ollama's unlimited sentinel; OpenAI must never receive a token-limit field for it.
        Assert.False(body.TryGetProperty("max_completion_tokens", out _));
        Assert.False(body.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task ZeroNumPredict_DoesNotEmitAnyTokenLimit()
    {
        var body = await SendAndCaptureBodyAsync(Profile(numPredict: 0));

        Assert.False(body.TryGetProperty("max_completion_tokens", out _));
        Assert.False(body.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task OllamaSpecificKeys_NeverLeakOntoOpenAIRequest()
    {
        var body = await SendAndCaptureBodyAsync(Profile(numPredict: -1, repeatPenalty: 1.3));

        Assert.False(body.TryGetProperty("num_predict", out _));
        Assert.False(body.TryGetProperty("repeat_penalty", out _));
        // RepeatPenalty must not be smuggled through the typed FrequencyPenalty either.
        Assert.False(body.TryGetProperty("frequency_penalty", out _));
    }
}
