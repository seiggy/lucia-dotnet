using lucia.HomeAssistant.Configuration;
using Microsoft.Extensions.Options;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Mutable <see cref="IOptionsMonitor{T}"/> backed by a single settable value.
/// Used to simulate live token rotation without a full DI container in tests.
/// </summary>
internal sealed class MutableOptionsMonitor : IOptionsMonitor<HomeAssistantOptions>
{
    public required string AccessToken { get; set; }

    public HomeAssistantOptions CurrentValue => new() { AccessToken = AccessToken };

    public HomeAssistantOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<HomeAssistantOptions, string?> listener)
        => NullDisposable.Instance;
}
