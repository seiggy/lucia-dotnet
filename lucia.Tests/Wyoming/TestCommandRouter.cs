using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

internal sealed class TestCommandRouter(CommandRouteResult routeResult) : ICommandRouter
{
    public int RouteCallCount { get; private set; }

    public string? LastTranscript { get; private set; }

    public Task<CommandRouteResult> RouteAsync(string transcript, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(transcript);

        RouteCallCount++;
        LastTranscript = transcript;
        return Task.FromResult(routeResult);
    }
}
