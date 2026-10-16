namespace lucia.Data.Telemetry;

/// <summary>
/// Measures the health of every database a data provider manages.
/// </summary>
public interface IDataStoreHealthProbe
{
    /// <summary>OpenTelemetry <c>db.system.name</c> value, for example <c>sqlite</c> or <c>postgresql</c>.</summary>
    string System { get; }

    /// <summary>Probes each database. A database that cannot be reached is reported as down, not thrown.</summary>
    Task<IReadOnlyList<DataStoreHealthSample>> ProbeAsync(CancellationToken cancellationToken);
}
