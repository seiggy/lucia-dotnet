using Microsoft.Extensions.Options;

namespace lucia.Tests.Helpers;

/// <summary>
/// Lightweight <see cref="IOptionsMonitor{T}"/> for unit tests that need hot-reload semantics
/// without standing up the full options infrastructure.
/// </summary>
internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
