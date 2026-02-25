namespace lucia.Agents.Training.Models;

/// <summary>
/// Summary statistics for the trace dashboard.
/// </summary>
public sealed class TraceStats
{
    public int TotalTraces { get; set; }
    public int UnlabeledCount { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int ErroredCount { get; set; }
    public Dictionary<string, int> ByAgent { get; set; } = [];
    public Dictionary<string, int> ErrorsByAgent { get; set; } = [];
}
