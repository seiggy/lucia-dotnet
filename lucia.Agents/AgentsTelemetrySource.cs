using System.Diagnostics;
using System.Reflection;

namespace lucia.Agents;

public sealed class AgentsTelemetry : IDisposable
{
    public static ActivitySource ActivitySource { get; } = new ActivitySource("lucia.Agents", Assembly.GetEntryAssembly()?.GetName().Version?.ToString());
}