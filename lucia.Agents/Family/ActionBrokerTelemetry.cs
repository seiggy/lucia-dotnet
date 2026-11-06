using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lucia.Agents.Family;

public static class ActionBrokerTelemetry
{
    public const string SourceName = "lucia.ActionBroker";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        "lucia.action_broker.decisions",
        description: "Capability invocations evaluated for family members, by outcome.");
}
