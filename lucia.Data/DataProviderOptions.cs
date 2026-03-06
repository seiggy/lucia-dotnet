namespace lucia.Data;

/// <summary>
/// Configuration options for the Lucia data provider.
/// Read from the LUCIA_DB_PROVIDER environment variable.
/// </summary>
public sealed class DataProviderOptions
{
    public const string SectionName = "DataProvider";
    public const string EnvironmentVariable = "LUCIA_DB_PROVIDER";

    /// <summary>
    /// The database provider to use. Defaults to <see cref="DataProviderType.MongoDB"/>.
    /// </summary>
    public DataProviderType Provider { get; set; } = DataProviderType.MongoDB;

    /// <summary>
    /// SQLite connection string. Defaults to a local file.
    /// </summary>
    public string SqliteConnectionString { get; set; } = "Data Source=lucia.db";

    /// <summary>
    /// PostgreSQL connection string for the config database.
    /// </summary>
    public string? PostgreSqlConnectionString { get; set; }

    /// <summary>
    /// Parses the LUCIA_DB_PROVIDER environment variable into a <see cref="DataProviderType"/>.
    /// </summary>
    public static DataProviderType ParseFromEnvironment()
    {
        var envValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(envValue))
            return DataProviderType.MongoDB;

        return envValue.Trim().ToLowerInvariant() switch
        {
            "mongodb" or "mongo" => DataProviderType.MongoDB,
            "sqlite" => DataProviderType.Sqlite,
            "postgresql" or "postgres" or "pg" => DataProviderType.PostgreSql,
            _ => throw new InvalidOperationException(
                $"Unknown database provider '{envValue}'. Supported values: mongodb, sqlite, postgresql")
        };
    }
}
