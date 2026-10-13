using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lucia.Agents.Skills;

/// <summary>
/// The request counter, failure counter and duration histogram of one group of skill tools. Create the
/// group once per skill with <see cref="Create"/>, then bind each tool with <see cref="ForTool"/> so every
/// measurement carries a <c>tool</c> tag. <see cref="ToolRunner"/> records through it.
/// </summary>
public sealed class ToolMetrics
{
    private readonly Counter<long> _requests;
    private readonly Counter<long> _failures;
    private readonly Histogram<double> _duration;
    private readonly string? _tool;

    private ToolMetrics(Counter<long> requests, Counter<long> failures, Histogram<double> duration, string? tool)
    {
        _requests = requests;
        _failures = failures;
        _duration = duration;
        _tool = tool;
    }

    /// <param name="meter">The skill's meter.</param>
    /// <param name="prefix">Instrument name prefix, such as <c>climate.lookup</c>.</param>
    /// <param name="noun">Used in the instrument descriptions, such as <c>climate lookup</c>.</param>
    public static ToolMetrics Create(Meter meter, string prefix, string noun) => new(
        meter.CreateCounter<long>($"{prefix}.requests", "{count}", $"Number of {noun} requests."),
        meter.CreateCounter<long>($"{prefix}.failures", "{count}", $"Number of failed {noun} requests."),
        meter.CreateHistogram<double>($"{prefix}.duration", "ms", $"Duration of {noun} requests."),
        tool: null);

    /// <summary>Returns the same instruments with every measurement tagged with the tool name.</summary>
    public ToolMetrics ForTool(string tool) => new(_requests, _failures, _duration, tool);

    public void Started() => _requests.Add(1, Tags());

    public void Failed() => _failures.Add(1, Tags());

    public void RecordDuration(double elapsedMs) => _duration.Record(elapsedMs, Tags());

    private TagList Tags()
    {
        var tags = new TagList();
        if (_tool is not null)
        {
            tags.Add("tool", _tool);
        }

        return tags;
    }
}
