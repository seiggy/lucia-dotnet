using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Providers;

public sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo> Models { get; set; } = [];
}
