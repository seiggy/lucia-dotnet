using lucia.EvalHarness.DataPipeline.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace lucia.EvalHarness.DataPipeline;

/// <summary>
/// Exports evaluation scenarios to YAML format matching the existing TestData structure.
/// </summary>
public sealed class EvalScenarioExporter
{
    private readonly ISerializer _yamlSerializer;

    public EvalScenarioExporter()
    {
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    /// <summary>
    /// Exports scenarios to a YAML file.
    /// </summary>
    /// <param name="scenarios">Scenarios to export.</param>
    /// <param name="outputPath">Output file path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportToYamlAsync(List<EvalScenario> scenarios, string outputPath, CancellationToken ct = default)
    {
        if (scenarios.Count == 0)
        {
            throw new ArgumentException("Cannot export empty scenario list", nameof(scenarios));
        }

        // Group scenarios by category for organized output
        var groupedByCategory = scenarios
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key);

        var yamlDocument = new YamlDocument
        {
            Scenarios = scenarios.Select(ConvertToYamlScenario).ToList()
        };

        var yamlContent = _yamlSerializer.Serialize(yamlDocument);

        // Add header comment
        var header = $"""
            # Evaluation Scenarios
            # Generated from data pipeline on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            # Total scenarios: {scenarios.Count}
            # Sources: {string.Join(", ", scenarios.Select(s => s.Source.Split('-')[0]).Distinct())}
            
            
            """;

        await File.WriteAllTextAsync(outputPath, header + yamlContent, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports scenarios grouped by agent into separate files.
    /// </summary>
    /// <param name="scenarios">Scenarios to export.</param>
    /// <param name="outputDirectory">Output directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportByAgentAsync(List<EvalScenario> scenarios, string outputDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var groupedByAgent = scenarios
            .Where(s => !string.IsNullOrWhiteSpace(s.ExpectedAgent))
            .GroupBy(s => s.ExpectedAgent);

        foreach (var group in groupedByAgent)
        {
            var fileName = $"{group.Key}-generated.yaml";
            var filePath = Path.Combine(outputDirectory, fileName);

            await ExportToYamlAsync(group.ToList(), filePath, ct).ConfigureAwait(false);
        }
    }

    private static YamlScenario ConvertToYamlScenario(EvalScenario scenario)
    {
        var yamlScenario = new YamlScenario
        {
            Id = scenario.Id,
            Description = scenario.Description,
            Category = scenario.Category,
            UserPrompt = scenario.UserPrompt
        };

        if (scenario.ExpectedAgent is not null)
        {
            yamlScenario.Expected = scenario.ExpectedAgent;
        }

        if (scenario.ExpectedToolCalls.Count > 0)
        {
            yamlScenario.ExpectedToolCalls = scenario.ExpectedToolCalls
                .Select(tc => new YamlToolCall
                {
                    Tool = tc.Tool,
                    Arguments = tc.Arguments
                })
                .ToList();
        }

        if (scenario.ResponseMustContain.Count > 0)
        {
            yamlScenario.ResponseMustContain = scenario.ResponseMustContain;
        }

        if (scenario.ResponseMustNotContain.Count > 0)
        {
            yamlScenario.ResponseMustNotContain = scenario.ResponseMustNotContain;
        }

        if (scenario.Criteria.Count > 0)
        {
            yamlScenario.Criteria = scenario.Criteria;
        }

        if (scenario.Metadata.Count > 0)
        {
            yamlScenario.Metadata = scenario.Metadata;
        }

        if (scenario.InitialState is not null)
        {
            yamlScenario.InitialState = scenario.InitialState.ToDictionary(
                kvp => kvp.Key,
                kvp => new YamlEntityState
                {
                    State = kvp.Value.State,
                    Attributes = kvp.Value.Attributes
                }
            );
        }

        if (scenario.ExpectedFinalState is not null)
        {
            yamlScenario.ExpectedFinalState = scenario.ExpectedFinalState.ToDictionary(
                kvp => kvp.Key,
                kvp => new YamlEntityState
                {
                    State = kvp.Value.State,
                    Attributes = kvp.Value.Attributes
                }
            );
        }

        return yamlScenario;
    }

    private sealed class YamlDocument
    {
        public List<YamlScenario> Scenarios { get; set; } = [];
    }

    private sealed class YamlScenario
    {
        public required string Id { get; set; }
        public required string Description { get; set; }
        public required string Category { get; set; }
        public required string UserPrompt { get; set; }
        public string? Expected { get; set; }
        public List<YamlToolCall>? ExpectedToolCalls { get; set; }
        public List<string>? ResponseMustContain { get; set; }
        public List<string>? ResponseMustNotContain { get; set; }
        public List<string>? Criteria { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public Dictionary<string, YamlEntityState>? InitialState { get; set; }
        public Dictionary<string, YamlEntityState>? ExpectedFinalState { get; set; }
    }

    private sealed class YamlToolCall
    {
        public required string Tool { get; set; }
        public Dictionary<string, string> Arguments { get; set; } = [];
    }

    private sealed class YamlEntityState
    {
        public required string State { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = [];
    }
}
