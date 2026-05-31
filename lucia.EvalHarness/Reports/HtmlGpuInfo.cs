using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlGpuInfo
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("vramMb")]
    public int VramMb { get; init; }
}
