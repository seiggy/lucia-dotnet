using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Providers;

public sealed class OllamaProcessResponse
{
    [JsonPropertyName("models")]
    public List<OllamaProcessInfo> Models { get; set; } = [];
}
