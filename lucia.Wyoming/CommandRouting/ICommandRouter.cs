namespace lucia.Wyoming.CommandRouting;

public interface ICommandRouter
{
    bool FallbackToLlmEnabled { get; }

    Task<CommandRouteResult> RouteAsync(string transcript, CancellationToken ct);
}
