namespace lucia.AgentHost.Auth;

/// <summary>
/// Implemented by services that require asynchronous initialization before they are ready to
/// handle requests. Call <see cref="InitializeAsync"/> exactly once at application startup,
/// before the host begins serving traffic.
/// </summary>
public interface IAsyncInitializable
{
    /// <summary>
    /// Performs one-time asynchronous initialization. Implementations must be idempotent —
    /// a second call after successful initialization should return immediately.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
