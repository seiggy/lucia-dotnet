using System.Text.Json.Serialization;

namespace lucia.Agents.Models;

/// <summary>
/// Represents a cached LLM chat response for agent-level prompt caching.
/// Stores the assistant response (text and/or function calls) so that subsequent
/// identical prompts skip the LLM call. Tool calls are replayed through the
/// function-invoking layer, so tools still execute fresh every time.
/// </summary>
public sealed class CachedChatResponseData
{
    /// <summary>SHA256 hash of the normalized prompt text, used as the Redis key suffix.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>The normalized prompt text (for observability and semantic matching).</summary>
    public string NormalizedPrompt { get; set; } = string.Empty;

    /// <summary>The assistant's text response content, if any.</summary>
    public string? ResponseText { get; set; }

    /// <summary>Tool calls from the assistant response, if any.</summary>
    public List<CachedFunctionCallData>? FunctionCalls { get; set; }

    /// <summary>The model that generated this response.</summary>
    public string? ModelId { get; set; }

    /// <summary>Embedding vector for semantic similarity matching.</summary>
    [JsonIgnore]
    public float[]? Embedding { get; set; }

    /// <summary>Number of times this cache entry has been hit.</summary>
    public long HitCount { get; set; }

    /// <summary>When this entry was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this entry was last accessed.</summary>
    public DateTime LastHitAt { get; set; } = DateTime.UtcNow;
}
