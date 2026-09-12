using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Evaluation;

/// <summary>Model-under-test provider charges only, estimated in USD. Electricity and judge calls are excluded.</summary>
public sealed record InferenceCostSummary
{
    public static InferenceCostSummary Untracked { get; } = new() { CostStatus = "untracked", UsageStatus = "untracked" };

    public int CallCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? InputTokens { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? OutputTokens { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? CachedInputTokens { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? EstimatedUsd { get; init; }
    public string UsageStatus { get; init; } = "no_calls";
    public string CostStatus { get; init; } = "no_calls";

    public static InferenceCostSummary Aggregate(IEnumerable<InferenceCostSummary> summaries)
    {
        var values = summaries.ToList();
        if (values.Count == 0)
        {
            return new();
        }

        return new()
        {
            CallCount = values.Sum(value => value.CallCount),
            InputTokens = SumKnown(values.Select(value => value.InputTokens)),
            OutputTokens = SumKnown(values.Select(value => value.OutputTokens)),
            CachedInputTokens = SumKnown(values.Select(value => value.CachedInputTokens)),
            EstimatedUsd = values.All(value => value.EstimatedUsd.HasValue)
                ? values.Sum(value => value.EstimatedUsd!.Value)
                : null,
            UsageStatus = CombineStatus(values.Select(value => value.UsageStatus)),
            CostStatus = CombineStatus(values.Select(value => value.CostStatus))
        };
    }

    private static long? SumKnown(IEnumerable<long?> counts)
    {
        var values = counts.ToList();
        return values.All(value => value.HasValue) ? values.Sum(value => value!.Value) : null;
    }

    private static string CombineStatus(IEnumerable<string> statuses)
    {
        var values = statuses.Distinct().ToList();
        if (values.Count == 1)
        {
            return values[0];
        }

        return values.All(value => value is "estimated" or "local") ? "estimated" : "partial";
    }
}
