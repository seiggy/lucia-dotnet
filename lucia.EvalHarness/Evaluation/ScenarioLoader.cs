using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Loads <see cref="TestScenario"/> definitions from YAML files.
/// </summary>
public static class ScenarioLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<TestScenario> LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return [];

        var yaml = File.ReadAllText(path);
        var doc = Deserializer.Deserialize<ScenarioDocument>(yaml);
        return doc?.Scenarios ?? [];
    }
}

internal sealed class ScenarioDocument
{
    public List<TestScenario> Scenarios { get; set; } = [];
}
