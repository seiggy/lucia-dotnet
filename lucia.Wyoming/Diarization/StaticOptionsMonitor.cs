using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable OnChange(Action<T, string?> listener) => new CancellationTokenSource();
}
