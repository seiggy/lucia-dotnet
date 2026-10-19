namespace lucia.AgentHost.Memory;

/// <summary>
/// Optional Neo4j-backed Agent Memory. Off by default; the built-in user memory provider is used instead.
/// Read once at startup, so changing <see cref="Enabled"/> requires a restart.
/// </summary>
public sealed class AgentMemoryOptions
{
    public const string SectionName = "AgentMemory";

    public bool Enabled { get; set; }

    public string Uri { get; set; } = "bolt://localhost:7687";

    public string Username { get; set; } = "neo4j";

    public string Password { get; set; } = string.Empty;

    public string Database { get; set; } = "neo4j";

    /// <summary>
    /// Must match the embedding provider's vector size. Neo4j fixes it when the vector indexes are first created.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// Chat model provider for fact extraction. Empty uses the default model.
    /// </summary>
    public string? ModelConnectionName { get; set; }

    /// <summary>
    /// Embedding provider for recall. Empty uses the default embedding provider.
    /// </summary>
    public string? EmbeddingProviderName { get; set; }
}
