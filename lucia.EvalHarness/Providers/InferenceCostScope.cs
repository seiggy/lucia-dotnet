using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Providers;

/// <summary>A single test's calls. Closing freezes the result, including unknown in-flight usage.</summary>
public sealed class InferenceCostScope(bool isLocal, ModelPricing? pricing) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<InferenceCostSummary> _calls = [];
    private InferenceCostSummary? _completed;

    internal int StartCall()
    {
        lock (_gate)
        {
            if (_completed is not null)
            {
                return -1;
            }

            _calls.Add(Estimate(null));
            return _calls.Count - 1;
        }
    }

    internal void Record(int callIndex, UsageDetails? usage)
    {
        lock (_gate)
        {
            if (_completed is null && callIndex >= 0)
            {
                _calls[callIndex] = Estimate(usage);
            }
        }
    }

    public InferenceCostSummary Complete()
    {
        lock (_gate)
        {
            return _completed ??= InferenceCostSummary.Aggregate(_calls);
        }
    }

    public void Dispose() => Complete();

    private InferenceCostSummary Estimate(UsageDetails? usage)
    {
        var input = usage?.InputTokenCount is >= 0 ? usage.InputTokenCount : null;
        var output = usage?.OutputTokenCount is >= 0 ? usage.OutputTokenCount : null;
        var cached = usage?.CachedInputTokenCount;
        var validUsage = input.HasValue && output.HasValue &&
            cached is not < 0 && (!cached.HasValue || cached <= input);
        decimal? usd = null;
        var status = "missing_usage";

        if (isLocal)
        {
            usd = 0m;
            status = "local";
        }
        else if (validUsage)
        {
            status = "missing_pricing";
            var cachedTokens = cached ?? 0;
            var rates = GetRequestPricing(input!.Value);
            if (rates is { InputUsdPerMillionTokens: >= 0, OutputUsdPerMillionTokens: >= 0, RequestUsd: >= 0 } &&
                (cachedTokens == 0 || rates.CachedInputUsdPerMillionTokens is >= 0))
            {
                // Reasoning tokens are already included in output. Unreported cache usage gets no discount.
                usd = ((input.Value - cachedTokens) * rates.InputUsdPerMillionTokens.Value +
                    cachedTokens * (rates.CachedInputUsdPerMillionTokens ?? 0m) +
                    output!.Value * rates.OutputUsdPerMillionTokens.Value) / 1_000_000m + rates.RequestUsd;
                status = "estimated";
            }
        }

        return new()
        {
            CallCount = 1,
            InputTokens = input,
            OutputTokens = output,
            CachedInputTokens = cached is >= 0 && cached <= input ? cached : null,
            EstimatedUsd = usd,
            UsageStatus = validUsage ? "available" : "missing_usage",
            CostStatus = status
        };
    }

    private ModelPricing? GetRequestPricing(long inputTokens)
    {
        var tier = pricing?.ContextTiers
            .Where(candidate => candidate.MinimumInputTokens >= 0 && candidate.MinimumInputTokens <= inputTokens)
            .MaxBy(candidate => candidate.MinimumInputTokens);
        if (pricing is null || tier is null)
        {
            return pricing;
        }

        return pricing with
        {
            InputUsdPerMillionTokens = tier.InputUsdPerMillionTokens ?? pricing.InputUsdPerMillionTokens,
            OutputUsdPerMillionTokens = tier.OutputUsdPerMillionTokens ?? pricing.OutputUsdPerMillionTokens,
            CachedInputUsdPerMillionTokens = tier.CachedInputUsdPerMillionTokens ?? pricing.CachedInputUsdPerMillionTokens
        };
    }
}
