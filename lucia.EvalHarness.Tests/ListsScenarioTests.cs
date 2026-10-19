using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tests;

public sealed class ListsScenarioTests
{
    private static readonly string[] SkillTools =
    [
        "AddToShoppingList", "AddToTodoList", "ListShoppingItems", "ListTodoItems", "ListTodoEntities"
    ];

    [Fact]
    public void ListsDataset_UsesOnlyToolsTheSkillExposes()
    {
        var scenarios = ScenarioLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", "lists-agent.yaml"));

        Assert.NotEmpty(scenarios);
        var referenced = scenarios
            .SelectMany(static scenario => scenario.ExpectedToolCalls)
            .SelectMany(static call => call.Alternatives.Prepend(call))
            .Select(static call => call.Tool)
            .Distinct();
        Assert.All(referenced, tool => Assert.Contains(tool, SkillTools));
    }
}
