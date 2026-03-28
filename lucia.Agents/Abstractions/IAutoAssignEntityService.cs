using lucia.Agents.Models;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Service for automatically assigning entities to agents based on configurable strategies.
/// </summary>
public interface IAutoAssignEntityService
{
    /// <summary>
    /// Preview the auto-assign operation without applying changes.
    /// </summary>
    Task<AutoAssignPreview> PreviewAsync(AutoAssignStrategy strategy, CancellationToken ct = default);

    /// <summary>
    /// Apply the auto-assign operation, persisting entity-to-agent visibility changes.
    /// </summary>
    Task<AutoAssignResult> ApplyAsync(AutoAssignStrategy strategy, CancellationToken ct = default);
}
