using System.Text.Json;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace lucia.EvalHarness.Tests.Providers;

/// <summary>
/// Wire-level tests that run the real installed OllamaSharp <see cref="OllamaApiClient"/> adapter
/// against a fake <see cref="System.Net.Http.HttpMessageHandler"/>, asserting on the exact
/// <c>options</c> object that OllamaSharp serializes for the <c>/api/chat</c> request. This proves
/// the determinism knobs survive the real M.E.AI → OllamaSharp mapping onto the wire.
/// </summary>
public class OllamaWireParameterTests
{
    private const string ValidChatResponse =
        "{\"model\":\"test-model\",\"created_at\":\"2024-01-01T00:00:00.0000000Z\"," +
        "\"message\":{\"role\":\"assistant\",\"content\":\"hi\"}," +
        "\"done\":true,\"done_reason\":\"stop\"," +
        "\"total_duration\":1000,\"load_duration\":1000," +
        "\"prompt_eval_count\":1,\"prompt_eval_duration\":1000," +
        "\"eval_count\":1,\"eval_duration\":1000}";

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

    private static async Task<JsonElement> SendAndCaptureOptionsAsync(
        ModelParameterProfile profile,
        ChatOptions? caller = null)
    {
        var handler = new CapturingHttpMessageHandler(ValidChatResponse);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var inner = new OllamaApiClient(http, "test-model");
        var client = new ParameterInjectingChatClient(inner, profile);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], caller);

        return handler.CapturedRoot.GetProperty("options");
    }

    [Fact]
    public async Task TypedSeed_IsWiredToOllamaSeed()
    {
        var options = await SendAndCaptureOptionsAsync(Profile(seed: 42));

        Assert.Equal(42, options.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task CallerSeed_TakesPrecedenceOnWire()
    {
        var options = await SendAndCaptureOptionsAsync(Profile(seed: 42), new ChatOptions { Seed = 999 });

        Assert.Equal(999, options.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task PositiveNumPredict_IsWiredToOllamaNumPredict()
    {
        var options = await SendAndCaptureOptionsAsync(Profile(numPredict: 128));

        Assert.Equal(128, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task UnlimitedNumPredict_IsWiredAsNegativeOne()
    {
        var options = await SendAndCaptureOptionsAsync(Profile(numPredict: -1));

        Assert.Equal(-1, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task UnlimitedNumPredict_DoesNotOverrideCallerTypedLimitOnWire()
    {
        var options = await SendAndCaptureOptionsAsync(
            Profile(numPredict: -1),
            new ChatOptions { MaxOutputTokens = 200 });

        // Caller's typed cap must survive; the -1 sentinel must not clobber it.
        Assert.Equal(200, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task RepeatPenalty_IsWiredToOllamaRepeatPenalty()
    {
        var options = await SendAndCaptureOptionsAsync(Profile(repeatPenalty: 1.3));

        Assert.Equal(1.3f, options.GetProperty("repeat_penalty").GetSingle(), 3);
    }

    [Fact]
    public async Task NoFrequencyPenalty_IsEmittedOnWire()
    {
        var options = await SendAndCaptureOptionsAsync(Profile());

        Assert.False(options.TryGetProperty("frequency_penalty", out _));
    }
}
