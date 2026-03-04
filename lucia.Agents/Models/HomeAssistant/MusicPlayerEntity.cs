using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Represents a cached Music Assistant player endpoint with semantic lookup metadata.
/// </summary>
public sealed class MusicPlayerEntity : IMatchableEntity
{
    /// <summary>
    /// The Home Assistant entity id (e.g. media_player.satellite1_living_room).
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The friendly display name reported by Home Assistant.
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>
    /// Optional config entry id that identifies the backing Music Assistant instance.
    /// </summary>
    public string? ConfigEntryId { get; set; }

    /// <summary>
    /// Indicates if this endpoint belongs to the Satellite1 mesh (friendly/entity id contains "satellite").
    /// </summary>
    public bool IsSatellite { get; set; }
    
    /// <inheritdoc />
    public string[] PhoneticKeys { get; set; } = [];

    /// <summary>
    /// Cached embedding for semantic similarity searches.
    /// </summary>
    public Embedding<float> NameEmbedding { get; set; } = null!;
    // ── IMatchableEntity ────────────────────────────────────────

    string IMatchableEntity.MatchableName => FriendlyName;
    Embedding<float>? IMatchableEntity.NameEmbedding => NameEmbedding;
}
