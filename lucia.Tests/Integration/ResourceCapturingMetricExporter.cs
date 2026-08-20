using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace lucia.Tests.Integration;

internal sealed class ResourceCapturingMetricExporter : BaseExporter<Metric>
{
    public IReadOnlyDictionary<string, object> ResourceAttributes { get; private set; } =
        new Dictionary<string, object>();

    public IReadOnlyList<string> MetricNames { get; private set; } = [];

    public override ExportResult Export(in Batch<Metric> batch)
    {
        ResourceAttributes = ParentProvider?.GetResource().Attributes.ToDictionary() ??
            new Dictionary<string, object>();
        var metricNames = new List<string>();
        foreach (var metric in batch)
        {
            metricNames.Add(metric.Name);
        }

        MetricNames = metricNames;
        return ExportResult.Success;
    }
}