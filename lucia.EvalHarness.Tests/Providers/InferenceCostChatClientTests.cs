using FakeItEasy;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.Providers;

public sealed class InferenceCostChatClientTests
{
    [Fact]
    public async Task MultipleCalls_CountCachedInputOnceAndDoNotAddReasoningToOutput()
    {
        using var client = CreateClient(new UsageDetails
        {
            InputTokenCount = 1_000,
            CachedInputTokenCount = 400,
            OutputTokenCount = 200,
            ReasoningTokenCount = 100
        });
        using var scope = client.BeginScope();

        await client.GetResponseAsync([]);
        await client.GetResponseAsync([]);
        var cost = scope.Complete();

        Assert.Equal(2, cost.CallCount);
        Assert.Equal(2_000, cost.InputTokens);
        Assert.Equal(800, cost.CachedInputTokens);
        Assert.Equal(400, cost.OutputTokens);
        Assert.Equal(0.0044m, cost.EstimatedUsd);
        Assert.Equal("estimated", cost.CostStatus);
    }

    [Fact]
    public async Task MissingCloudUsageAndPricing_AreUnknown()
    {
        using var missingUsage = CreateClient(null);
        using var usageScope = missingUsage.BeginScope();
        await missingUsage.GetResponseAsync([]);
        Assert.Null(usageScope.Complete().EstimatedUsd);
        Assert.Equal("missing_usage", usageScope.Complete().CostStatus);

        using var missingPricing = CreateClient(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 }, priced: false);
        using var pricingScope = missingPricing.BeginScope();
        await missingPricing.GetResponseAsync([]);
        Assert.Equal("missing_pricing", pricingScope.Complete().CostStatus);
        Assert.Null(pricingScope.Complete().EstimatedUsd);
        Assert.Equal(10, pricingScope.Complete().InputTokens);
    }

    [Theory]
    [InlineData(InferenceBackendType.Ollama)]
    [InlineData(InferenceBackendType.OpenAICompat)]
    public async Task LocalProvider_IsZeroEvenWithoutUsageButUnexecutedIsNotFree(InferenceBackendType type)
    {
        using var client = CreateClient(null, type);
        using (var empty = client.BeginScope())
        {
            Assert.Equal("no_calls", empty.Complete().CostStatus);
            Assert.Null(empty.Complete().EstimatedUsd);
        }

        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);
        var cost = scope.Complete();
        Assert.Equal(0m, cost.EstimatedUsd);
        Assert.Equal("local", cost.CostStatus);
        Assert.Equal("missing_usage", cost.UsageStatus);
        Assert.Null(cost.InputTokens);
    }

    [Fact]
    public async Task PartialUsage_DoesNotPresentPartialTotalAsFullCost()
    {
        var call = 0;
        using var client = new InferenceCostChatClient(new ScriptedChatClient(_ => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = ++call == 1 ? new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 } : null
            })), Backend(), "model");
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);
        await client.GetResponseAsync([]);
        var cost = scope.Complete();
        Assert.Null(cost.EstimatedUsd);
        Assert.Null(cost.InputTokens);
        Assert.Equal("partial", cost.UsageStatus);
    }

    [Fact]
    public async Task IncompleteRequest_RemainsUnknownAndCannotContaminateNextScope()
    {
        var response = new TaskCompletionSource<ChatResponse>();
        using var client = new InferenceCostChatClient(new ScriptedChatClient(_ => response.Task), Backend(), "model");
        using var first = client.BeginScope();
        var request = client.GetResponseAsync([]);
        var incomplete = first.Complete();
        Assert.Equal(1, incomplete.CallCount);
        Assert.Null(incomplete.EstimatedUsd);

        using var next = client.BeginScope();
        response.SetResult(new ChatResponse { Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 } });
        await request;
        Assert.Equal(0, next.Complete().CallCount);
        Assert.Null(first.Complete().EstimatedUsd);
    }

    [Fact]
    public async Task StreamingUsage_IsCountedOncePerRawRequest()
    {
        var inner = A.Fake<IChatClient>();
        A.CallTo(() => inner.GetStreamingResponseAsync(
                A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
            .Returns(Stream());
        using var client = new InferenceCostChatClient(inner, Backend(), "model");
        using var scope = client.BeginScope();

        await foreach (var _ in client.GetStreamingResponseAsync([]))
        {
        }

        var cost = scope.Complete();
        Assert.Equal(1, cost.CallCount);
        Assert.Equal(100, cost.InputTokens);
        Assert.Equal(20, cost.OutputTokens);
        Assert.Equal(0.00028m, cost.EstimatedUsd);
        Assert.Same(client, new ParameterInjectingChatClient(client, ModelParameterProfile.Default)
            .GetService<InferenceCostChatClient>());
    }

    [Fact]
    public void Aggregates_DoNotTreatEmptyOrUnknownAsFree()
    {
        Assert.Null(InferenceCostSummary.Aggregate([]).EstimatedUsd);
        Assert.Equal("no_calls", InferenceCostSummary.Aggregate([]).CostStatus);
        var known = new InferenceCostSummary { CallCount = 1, EstimatedUsd = 0.1m, CostStatus = "estimated" };
        var unknown = new InferenceCostSummary { CallCount = 1, CostStatus = "missing_pricing" };
        var aggregate = InferenceCostSummary.Aggregate([known, unknown]);
        Assert.Null(aggregate.EstimatedUsd);
        Assert.Equal("partial", aggregate.CostStatus);
    }

    [Theory]
    [InlineData(271999, "0.544078")]
    [InlineData(272000, "1.36045")]
    [InlineData(600000, "4.2006")]
    public void ContextTiers_SelectHighestQualifyingThresholdPerRequest(long input, string expectedUsd)
    {
        using var scope = new InferenceCostScope(false, new ModelPricing
        {
            InputUsdPerMillionTokens = 2m,
            OutputUsdPerMillionTokens = 4m,
            ContextTiers =
            [
                new() { MinimumInputTokens = 600000, InputUsdPerMillionTokens = 7m, OutputUsdPerMillionTokens = 30m },
                new() { MinimumInputTokens = 272000, InputUsdPerMillionTokens = 5m, OutputUsdPerMillionTokens = 22.5m }
            ]
        });
        scope.Record(scope.StartCall(), new UsageDetails { InputTokenCount = input, OutputTokenCount = 20 });
        Assert.Equal(decimal.Parse(expectedUsd, System.Globalization.CultureInfo.InvariantCulture), scope.Complete().EstimatedUsd);
    }

    [Fact]
    public void ContextTiers_DoNotApplyThresholdToAggregateTestTokens()
    {
        using var scope = new InferenceCostScope(false, new ModelPricing
        {
            InputUsdPerMillionTokens = 2m,
            OutputUsdPerMillionTokens = 4m,
            ContextTiers = [new() { MinimumInputTokens = 272000, InputUsdPerMillionTokens = 5m }]
        });
        var usage = new UsageDetails { InputTokenCount = 200000, OutputTokenCount = 20 };
        scope.Record(scope.StartCall(), usage);
        scope.Record(scope.StartCall(), usage);
        Assert.Equal(400000, scope.Complete().InputTokens);
        Assert.Equal(0.80016m, scope.Complete().EstimatedUsd);
    }

    [Fact]
    public void ContextTiers_InheritMissingRatesFromBaseAndUseFullInputForThreshold()
    {
        using var scope = new InferenceCostScope(false, new ModelPricing
        {
            InputUsdPerMillionTokens = 2m,
            OutputUsdPerMillionTokens = 4m,
            CachedInputUsdPerMillionTokens = 0.5m,
            ContextTiers = [new() { MinimumInputTokens = 272000, InputUsdPerMillionTokens = 5m }]
        });
        scope.Record(scope.StartCall(), new UsageDetails
        {
            InputTokenCount = 272000,
            CachedInputTokenCount = 172000,
            OutputTokenCount = 20
        });
        Assert.Equal(0.58608m, scope.Complete().EstimatedUsd);
    }

    [Fact]
    public void ContextTiers_MissingCachedRateStaysUnknown()
    {
        using var scope = new InferenceCostScope(false, new ModelPricing
        {
            InputUsdPerMillionTokens = 2m,
            OutputUsdPerMillionTokens = 4m,
            ContextTiers = [new() { MinimumInputTokens = 272000, InputUsdPerMillionTokens = 5m }]
        });
        scope.Record(scope.StartCall(), new UsageDetails
        {
            InputTokenCount = 272000,
            CachedInputTokenCount = 1000,
            OutputTokenCount = 20
        });
        Assert.Null(scope.Complete().EstimatedUsd);
        Assert.Equal("missing_pricing", scope.Complete().CostStatus);
    }

    [Fact]
    public void RequestFee_IsAddedOncePerCallAlongsideTieredTokenPrice()
    {
        using var scope = new InferenceCostScope(false, new ModelPricing
        {
            InputUsdPerMillionTokens = 2m,
            OutputUsdPerMillionTokens = 4m,
            RequestUsd = 0.001m,
            ContextTiers = [new() { MinimumInputTokens = 272000, InputUsdPerMillionTokens = 5m }]
        });
        var usage = new UsageDetails { InputTokenCount = 272000, OutputTokenCount = 20 };
        scope.Record(scope.StartCall(), usage);
        scope.Record(scope.StartCall(), usage);
        Assert.Equal(2.72216m, scope.Complete().EstimatedUsd);
    }

    private static InferenceCostChatClient CreateClient(
        UsageDetails? usage,
        InferenceBackendType type = InferenceBackendType.AzureFoundry,
        bool priced = true) =>
        new(new ScriptedChatClient(_ => Task.FromResult(new ChatResponse { Usage = usage })),
            Backend(type, priced), "model");

    private static InferenceBackend Backend(
        InferenceBackendType type = InferenceBackendType.AzureFoundry,
        bool priced = true) => new()
    {
        Name = "backend",
        Endpoint = "https://example.test",
        Type = type,
        ModelPricing = priced
            ? new() { ["model"] = new ModelPricing
            {
                InputUsdPerMillionTokens = 2m,
                OutputUsdPerMillionTokens = 4m,
                CachedInputUsdPerMillionTokens = 0.5m
            } }
            : []
    };

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        await Task.Yield();
        yield return new ChatResponseUpdate
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 })]
        };
    }
}
