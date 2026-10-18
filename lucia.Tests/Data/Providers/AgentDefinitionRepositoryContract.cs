using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IAgentDefinitionRepository"/> provider must share.
/// </summary>
public abstract class AgentDefinitionRepositoryContract
{
    protected abstract Task<StoreScope<IAgentDefinitionRepository>> CreateAsync();

    [Fact]
    public async Task GetAllToolServersAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllToolServersAsync());
    }

    [Fact]
    public async Task GetToolServerAsync_UnknownId_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetToolServerAsync("missing"));
    }

    [Fact]
    public async Task UpsertToolServerAsync_NewServer_RoundTripsEveryMeaningfulField()
    {
        await using var scope = await CreateAsync();
        var createdAt = new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc);
        var server = new McpToolServerDefinition
        {
            Id = "github-mcp",
            Name = "GitHub MCP",
            Description = "GitHub access",
            TransportType = "stdio",
            Command = "npx",
            Arguments = ["-y", "@modelcontextprotocol/server-github"],
            WorkingDirectory = "/workspace",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["GITHUB_TOKEN"] = "secret-token",
            },
            Url = "https://example.com/mcp",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer example",
            },
            Enabled = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(1),
        };

        await scope.Store.UpsertToolServerAsync(server);

        var saved = Assert.Single(await scope.Store.GetAllToolServersAsync());
        Assert.Equal(server.Id, saved.Id);
        Assert.Equal(server.Name, saved.Name);
        Assert.Equal(server.Description, saved.Description);
        Assert.Equal(server.TransportType, saved.TransportType);
        Assert.Equal(server.Command, saved.Command);
        Assert.Equal(server.Arguments, saved.Arguments);
        Assert.Equal(server.WorkingDirectory, saved.WorkingDirectory);
        Assert.Equal(server.EnvironmentVariables, saved.EnvironmentVariables);
        Assert.Equal(server.Url, saved.Url);
        Assert.Equal(server.Headers, saved.Headers);
        Assert.Equal(server.Enabled, saved.Enabled);
        Assert.Equal(server.CreatedAt, saved.CreatedAt);
        Assert.True(saved.UpdatedAt >= server.UpdatedAt);
    }

    [Fact]
    public async Task UpsertToolServerAsync_SameId_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertToolServerAsync(CreateToolServer("server-a", "Server A", true));

        await scope.Store.UpsertToolServerAsync(CreateToolServer("server-a", "Server A Updated", false));

        var servers = await scope.Store.GetAllToolServersAsync();
        var server = Assert.Single(servers);
        Assert.Equal("Server A Updated", server.Name);
        Assert.False(server.Enabled);
    }

    [Fact]
    public async Task DeleteToolServerAsync_RemovesOnlyNamedServer()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertToolServerAsync(CreateToolServer("server-a", "A", true));
        await scope.Store.UpsertToolServerAsync(CreateToolServer("server-b", "B", true));

        await scope.Store.DeleteToolServerAsync("server-a");

        var servers = await scope.Store.GetAllToolServersAsync();
        var remaining = Assert.Single(servers);
        Assert.Equal("server-b", remaining.Id);
    }

    [Fact]
    public async Task GetAllAgentDefinitionsAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllAgentDefinitionsAsync());
    }

    [Fact]
    public async Task GetEnabledAgentDefinitionsAsync_FiltersOnlyEnabledDefinitions()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("alpha", "Alpha", true));
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("beta", "Beta", false));
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("gamma", "Gamma", true));

        var definitions = await scope.Store.GetEnabledAgentDefinitionsAsync();

        Assert.Equal(["alpha", "gamma"], definitions.Select(d => d.Id).ToArray());
    }

    [Fact]
    public async Task GetAgentDefinitionAsync_UnknownId_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetAgentDefinitionAsync("missing"));
    }

    [Fact]
    public async Task UpsertAgentDefinitionAsync_NewDefinition_RoundTripsEveryMeaningfulField()
    {
        await using var scope = await CreateAsync();
        var createdAt = new DateTime(2024, 06, 07, 08, 09, 10, DateTimeKind.Utc);
        var definition = new AgentDefinition
        {
            Id = "research-agent",
            Name = "research-agent",
            DisplayName = "Research Agent",
            Description = "Researches the web",
            Instructions = "Use tools carefully.",
            Tools =
            [
                new AgentToolReference { ServerId = "github-mcp", ToolName = "search" },
                new AgentToolReference { ServerId = "docs-mcp", ToolName = "read_file" },
            ],
            ModelConnectionName = "gpt-4o",
            EmbeddingProviderName = "text-embedding-3",
            Enabled = true,
            IsBuiltIn = true,
            IsRemote = false,
            IsOrchestrator = false,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(2),
        };

        await scope.Store.UpsertAgentDefinitionAsync(definition);

        var saved = Assert.Single(await scope.Store.GetAllAgentDefinitionsAsync());
        Assert.Equal(definition.Id, saved.Id);
        Assert.Equal(definition.Name, saved.Name);
        Assert.Equal(definition.DisplayName, saved.DisplayName);
        Assert.Equal(definition.Description, saved.Description);
        Assert.Equal(definition.Instructions, saved.Instructions);
        Assert.Equal(
            definition.Tools.Select(tool => (tool.ServerId, tool.ToolName)).ToArray(),
            saved.Tools.Select(tool => (tool.ServerId, tool.ToolName)).ToArray());
        Assert.Equal(definition.ModelConnectionName, saved.ModelConnectionName);
        Assert.Equal(definition.EmbeddingProviderName, saved.EmbeddingProviderName);
        Assert.Equal(definition.Enabled, saved.Enabled);
        Assert.Equal(definition.IsBuiltIn, saved.IsBuiltIn);
        Assert.Equal(definition.IsRemote, saved.IsRemote);
        Assert.Equal(definition.IsOrchestrator, saved.IsOrchestrator);
        Assert.Equal(definition.CreatedAt, saved.CreatedAt);
        Assert.True(saved.UpdatedAt >= definition.UpdatedAt);
    }

    [Fact]
    public async Task UpsertAgentDefinitionAsync_SameId_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("alpha", "Alpha", true));

        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("alpha", "Alpha Updated", false));

        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        var definition = Assert.Single(definitions);
        Assert.Equal("Alpha Updated", definition.DisplayName);
        Assert.False(definition.Enabled);
    }

    [Fact]
    public async Task DeleteAgentDefinitionAsync_RemovesOnlyNamedDefinition()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("alpha", "Alpha", true));
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("beta", "Beta", true));

        await scope.Store.DeleteAgentDefinitionAsync("alpha");

        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        var remaining = Assert.Single(definitions);
        Assert.Equal("beta", remaining.Id);
    }

    [Fact]
    public async Task InsertAgentDefinitionIfAbsentAsync_SecondInsert_ReturnsFalseAndKeepsOriginal()
    {
        await using var scope = await CreateAsync();
        var original = CreateAgentDefinition("alpha", "Alpha", true);

        var inserted = await scope.Store.InsertAgentDefinitionIfAbsentAsync(original);
        var duplicate = CreateAgentDefinition("alpha", "Alpha Duplicate", false);

        var rejected = await scope.Store.InsertAgentDefinitionIfAbsentAsync(duplicate);

        Assert.True(inserted);
        Assert.False(rejected);
        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        var definition = Assert.Single(definitions);
        Assert.Equal("Alpha", definition.DisplayName);
    }

    [Fact]
    public async Task InsertAgentDefinitionIfAbsentAsync_ConcurrentInserts_ExactlyOneWins()
    {
        await using var scope = await CreateAsync();

        var results = await Task.WhenAll(
            scope.Store.InsertAgentDefinitionIfAbsentAsync(CreateAgentDefinition("alpha", "Alpha One", true)),
            scope.Store.InsertAgentDefinitionIfAbsentAsync(CreateAgentDefinition("alpha", "Alpha Two", false)));

        Assert.Equal([true, false], results.OrderByDescending(r => r).ToArray());
        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        Assert.Single(definitions);
    }

    [Fact]
    public async Task RenameAgentDefinitionIfTargetFreeAsync_RenamesAndPinsName_PreservingContent()
    {
        await using var scope = await CreateAsync();
        var original = CreateAgentDefinition("Legacy Agent Id", "Legacy Label", true);
        await scope.Store.UpsertAgentDefinitionAsync(original);

        var renamed = await scope.Store.RenameAgentDefinitionIfTargetFreeAsync("Legacy Agent Id", "legacy-agent-id");

        Assert.True(renamed);
        Assert.Null(await scope.Store.GetAgentDefinitionAsync("Legacy Agent Id"));
        var saved = Assert.Single(await scope.Store.GetAllAgentDefinitionsAsync());
        Assert.Equal("legacy-agent-id", saved.Id);
        Assert.Equal("legacy-agent-id", saved.Name);
        Assert.Equal(original.DisplayName, saved.DisplayName);
        Assert.Equal(original.Description, saved.Description);
        Assert.Equal(original.Instructions, saved.Instructions);
        Assert.Equal(
            original.Tools.Select(tool => (tool.ServerId, tool.ToolName)).ToArray(),
            saved.Tools.Select(tool => (tool.ServerId, tool.ToolName)).ToArray());
        Assert.True(saved.UpdatedAt >= original.UpdatedAt);
    }

    [Fact]
    public async Task RenameAgentDefinitionIfTargetFreeAsync_TargetExists_ReturnsFalseAndLeavesBothUntouched()
    {
        await using var scope = await CreateAsync();
        var existing = CreateAgentDefinition("legacy-agent-id", "Existing Label", false);
        var legacy = CreateAgentDefinition("Legacy Agent Id", "Legacy Label", true);
        await scope.Store.UpsertAgentDefinitionAsync(existing);
        await scope.Store.UpsertAgentDefinitionAsync(legacy);

        var renamed = await scope.Store.RenameAgentDefinitionIfTargetFreeAsync("Legacy Agent Id", "legacy-agent-id");

        Assert.False(renamed);
        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        Assert.Equal(2, definitions.Count);
        Assert.Equal("Existing Label", definitions.Single(d => d.Id == "legacy-agent-id").DisplayName);
        Assert.Equal("Legacy Label", definitions.Single(d => d.Id == "Legacy Agent Id").DisplayName);
    }

    [Fact]
    public async Task RenameAgentDefinitionIfTargetFreeAsync_OldIdMissing_ReturnsFalse()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("unrelated", "Unrelated", true));

        var renamed = await scope.Store.RenameAgentDefinitionIfTargetFreeAsync("missing-legacy-id", "new-id");

        Assert.False(renamed);
        var remaining = Assert.Single(await scope.Store.GetAllAgentDefinitionsAsync());
        Assert.Equal("unrelated", remaining.Id);
    }

    [Fact]
    public async Task RenameAgentDefinitionIfTargetFreeAsync_ConcurrentRenamesToSameTarget_ExactlyOneWins()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("Legacy One", "First", true));
        await scope.Store.UpsertAgentDefinitionAsync(CreateAgentDefinition("Legacy Two", "Second", false));

        var results = await Task.WhenAll(
            scope.Store.RenameAgentDefinitionIfTargetFreeAsync("Legacy One", "legacy-target"),
            scope.Store.RenameAgentDefinitionIfTargetFreeAsync("Legacy Two", "legacy-target"));

        Assert.Equal([true, false], results.OrderByDescending(r => r).ToArray());
        var definitions = await scope.Store.GetAllAgentDefinitionsAsync();
        // The winner moved to the target id; the loser keeps its legacy id and content.
        var winner = definitions.Single(d => d.Id == "legacy-target");
        Assert.True(winner.DisplayName is "First" or "Second");
        Assert.Single(definitions, d => d.Id is "Legacy One" or "Legacy Two");
    }

    [Fact]
    public async Task UpsertAgentDefinitionAsync_UpdatesTimestampWhenReplacingExistingRecord()
    {
        await using var scope = await CreateAsync();
        var initial = CreateAgentDefinition("alpha", "Alpha", true);
        await scope.Store.UpsertAgentDefinitionAsync(initial);

        var before = await scope.Store.GetAgentDefinitionAsync("alpha");
        Assert.NotNull(before);

        var updated = CreateAgentDefinition("alpha", "Alpha Updated", false);
        await scope.Store.UpsertAgentDefinitionAsync(updated);

        var persisted = await scope.Store.GetAgentDefinitionAsync("alpha");
        Assert.NotNull(persisted);
        Assert.Equal("Alpha Updated", persisted.DisplayName);
        Assert.True(persisted.UpdatedAt >= before!.UpdatedAt);
    }

    private static McpToolServerDefinition CreateToolServer(string id, string name, bool enabled)
        => new()
        {
            Id = id,
            Name = name,
            Description = $"Description for {name}",
            TransportType = "stdio",
            Command = "node",
            Arguments = ["server.js"],
            Enabled = enabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private static AgentDefinition CreateAgentDefinition(string id, string displayName, bool enabled)
        => new()
        {
            Id = id,
            Name = id,
            DisplayName = displayName,
            Description = $"Description for {displayName}",
            Instructions = "Use tools.",
            Tools = [new AgentToolReference { ServerId = "server-a", ToolName = "tool-a" }],
            ModelConnectionName = "gpt-4o",
            Enabled = enabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
