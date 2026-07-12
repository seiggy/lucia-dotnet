using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.Providers;

/// <summary>
/// Provider-free tests that assert how <see cref="ParameterInjectingChatClient"/> maps a
/// <see cref="ModelParameterProfile"/> onto <see cref="ChatOptions"/>, captured verbatim by a
/// generic <see cref="CapturingChatClient"/> double.
/// </summary>
public class ParameterInjectingChatClientTests
{
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

    private static async Task<ChatOptions> CaptureAsync(ModelParameterProfile profile, ChatOptions? caller)
    {
        var capture = new CapturingChatClient();
        var client = new ParameterInjectingChatClient(capture, profile);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], caller);
        return capture.CapturedOptions!;
    }

    [Fact]
    public async Task Seed_FillsTypedSeed_FromProfile()
    {
        var options = await CaptureAsync(Profile(seed: 42), caller: null);

        Assert.Equal(42L, options.Seed);
        // Seed must never be smuggled through additional properties.
        Assert.False(options.AdditionalProperties?.ContainsKey("seed") ?? false);
    }

    [Fact]
    public async Task Seed_CallerValueWins()
    {
        var caller = new ChatOptions { Seed = 999 };

        var options = await CaptureAsync(Profile(seed: 42), caller);

        Assert.Equal(999L, options.Seed);
    }

    [Fact]
    public async Task NumPredictPositive_FillsTypedMaxOutputTokens()
    {
        var options = await CaptureAsync(Profile(numPredict: 128), caller: null);

        Assert.Equal(128, options.MaxOutputTokens);
        Assert.False(options.AdditionalProperties?.ContainsKey("num_predict") ?? false);
    }

    [Fact]
    public async Task NumPredictPositive_CallerMaxOutputTokensWins()
    {
        var caller = new ChatOptions { MaxOutputTokens = 64 };

        var options = await CaptureAsync(Profile(numPredict: 128), caller);

        Assert.Equal(64, options.MaxOutputTokens);
    }

    [Fact]
    public async Task NumPredictZero_OmitsTokenLimitEntirely()
    {
        var options = await CaptureAsync(Profile(numPredict: 0), caller: null);

        Assert.Null(options.MaxOutputTokens);
        Assert.False(options.AdditionalProperties?.ContainsKey("num_predict") ?? false);
    }

    [Fact]
    public async Task NumPredictUnlimited_AddsProviderNumPredictKey()
    {
        var options = await CaptureAsync(Profile(numPredict: -1), caller: null);

        Assert.Null(options.MaxOutputTokens);
        Assert.Equal(-1, Assert.IsType<int>(options.AdditionalProperties!["num_predict"]));
    }

    [Fact]
    public async Task NumPredictUnlimited_DoesNotOverrideCallerTypedLimit()
    {
        var caller = new ChatOptions { MaxOutputTokens = 200 };

        var options = await CaptureAsync(Profile(numPredict: -1), caller);

        Assert.Equal(200, options.MaxOutputTokens);
        Assert.False(options.AdditionalProperties?.ContainsKey("num_predict") ?? false);
    }

    [Fact]
    public async Task NumPredictUnlimited_DoesNotOverrideCallerProviderKey()
    {
        var caller = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["num_predict"] = 50 }
        };

        var options = await CaptureAsync(Profile(numPredict: -1), caller);

        Assert.Equal(50, Assert.IsType<int>(options.AdditionalProperties!["num_predict"]));
    }

    [Fact]
    public async Task RepeatPenalty_AddedAsProviderKey_NeverFrequencyPenalty()
    {
        var options = await CaptureAsync(Profile(repeatPenalty: 1.3), caller: null);

        Assert.Equal(1.3f, Assert.IsType<float>(options.AdditionalProperties!["repeat_penalty"]));
        Assert.Null(options.FrequencyPenalty);
    }

    [Fact]
    public async Task RepeatPenalty_CallerValueWins()
    {
        var caller = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["repeat_penalty"] = 2.0f }
        };

        var options = await CaptureAsync(Profile(repeatPenalty: 1.3), caller);

        Assert.Equal(2.0f, Assert.IsType<float>(options.AdditionalProperties!["repeat_penalty"]));
    }

    [Fact]
    public async Task UnrelatedProperties_ArePreserved()
    {
        var caller = new ChatOptions
        {
            ModelId = "custom-model",
            AdditionalProperties = new AdditionalPropertiesDictionary { ["keep_alive"] = "10m" }
        };

        var options = await CaptureAsync(Profile(), caller);

        Assert.Equal("custom-model", options.ModelId);
        Assert.Equal("10m", options.AdditionalProperties!["keep_alive"]);
    }

    [Fact]
    public async Task StreamingPath_AppliesSameMapping()
    {
        var capture = new CapturingChatClient();
        var client = new ParameterInjectingChatClient(capture, Profile(numPredict: 128, seed: 7));

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            // drain
        }

        var options = capture.CapturedOptions!;
        Assert.Equal(7L, options.Seed);
        Assert.Equal(128, options.MaxOutputTokens);
        Assert.Equal(1.1f, Assert.IsType<float>(options.AdditionalProperties!["repeat_penalty"]));
    }
}
