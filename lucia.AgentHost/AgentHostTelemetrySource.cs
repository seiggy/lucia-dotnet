using System.Diagnostics;
using System.Diagnostics.Metrics;
using lucia.Agents;

namespace lucia.AgentHost;

public class AgentHostTelemetrySource : IDisposable
{
    private const string ActivitySourceName = "lucia.AgentHost";
    
    public AgentHostTelemetrySource()
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