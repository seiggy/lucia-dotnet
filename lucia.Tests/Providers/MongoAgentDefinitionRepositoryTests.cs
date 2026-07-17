using FakeItEasy;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Providers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace lucia.Tests.Providers;

public sealed class MongoAgentDefinitionRepositoryTests
{
    [SkippableFact, Trait("Category", "Integration")]
    public async Task UpsertAgentDefinitionAsync_UsesStrictPersistedMarkersForRacingWrites()
    {
        var connectionString = Environment.GetEnvironmentVariable("LUCIA_TEST_MONGODB_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(connectionString),
            "LUCIA_TEST_MONGODB_CONNECTION_STRING is required for the MongoDB integration test.");

        var serverClient = new MongoClient(connectionString);
        var databaseName = $"lucia_agent_definition_tests_{Guid.NewGuid():N}";
        var database = serverClient.GetDatabase(databaseName);
        var client = A.Fake<IMongoClient>();
        A.CallTo(() => client.GetDatabase("luciaconfig", A<MongoDatabaseSettings?>._))
            .Returns(database);
        var collection = database.GetCollection<AgentDefinition>(AgentDefinition.CollectionName);
        var repository = new MongoAgentDefinitionRepository(client);

        try
        {
            var created = CreateDefinition("created");
            await repository.UpsertAgentDefinitionAsync(created);
            var persistedCreate = await collection.Find(definition => definition.Id == created.Id).SingleAsync();

            Assert.Equal(0, created.UpdatedAt.Ticks % TimeSpan.TicksPerMillisecond);
            Assert.Equal(created.UpdatedAt, persistedCreate.UpdatedAt);

            var writes = Enumerable.Range(0, 16)
                .Select(index => CreateDefinition($"write-{index}"))
                .ToArray();
            await Task.WhenAll(writes.Select(definition => repository.UpsertAgentDefinitionAsync(definition)));

            Assert.All(writes, definition => Assert.True(definition.UpdatedAt > created.UpdatedAt));
            Assert.Equal(writes.Length, writes.Select(definition => definition.UpdatedAt).Distinct().Count());
            Assert.All(writes, definition =>
                Assert.Equal(0, definition.UpdatedAt.Ticks % TimeSpan.TicksPerMillisecond));

            var persistedWinner = await collection.Find(definition => definition.Id == created.Id).SingleAsync();
            var latestWrite = writes.MaxBy(definition => definition.UpdatedAt)!;
            Assert.Equal(latestWrite.UpdatedAt, persistedWinner.UpdatedAt);
            Assert.Equal(latestWrite.ModelConnectionName, persistedWinner.ModelConnectionName);

            await collection.DeleteOneAsync(definition => definition.Id == created.Id);
            var legacyDocument = CreateDefinition("legacy").ToBsonDocument();
            legacyDocument.Remove(nameof(AgentDefinition.UpdatedAt));
            await database.GetCollection<BsonDocument>(AgentDefinition.CollectionName).InsertOneAsync(legacyDocument);

            var migrated = CreateDefinition("migrated");
            await repository.UpsertAgentDefinitionAsync(migrated);
            var persistedMigration = await collection.Find(definition => definition.Id == migrated.Id).SingleAsync();

            Assert.Equal(migrated.UpdatedAt, persistedMigration.UpdatedAt);
            Assert.Equal("migrated", persistedMigration.ModelConnectionName);
        }
        finally
        {
            await serverClient.DropDatabaseAsync(databaseName);
        }
    }

    private static AgentDefinition CreateDefinition(string modelConnectionName)
    {
        return new AgentDefinition
        {
            Id = "general-assistant",
            Name = "general-assistant",
            DisplayName = "General Assistant",
            Description = "General assistant",
            Instructions = "Use tools",
            ModelConnectionName = modelConnectionName,
        };
    }
}
