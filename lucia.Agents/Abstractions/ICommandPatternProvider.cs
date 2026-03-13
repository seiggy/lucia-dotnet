namespace lucia.Agents.Abstractions;

/// <summary>
/// Skills implement this to expose command patterns for fast-path routing.
/// </summary>
public interface ICommandPatternProvider
{
    /// <summary>
    /// Returns command patterns as portable definitions that can be adapted by the Wyoming routing layer.
    /// Templates use syntax: {name} = required, {name:a|b} = constrained, [word] = optional.
    /// </summary>
    IReadOnlyList<CommandPatternDefinition> GetCommandPatterns();
}
