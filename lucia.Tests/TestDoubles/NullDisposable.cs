namespace lucia.Tests.TestDoubles;

/// <summary>No-op <see cref="IDisposable"/> for use in test stub implementations.</summary>
internal sealed class NullDisposable : IDisposable
{
    internal static readonly NullDisposable Instance = new();

    public void Dispose() { }
}
