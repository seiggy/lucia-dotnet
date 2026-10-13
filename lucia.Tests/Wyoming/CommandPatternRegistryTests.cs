using lucia.Agents.Abstractions;
using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

public sealed class CommandPatternRegistryTests
{
    [Fact]
    public void Registry_PartitionsCommandAndQueryPatterns()
    {
        var registry = new CommandPatternRegistry(new ICommandPatternProvider[] { new TestProvider() });

        Assert.Equal(3, registry.GetAllPatterns().Count);
        Assert.Equal(2, registry.GetCommandPatterns().Count);

        var queryPattern = Assert.Single(registry.GetQueryPatterns());
        Assert.Equal("security-state", queryPattern.Id);
        Assert.True(queryPattern.IsQuery);

        Assert.All(registry.GetCommandPatterns(), static pattern => Assert.False(pattern.IsQuery));
    }
}

file sealed class TestProvider : ICommandPatternProvider
{
    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
    [
        new()
        {
            Id = "light-on-off",
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["turn {action:on|off} [the] {entity}"],
        },
        new()
        {
            Id = "climate-set-temp",
            SkillId = "ClimateControlSkill",
            Action = "set_temperature",
            Templates = ["set [the] thermostat to {value}"],
        },
        new()
        {
            Id = "security-state",
            SkillId = "SecurityControlSkill",
            Action = "status",
            Templates = ["is [the] {entity} [open|closed|locked|unlocked]"],
            MinConfidence = 0.6f,
            IsQuery = true,
        },
    ];
}
