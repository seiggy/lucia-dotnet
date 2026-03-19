namespace lucia.Data;

/// <summary>
/// Available persistent store provider types.
/// </summary>
public enum StoreProviderType
{
    /// <summary>MongoDB document store (default, requires MongoDB server).</summary>
    MongoDB,

    /// <summary>SQLite embedded database (no external dependencies).</summary>
    SQLite
}
