namespace lucia.Tests.Integration;

// Other test collections must not feed the process-wide listeners attached to the stalled exporter.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryTestCollection
{
    public const string Name = "Process-wide telemetry";
}
