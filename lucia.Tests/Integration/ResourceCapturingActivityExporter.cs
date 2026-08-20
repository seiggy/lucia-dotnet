using System.Diagnostics;
using OpenTelemetry;

namespace lucia.Tests.Integration;

internal sealed class ResourceCapturingActivityExporter : BaseExporter<Activity>
{
    public IReadOnlyDictionary<string, object> ResourceAttributes { get; private set; } =
        new Dictionary<string, object>();

    public override ExportResult Export(in Batch<Activity> batch)
    {
        ResourceAttributes = ParentProvider?.GetResource().Attributes.ToDictionary() ??
            new Dictionary<string, object>();
        return ExportResult.Success;
    }
}