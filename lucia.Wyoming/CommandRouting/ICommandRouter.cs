namespace lucia.Wyoming.CommandRouting;

public interface ICommandRouter
{
    Task<CommandRouteResult> RouteAsync(string transcript, CancellationToken ct);
}
