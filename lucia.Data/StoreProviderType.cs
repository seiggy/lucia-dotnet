namespace lucia.Data;

/// <summary>
/// Available persistent store provider types.
/// </summary>
public enum StoreProviderType
{
    /// <summary>SQLite embedded database (default, no external dependencies).</summary>
    SQLite,

    /// <summary>PostgreSQL relational database (external PostgreSQL server).</summary>
    PostgreSQL
}