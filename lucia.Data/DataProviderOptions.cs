namespace lucia.Data;

/// <summary>
/// Configuration for selecting data provider backends.
/// Allows switching between Redis/MongoDB (full) and InMemory/SQLite (lightweight) modes.
/// </summary>
public sealed class DataProviderOptions
{
    public const string SectionName = "DataProvider";

    /// <summary>
    /// Cache provider for session state, device caching, and prompt caching.
    /// Default: Redis. Use InMemory for lightweight/mono-container deployments.
    /// </summary>
    public CacheProviderType Cache { get; set; } = CacheProviderType.Redis;

    /// <summary>
    /// Persistent store for configuration, traces, tasks, and repositories.
    /// Default: MongoDB. Use SQLite for lightweight/mono-container deployments.
    /// </summary>
    public StoreProviderType Store { get; set; } = StoreProviderType.MongoDB;

    /// <summary>
    /// SQLite database file path. Only used when Store is SQLite.
    /// </summary>
    public string SqlitePath { get; set; } = "./data/lucia.db";
}
