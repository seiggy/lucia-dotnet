using System.Text.Json.Serialization;

namespace lucia.Agents.Configuration;

/// <summary>
/// Indicates whether a model provider is used for chat completions or embedding generation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModelPurpose
{
    /// <summary>Chat completion / text generation (IChatClient).</summary>
    Chat,

    /// <summary>Text embedding generation (IEmbeddingGenerator).</summary>
    Embedding
}
