using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

internal sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
{
    public T CurrentValue => currentValue;

    public T Get(string? name) => currentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
