namespace lucia.Tests.Data.Providers;

/// <summary>
/// A store under test plus the cleanup for the database it lives in.
/// </summary>
public sealed class StoreScope<T>(T store, Func<ValueTask> cleanup) : IAsyncDisposable
{
    public T Store { get; } = store;

    public ValueTask DisposeAsync() => cleanup();
}
