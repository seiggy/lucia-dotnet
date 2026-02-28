using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Lightweight <see cref="IMatchableEntity"/> built on-the-fly from
/// <see cref="EntityLocationInfo"/> when the skill's internal device cache
/// has not yet been populated. Used by the optimizer to run against the
/// shared entity location service.
/// </summary>
public sealed class MatchableEntityInfo : IMatchableEntity
{
    public required string EntityId { get; init; }
    public required string MatchableName { get; init; }
    public Embedding<float>? NameEmbedding { get; init; }
    public string[] PhoneticKeys { get; init; } = [];
}
