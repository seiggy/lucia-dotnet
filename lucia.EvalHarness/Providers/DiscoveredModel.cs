using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Providers;

/// <summary>A model advertised by an inference backend.</summary>
public sealed class DiscoveredModel
{
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public string? ParameterSize { get; init; }
    public string? QuantizationLevel { get; init; }
    public ModelPricing? Pricing { get; init; }

    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Name };
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(ParameterSize)) meta.Add(ParameterSize);
            if (!string.IsNullOrEmpty(QuantizationLevel)) meta.Add(QuantizationLevel);
            if (Size > 0) meta.Add($"{Size / (1024.0 * 1024 * 1024):F1} GB");
            if (meta.Count > 0) parts.Add($"({string.Join(", ", meta)})");
            return string.Join(" ", parts);
        }
    }
}
