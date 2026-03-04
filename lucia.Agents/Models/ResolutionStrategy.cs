namespace lucia.Agents.Models;

/// <summary>
/// The strategy used to resolve which entities should be acted upon.
/// </summary>
public enum ResolutionStrategy
{
    /// <summary>Direct entity match won — the query targeted a specific device.</summary>
    Entity,
    /// <summary>Area expansion won — the query targeted a location.</summary>
    Area,
    /// <summary>Floor expansion won — the query targeted an entire floor.</summary>
    Floor,
    /// <summary>No matches found at any level.</summary>
    None
}