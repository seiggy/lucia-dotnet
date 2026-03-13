namespace lucia.Wyoming.CommandRouting;

using lucia.Agents.Abstractions;

public sealed class CommandPatternRegistry
{
    private readonly IReadOnlyList<CommandPattern> _patterns;

    public CommandPatternRegistry(IEnumerable<ICommandPatternProvider> providers)
    {
        _patterns = providers
            .SelectMany(static provider => provider.GetCommandPatterns())
            .Select(static definition => new CommandPattern
            {
                Id = definition.Id,
                SkillId = definition.SkillId,
                Action = definition.Action,
                Templates = definition.Templates,
                MinConfidence = definition.MinConfidence,
                Priority = definition.Priority,
            })
            .OrderByDescending(static pattern => pattern.Priority)
            .ToList();
    }

    public IReadOnlyList<CommandPattern> GetAllPatterns() => _patterns;
}
