using System.Text.Json.Serialization;

namespace lucia.Agents.Models;

/// <summary>
/// Represents a cached routing decision for the prompt cache.
/// Stores which agent was selected for a given prompt so that subsequent
/// identical/similar prompts skip the router LLM call but still execute
/// the agent (tools run fresh every time).
/// </summary>
public sealed class CachedPromptEntry
{
    /// <summary>SHA256 hash of the normalized prompt text, used as the Redis key suffix.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>The normalized prompt text (for display in management UI).</summary>
    public string NormalizedPrompt { get; set; } = string.Empty;

    /// <summary>The agent that was selected by the router for this prompt.</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Router confidence score (0.0–1.0) for the cached routing decision.</summary>
    public double Confidence { get; set; }

    /// <summary>Router reasoning for observability.</summary>
    public string? Reasoning { get; set; }

    /// <summary>Optional additional agents for parallel execution scenarios.</summary>
    public List<string>? AdditionalAgents { get; set; }

    /// <summary>Embedding vector for semantic similarity matching (stored as float[]).</summary>
    [JsonIgnore]
    public float[]? Embedding { get; set; }

    /// <summary>Number of times this cache entry has been hit.</summary>
    public long HitCount { get; set; }

    /// <summary>When this entry was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this entry was last accessed.</summary>
    public DateTime LastHitAt { get; set; } = DateTime.UtcNow;
}
