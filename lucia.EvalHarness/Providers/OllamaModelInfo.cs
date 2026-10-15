using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Providers;

public sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>
    /// Formatted display string (e.g., "llama3.2:latest (3.2B Q4_0, 2.0 GB)").
    /// </summary>
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
