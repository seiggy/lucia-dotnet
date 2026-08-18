using System.Globalization;

using lucia.Agents.Configuration.UserConfiguration;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

public sealed class SqliteAgentDefinitionRepositoryTests
{
    [Fact]
    public async Task UpsertAgentDefinitionAsync_ContendedWritesAdvanceMillisecondMarkerPastStoredRow()
    {
        using var database = new AgentDefinitionSqliteTestDatabase();
        var connectionFactory = database.ConnectionFactory;
        var repository = database.Repository;
        var initial = CreateDefinition("initial");
        await repository.UpsertAgentDefinitionAsync(initial);

        var futureMarker = new DateTime(
            DateTime.UtcNow.AddMinutes(1).Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
        using (var connection = connectionFactory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE agent_definitions
                SET data = json_set(data, '$.updatedAt', @updatedAt)
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@updatedAt", futureMarker.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@id", initial.Id);
            await command.ExecuteNonQueryAsync();
        }

        var storedInitial = await repository.GetAgentDefinitionAsync(initial.Id);
        Assert.NotNull(storedInitial);
        Assert.Equal(futureMarker, storedInitial.UpdatedAt);

        var writes = Enumerable.Range(0, 16)
            .Select(index => CreateDefinition($"write-{index}"))
            .ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeTasks = writes.Select(async definition =>
        {
            await start.Task;
            await repository.UpsertAgentDefinitionAsync(definition);
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(writeTasks);

        Assert.All(writes, definition => Assert.True(definition.UpdatedAt > storedInitial.UpdatedAt));
        Assert.Equal(writes.Length, writes.Select(definition => definition.UpdatedAt).Distinct().Count());
        Assert.All(writes, definition =>
        {
            Assert.Equal(DateTimeKind.Utc, definition.UpdatedAt.Kind);
            Assert.Equal(0, definition.UpdatedAt.Ticks % TimeSpan.TicksPerMillisecond);
        });

        var persistedWinner = await repository.GetAgentDefinitionAsync(initial.Id);
        var latestWrite = writes.MaxBy(definition => definition.UpdatedAt)!;
        Assert.NotNull(persistedWinner);
        Assert.Equal(latestWrite.UpdatedAt, persistedWinner.UpdatedAt);
        Assert.Equal(latestWrite.ModelConnectionName, persistedWinner.ModelConnectionName);
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
