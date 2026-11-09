using FakeItEasy;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace lucia.EvalHarness.Tests.Providers;

public sealed class ProviderCostClassificationTests
{
    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat, "https://provider.example/v1")]
    [InlineData(InferenceBackendType.OpenAICompat, "http://localhost:4000/v1")]
    [InlineData(InferenceBackendType.Ollama, "https://provider.example")]
    [InlineData(InferenceBackendType.Ollama, "http://localhost:11434")]
    [InlineData(InferenceBackendType.AzureFoundry, "https://provider.example")]
    [InlineData(InferenceBackendType.AzureFoundry, "http://localhost:4000")]
    [InlineData(InferenceBackendType.OpenRouter, "https://provider.example/v1")]
    [InlineData(InferenceBackendType.OpenRouter, "http://localhost:4000/v1")]
    public async Task ConfiguredPricing_IsUsedRegardlessOfProtocolOrAddress(InferenceBackendType type, string endpoint)
    {
        using var client = Client(Backend(type, endpoint), Usage());
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);

        var cost = scope.Complete();
        Assert.Equal(0.00028m, cost.EstimatedUsd);
        Assert.Equal("estimated", cost.CostStatus);
        Assert.Equal(100, cost.InputTokens);
    }

    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat)]
    [InlineData(InferenceBackendType.Ollama)]
    [InlineData(InferenceBackendType.AzureFoundry)]
    [InlineData(InferenceBackendType.OpenRouter)]
    public async Task MissingPricing_IsUnknownNotFree(InferenceBackendType type)
    {
        var backend = Backend(type);
        backend.ModelPricing.Clear();
        using var client = Client(backend, Usage());
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);

        Assert.Null(scope.Complete().EstimatedUsd);
        Assert.Equal("missing_pricing", scope.Complete().CostStatus);
        Assert.Equal("Unknown (missing_pricing)", CostReportFormatting.Cost(scope.Complete()));
    }

    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat)]
    [InlineData(InferenceBackendType.Ollama)]
    [InlineData(InferenceBackendType.AzureFoundry)]
    [InlineData(InferenceBackendType.OpenRouter)]
    public async Task MissingUsage_IsUnknownEvenWithPrices(InferenceBackendType type)
    {
        using var client = Client(Backend(type), null);
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);
        Assert.Null(scope.Complete().EstimatedUsd);
        Assert.Equal("missing_usage", scope.Complete().CostStatus);
    }

    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat)]
    [InlineData(InferenceBackendType.Ollama)]
    [InlineData(InferenceBackendType.AzureFoundry)]
    [InlineData(InferenceBackendType.OpenRouter)]
    public async Task ExplicitNoProviderCharge_IsZeroForAnyProtocolWithoutInventingUsage(InferenceBackendType type)
    {
        var backend = Backend(type, noProviderCharge: true);
        using var client = Client(backend, null);
        using var scope = client.BeginScope();
        Assert.Null(scope.Complete().EstimatedUsd);

        // A completed scope is frozen. Start a fresh one for the actual call.
        using var executed = client.BeginScope();
        await client.GetResponseAsync([]);
        var cost = executed.Complete();
        Assert.Equal(0m, cost.EstimatedUsd);
        Assert.Equal("no_provider_charge", cost.CostStatus);
        Assert.Equal("missing_usage", cost.UsageStatus);
        Assert.Null(cost.InputTokens);
    }

    [Theory]
    [InlineData(InferenceBackendType.OpenAICompat)]
    [InlineData(InferenceBackendType.Ollama)]
    [InlineData(InferenceBackendType.AzureFoundry)]
    [InlineData(InferenceBackendType.OpenRouter)]
    public async Task ExplicitNoProviderCharge_TakesPrecedenceOverStoredPrices(InferenceBackendType type)
    {
        using var client = Client(Backend(type, noProviderCharge: true), Usage());
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);
        Assert.Equal(0m, scope.Complete().EstimatedUsd);
        Assert.Equal("no_provider_charge", scope.Complete().CostStatus);
        Assert.Equal(100, scope.Complete().InputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_UsesTheSameExplicitBillingPolicy(bool noProviderCharge)
    {
        var inner = A.Fake<IChatClient>();
        A.CallTo(() => inner.GetStreamingResponseAsync(
                A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
            .Returns(Stream());
        using var client = new InferenceCostChatClient(inner,
            Backend(InferenceBackendType.OpenAICompat, noProviderCharge: noProviderCharge), "model");
        using var scope = client.BeginScope();
        await foreach (var _ in client.GetStreamingResponseAsync([]))
        {
        }
        Assert.Equal(noProviderCharge ? 0m : 0.00028m, scope.Complete().EstimatedUsd);
        Assert.Equal(noProviderCharge ? "no_provider_charge" : "estimated", scope.Complete().CostStatus);
        Assert.Equal(1, scope.Complete().CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOllamaSettings_PropagateExplicitDeclaration(bool noProviderCharge)
    {
        var values = new Dictionary<string, string?>
        {
            ["Ollama:Endpoint"] = "http://localhost:11434",
            ["Ollama:NoProviderCharge"] = noProviderCharge.ToString()
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<HarnessConfiguration>()!;
        config.Validate();
        using var client = Client(config.GetEffectiveBackends().Single(), null);
        using var scope = client.BeginScope();
        await client.GetResponseAsync([]);
        Assert.Equal(noProviderCharge ? "no_provider_charge" : "missing_usage", scope.Complete().CostStatus);
        Assert.Equal(noProviderCharge ? 0m : (decimal?)null, scope.Complete().EstimatedUsd);
    }

    [Fact]
    public void Aggregation_CombinesKnownNoChargeAndEstimatedCostsWithoutLosingUnknowns()
    {
        var free = new InferenceCostSummary { CallCount = 1, EstimatedUsd = 0m, CostStatus = "no_provider_charge" };
        var paid = new InferenceCostSummary { CallCount = 1, EstimatedUsd = 0.2m, CostStatus = "estimated" };
        var unknown = new InferenceCostSummary { CallCount = 1, CostStatus = "missing_pricing" };
        var known = InferenceCostSummary.Aggregate([free, paid]);
        Assert.Equal(0.2m, known.EstimatedUsd);
        Assert.Equal("estimated", known.CostStatus);
        Assert.Null(InferenceCostSummary.Aggregate([free, paid, unknown]).EstimatedUsd);
        Assert.Equal("partial", InferenceCostSummary.Aggregate([free, paid, unknown]).CostStatus);
    }

    [Theory]
    [InlineData("Ollama:Endpoint", "http://localhost:11434")]
    [InlineData("Backends:0:Name", "backend")]
    public void MissingDeclaration_DoesNotDefaultToNoCharge(string key, string value)
    {
        var values = new Dictionary<string, string?> { [key] = value };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<HarnessConfiguration>()!;
        Assert.False(config.GetEffectiveBackends().Single().NoProviderCharge);
    }

    private static InferenceCostChatClient Client(InferenceBackend backend, UsageDetails? usage) =>
        new(new ScriptedChatClient(_ => Task.FromResult(new ChatResponse { Usage = usage })), backend, "model");

    private static UsageDetails Usage() => new() { InputTokenCount = 100, OutputTokenCount = 20 };

    private static InferenceBackend Backend(InferenceBackendType type,
        string endpoint = "https://provider.example/v1", bool noProviderCharge = false)
    {
        // Bind the planned setting so these regressions also compile against the old implementation.
        var values = new Dictionary<string, string?>
        {
            ["Name"] = "backend",
            ["Endpoint"] = endpoint,
            ["Type"] = type.ToString(),
            ["NoProviderCharge"] = noProviderCharge.ToString(),
            ["ModelPricing:model:InputUsdPerMillionTokens"] = "2",
            ["ModelPricing:model:OutputUsdPerMillionTokens"] = "4"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<InferenceBackend>()!;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream()
    {
        yield return new ChatResponseUpdate { Contents = [new UsageContent(Usage())] };
        await Task.CompletedTask;
    }
}
