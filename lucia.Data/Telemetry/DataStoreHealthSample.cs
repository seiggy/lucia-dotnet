namespace lucia.Data.Telemetry;

/// <summary>
/// One database's health at the moment it was probed. Values a provider cannot measure stay null.
/// </summary>
/// <param name="Database">Logical database name, for example <c>luciaconfig</c>.</param>
/// <param name="Up">Whether the database answered a trivial query.</param>
/// <param name="ProbeSeconds">How long the probe took, including opening the connection.</param>
/// <param name="SizeBytes">On-disk size of the database.</param>
/// <param name="WalBytes">Size of the SQLite write-ahead log.</param>
/// <param name="Connections">Backends connected to the PostgreSQL database.</param>
public sealed record DataStoreHealthSample(
    string Database,
    bool Up,
    double ProbeSeconds,
    long? SizeBytes = null,
    long? WalBytes = null,
    long? Connections = null);
