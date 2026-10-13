using System.Security.Cryptography;
using System.Text;
using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresApiKeyServiceContractTests(PostgresMigrationFixture fixture) : ApiKeyServiceContract
{
    protected override Task<StoreScope<IApiKeyService>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IApiKeyService>(
            fixture, env => new PostgresApiKeyService(env.Config, NullLogger<PostgresApiKeyService>.Instance));

    [Fact]
    public async Task CreateKeyAsync_StoresSha256HashNotPlaintext()
    {
        await using var environment = await PostgresStoreEnvironment.CreateAsync(fixture);
        var service = new PostgresApiKeyService(environment.Config, NullLogger<PostgresApiKeyService>.Instance);

        var created = await service.CreateKeyAsync("hashed");

        await using var connection = await environment.Config.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key_hash FROM api_keys WHERE id = @id;";
        command.Parameters.AddWithValue("id", created.Id);
        var stored = (string?)await command.ExecuteScalarAsync();

        Assert.NotEqual(created.Key, stored);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Key))), stored);
    }
}