using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Providers;

public sealed class OllamaProcessInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("size_vram")]
    public long SizeVram { get; set; }
}
