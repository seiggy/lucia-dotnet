using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Extensions;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public class AgentDefinitionRoutingIdentityMigrationTests
{
    [Fact]
    public async Task MigrateRoutingIdentity_PinsDriftedNameToId_WithoutTouchingDisplayName()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        repository.Definitions["legacy-id"] = CreateDefinition(id: "legacy-id", name: "Legacy Display Name");

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        var definition = Assert.Single(repository.Definitions.Values);
        Assert.Equal("legacy-id", definition.Id);
        Assert.Equal("legacy-id", definition.Name);
        Assert.Equal("Legacy Label", definition.DisplayName);
    }

    [Fact]
    public async Task MigrateRoutingIdentity_RenamesNonKebabIdToSlug_PreservingContent()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var original = CreateDefinition(id: "My Garage Agent", name: "my-garage-agent");
        repository.Definitions["My Garage Agent"] = original;

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        var definition = Assert.Single(repository.Definitions.Values);
        Assert.Equal("my-garage-agent", definition.Id);
        Assert.Equal("my-garage-agent", definition.Name);
        Assert.False(repository.Definitions.ContainsKey("My Garage Agent"));
        Assert.Same(original, definition);
        Assert.Equal("Legacy Label", definition.DisplayName);
        Assert.Equal("Description", definition.Description);
        Assert.Equal("Instructions", definition.Instructions);
        Assert.Single(definition.Tools);
    }

    [Fact]
    public async Task MigrateRoutingIdentity_RenamesNonAsciiIdToAsciiSlug()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        repository.Definitions["Café Agent"] = CreateDefinition(id: "Café Agent", name: "legacy-name");

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        var definition = Assert.Single(repository.Definitions.Values);
        Assert.Equal("caf-agent", definition.Id);
        Assert.Equal("caf-agent", definition.Name);
    }

    [Fact]
    public async Task MigrateRoutingIdentity_SkipsRename_WhenTargetIdAlreadyInUse()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var existing = CreateDefinition(id: "my-garage-agent", name: "my-garage-agent");
        var legacy = CreateDefinition(id: "My Garage Agent", name: "legacy-name");
        repository.Definitions["my-garage-agent"] = existing;
        repository.Definitions["My Garage Agent"] = legacy;

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        // The legacy id is left untouched rather than overwriting the existing definition.
        Assert.Equal(2, repository.Definitions.Count);
        Assert.True(repository.Definitions.ContainsKey("My Garage Agent"));
        Assert.Same(existing, repository.Definitions["my-garage-agent"]);
        Assert.Same(legacy, repository.Definitions["My Garage Agent"]);
    }

    [Fact]
    public async Task MigrateRoutingIdentity_SkipsSecondOfTwoLegacyIds_ThatSlugifyToTheSameTarget()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        repository.Definitions["My Garage Agent"] = CreateDefinition(id: "My Garage Agent", name: "first-name");
        repository.Definitions["My_Garage_Agent"] = CreateDefinition(id: "My_Garage_Agent", name: "second-name");

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        Assert.Equal(2, repository.Definitions.Count);
        Assert.True(repository.Definitions.ContainsKey("my-garage-agent"));
        // Exactly one of the two colliding legacy ids survives unrenamed.
        var survivingLegacy = new[] { "My Garage Agent", "My_Garage_Agent" }
            .Where(repository.Definitions.ContainsKey)
            .ToArray();
        Assert.Single(survivingLegacy);
    }

    [Fact]
    public async Task MigrateRoutingIdentity_LeavesAlreadyCleanDefinitionsUntouched()
    {
        var repository = new InMemoryAgentDefinitionRepository
        {
            Definitions =
            {
                ["clean-agent"] = new AgentDefinition
                {
                    Id = "clean-agent",
                    Name = "clean-agent",
                    DisplayName = "Clean Label",
                    Description = "Description",
                    Instructions = "Instructions",
                    Tools = [],
                    Enabled = true,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
                }
            }
        };

        await repository.MigrateRoutingIdentityAsync(NullLogger.Instance);

        Assert.Equal(0, repository.UpsertCount);
        var definition = Assert.Single(repository.Definitions.Values);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), definition.UpdatedAt);
    }

    private static AgentDefinition CreateDefinition(string id, string name) => new()
    {
        Id = id,
        Name = name,
        DisplayName = "Legacy Label",
        Description = "Description",
        Instructions = "Instructions",
        Tools = [new lucia.Agents.Configuration.AgentToolReference { ServerId = "server-a", ToolName = "tool-a" }],
        Enabled = true,
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class InMemoryAgentDefinitionRepository : IAgentDefinitionRepository
    {
        public Dictionary<string, AgentDefinition> Definitions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public int UpsertCount { get; private set; }

        public Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<McpToolServerDefinition>());

        public Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default)
            => Task.FromResult<McpToolServerDefinition?>(null);

        public Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteToolServerAsync(string id, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default)
            => Task.FromResult(Definitions.Values.ToList());

        public Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default)
            => Task.FromResult(Definitions.Values.Where(d => d.Enabled).ToList());

        public Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default)
            => Task.FromResult(Definitions.GetValueOrDefault(id));

        public Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            Definitions[definition.Id] = definition;
            UpsertCount++;
            return Task.CompletedTask;
        }

        public Task<bool> InsertAgentDefinitionIfAbsentAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            if (Definitions.ContainsKey(definition.Id))
                return Task.FromResult(false);

            Definitions[definition.Id] = definition;
            return Task.FromResult(true);
        }

        public Task<bool> RenameAgentDefinitionIfTargetFreeAsync(string oldId, string newId, CancellationToken ct = default)
        {
            if (!Definitions.TryGetValue(oldId, out var definition) || Definitions.ContainsKey(newId))
                return Task.FromResult(false);

            definition.Id = newId;
            definition.Name = newId;
            Definitions[newId] = definition;
            Definitions.Remove(oldId);
            return Task.FromResult(true);
        }

        public Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default)
        {
            Definitions.Remove(id);
            return Task.CompletedTask;
        }
    }
}
