using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// MongoDB document representing a response template for a skill action.
/// Multiple template strings allow varied, natural-sounding responses.
/// </summary>
public sealed class ResponseTemplate
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Skill that owns this template (e.g. "LightControlSkill").</summary>
    public required string SkillId { get; init; }

    /// <summary>Action within the skill (e.g. "toggle", "brightness").</summary>
    public required string Action { get; init; }

    /// <summary>
    /// One or more template strings containing <c>{placeholder}</c> tokens
    /// that are replaced with captured values at render time.
    /// </summary>
    public required string[] Templates { get; init; }

    /// <summary>Whether this template was seeded by the system defaults.</summary>
    public bool IsDefault { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
