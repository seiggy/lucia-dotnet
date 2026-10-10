using System.Security.Cryptography;
using System.Text;
using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteApiKeyServiceContractTests : ApiKeyServiceContract
{
    protected override Task<StoreScope<IApiKeyService>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IApiKeyService>(
            env => new SqliteApiKeyService(env.Config, NullLogger<SqliteApiKeyService>.Instance));

    [Fact]
    public async Task CreateKeyAsync_StoresSha256HashNotPlaintext()
    {
        using var environment = await SqliteStoreEnvironment.CreateAsync();
        var service = new SqliteApiKeyService(environment.Config, NullLogger<SqliteApiKeyService>.Instance);

        var created = await service.CreateKeyAsync("hashed");

        using var connection = environment.Config.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key_hash FROM api_keys WHERE id = @id;";
        command.Parameters.AddWithValue("@id", created.Id);
        var stored = (string?)await command.ExecuteScalarAsync();

        Assert.NotEqual(created.Key, stored);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Key))), stored);
    }
}