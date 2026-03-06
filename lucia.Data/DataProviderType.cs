namespace lucia.Data;

/// <summary>
/// Supported database provider types for the Lucia data layer.
/// </summary>
public enum DataProviderType
{
    /// <summary>MongoDB (default) — requires a running MongoDB instance.</summary>
    MongoDB,

    /// <summary>SQLite — file-based, no server required.</summary>
    Sqlite,

    /// <summary>PostgreSQL — requires a running PostgreSQL instance.</summary>
    PostgreSql
}
