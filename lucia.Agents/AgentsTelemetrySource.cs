using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace lucia.Agents;

public sealed class AgentsTelemetrySource : IDisposable
{
    private const string ActivitySourceName = "lucia.Agents";
    
    public AgentsTelemetrySource()
    {
        var version = typeof(AgentsTelemetrySource).Assembly.GetName().Version?.ToString();
        ActivitySource = new ActivitySource(ActivitySourceName, version);
        Meter = new Meter(ActivitySourceName, version);
    }
    
    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}