using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Configuration;

/// <summary>
/// User-configured LLM provider connection stored in MongoDB.
/// Each provider maps to a unique key and produces an IChatClient at runtime.
/// </summary>
public sealed class ModelProvider
{
    /// <summary>
    /// Unique key for this provider (e.g., "gpt4o-prod", "claude-sonnet").
    /// Used as the reference key in agent definitions.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Human-readable display name (e.g., "GPT-4o Production").
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// The LLM provider SDK to use for creating the client.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public ProviderType ProviderType { get; set; }

    /// <summary>
    /// Whether this provider produces a chat client or an embedding generator.
    /// Defaults to <see cref="ModelPurpose.Chat"/> for backward compatibility.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public ModelPurpose Purpose { get; set; } = ModelPurpose.Chat;

    /// <summary>
    /// Base endpoint URL. Required for most providers, optional for cloud defaults
    /// (e.g., OpenAI uses api.openai.com by default if null).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Model or deployment name (e.g., "gpt-4o", "claude-sonnet-4-20250514", "phi3:mini").
    /// </summary>
    public string ModelName { get; set; } = default!;

    /// <summary>
    /// Authentication configuration for this provider.
    /// </summary>
    public ModelAuthConfig Auth { get; set; } = new();

    /// <summary>
    /// Optional metadata from GitHub Copilot CLI model discovery.
    /// Only populated for <see cref="ProviderType.GitHubCopilot"/> providers.
    /// </summary>
    public CopilotModelMetadata? CopilotMetadata { get; set; }

    /// <summary>
    /// Whether this provider is available for use by agents.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Indicates the provider was seeded from environment/connection-string configuration.
    /// Built-in providers cannot be deleted, only edited.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string CollectionName = "model_providers";
}
