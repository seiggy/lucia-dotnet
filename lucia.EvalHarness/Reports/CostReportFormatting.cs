using System.Globalization;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Reports;

public static class CostReportFormatting
{
    public const string EstimateNote =
        "USD estimates use configured or published token rates and request fees. Context tiers apply to each request's input tokens. " +
        "Model-under-test calls only; judge charges and electricity are excluded. " +
        "Local Ollama/llama.cpp provider cost is zero. Unknown usage or cloud pricing is not free. " +
        "Unreported cache usage receives no discount. Quality first; cost breaks exact ties.";

    public static string Usd(decimal? value) =>
        value.HasValue ? "$" + value.Value.ToString("0.############################", CultureInfo.InvariantCulture) : "Unknown";

    public static string Cost(InferenceCostSummary cost) =>
        cost.EstimatedUsd.HasValue
            ? $"{Usd(cost.EstimatedUsd)} ({cost.CostStatus})"
            : cost.CostStatus == "no_calls" ? "Not run" : $"Unknown ({cost.CostStatus})";

    public static string Tokens(InferenceCostSummary cost) =>
        $"{Count(cost.InputTokens)} / {Count(cost.OutputTokens)} / {Count(cost.CachedInputTokens)}";

    private static string Count(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
