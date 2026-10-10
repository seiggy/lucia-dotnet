using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucia.EvalHarness.Evaluation;

internal sealed class ScenarioDocument
{
    public List<TestScenario> Scenarios { get; set; } = [];
}
