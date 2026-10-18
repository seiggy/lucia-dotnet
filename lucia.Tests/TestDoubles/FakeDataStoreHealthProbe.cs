using lucia.Data.Telemetry;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Returns fixed <see cref="DataStoreHealthSample"/> values and counts how often it was probed.
/// </summary>
internal sealed class FakeDataStoreHealthProbe(string system, params DataStoreHealthSample[] samples)
    : IDataStoreHealthProbe
{
    public int ProbeCount { get; private set; }

    public string System => system;

    public Task<IReadOnlyList<DataStoreHealthSample>> ProbeAsync(CancellationToken cancellationToken)
    {
        ProbeCount++;
        return Task.FromResult<IReadOnlyList<DataStoreHealthSample>>(samples);
    }
}
