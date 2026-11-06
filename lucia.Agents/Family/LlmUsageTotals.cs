namespace lucia.Agents.Family;

public sealed record LlmUsageTotals(int Calls, long InputTokens, long OutputTokens, decimal CostUsd)
{
    public static LlmUsageTotals Empty { get; } = new(0, 0, 0, 0m);
}
