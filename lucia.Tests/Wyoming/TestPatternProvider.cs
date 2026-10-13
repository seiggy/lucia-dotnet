using lucia.Agents.Abstractions;
using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

internal sealed class TestPatternProvider(CommandPattern[] patterns) : ICommandPatternProvider
{
    private readonly CommandPattern[] _patterns = patterns;

    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
        _patterns.Select(static pattern => new CommandPatternDefinition
        {
            Id = pattern.Id,
            SkillId = pattern.SkillId,
            Action = pattern.Action,
            Templates = pattern.Templates.ToArray(),
            MinConfidence = pattern.MinConfidence,
            Priority = pattern.Priority,
        }).ToList();
}
