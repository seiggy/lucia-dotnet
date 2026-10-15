namespace lucia.Tests.Data.Providers;

/// <summary>
/// Shares one PostgreSQL container across every Postgres store test class
/// (a container per class would take minutes).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresStoreCollection : ICollectionFixture<PostgresMigrationFixture>
{
    public const string Name = "PostgresStores";
}
