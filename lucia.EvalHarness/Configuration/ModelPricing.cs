namespace lucia.EvalHarness.Configuration;

/// <summary>Estimated USD prices per million tokens. Null means the rate is unknown.</summary>
public sealed record ModelPricing
{
    public decimal? InputUsdPerMillionTokens { get; init; }
    public decimal? OutputUsdPerMillionTokens { get; init; }
    public decimal? CachedInputUsdPerMillionTokens { get; init; }
    public decimal RequestUsd { get; init; }
    public long MinimumInputTokens { get; init; }
    public IReadOnlyList<ModelPricing> ContextTiers { get; init; } = [];
}
