namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Configuration for parameter sweep experiments. Defines which parameter
/// dimensions to vary, which models to target, and sweep constraints.
/// </summary>
public sealed class ParameterSweepConfig
{
    /// <summary>
    /// The model to use as the quality baseline (benchmark target).
    /// "auto" selects the highest-scoring model from the most recent standard eval run.
    /// Otherwise, specify a model name like "llama3.2:latest".
    /// </summary>
    public string BaselineModel { get; set; } = "auto";

    /// <summary>
    /// Models to sweep parameters on.
    /// "auto:top3-smallest" selects the 3 smallest models by parameter count.
    /// Otherwise, provide explicit model names.
    /// </summary>
    public List<string> TargetModels { get; set; } = ["auto:top3-smallest"];

    /// <summary>
    /// Temperature values to sweep.
    /// </summary>
    public List<double> TemperatureValues { get; set; } = [0.1, 0.3, 0.5, 0.7, 1.0];

    /// <summary>
    /// Top-K values to sweep.
    /// </summary>
    public List<int> TopKValues { get; set; } = [10, 20, 40, 80];

    /// <summary>
    /// Top-P values to sweep.
    /// </summary>
    public List<double> TopPValues { get; set; } = [0.5, 0.7, 0.9, 0.95];

    /// <summary>
    /// Repeat penalty values to sweep.
    /// </summary>
    public List<double> RepeatPenaltyValues { get; set; } = [1.0, 1.1, 1.2];

    /// <summary>
    /// Maximum number of parameter combinations to test per model.
    /// When the full grid exceeds this, combinations are sampled.
    /// </summary>
    public int MaxCombinations { get; set; } = 20;

    /// <summary>
    /// Generates the parameter combinations to test, applying <see cref="MaxCombinations"/> limit.
    /// Uses systematic sampling when the full grid is too large.
    /// </summary>
    public IReadOnlyList<ModelParameterProfile> GenerateCombinations()
    {
        var fullGrid = new List<ModelParameterProfile>();
        var index = 0;

        foreach (var temp in TemperatureValues)
        foreach (var topK in TopKValues)
        foreach (var topP in TopPValues)
        foreach (var repeat in RepeatPenaltyValues)
        {
            fullGrid.Add(new ModelParameterProfile
            {
                Name = $"sweep-{index++}",
                Temperature = temp,
                TopK = topK,
                TopP = topP,
                RepeatPenalty = repeat
            });
        }

        if (fullGrid.Count <= MaxCombinations)
            return fullGrid;

        // Systematic sampling: take evenly spaced entries from the grid
        var step = (double)fullGrid.Count / MaxCombinations;
        return Enumerable.Range(0, MaxCombinations)
            .Select(i => fullGrid[(int)(i * step)])
            .Select((p, i) => p with { Name = $"sweep-{i}" })
            .ToList();
    }
}
