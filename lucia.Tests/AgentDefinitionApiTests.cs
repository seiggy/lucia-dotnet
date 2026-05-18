using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Tests;

public sealed class AgentDefinitionApiTests
{
    [Fact]
    public async Task ReplaceDefinitionAsync_ReplacesClientFieldsAndPreservesSystemManagedFields()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var existing = CreateDefinition(
            id: "existing-id",
            name: "existing-name",
            displayName: "Existing Display",
            description: "Existing description",
            instructions: "Existing instructions",
            enabled: true,
            modelConnectionName: "existing-model",
            embeddingProviderName: "existing-embedding",
            isBuiltIn: true,
            isRemote: true,
            isOrchestrator: true,
            createdAt: new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            updatedAt: new DateTime(2024, 1, 3, 3, 4, 5, DateTimeKind.Utc),
            tools:
            [
                new AgentToolReference { ServerId = "server-a", ToolName = "tool-a" },
            ]);
        var replacementRequest = CreateDefinition(
            id: "client-id",
            name: "replacement-name",
            displayName: "Replacement Display",
            description: "Replacement description",
            instructions: "Replacement instructions",
            enabled: false,
            modelConnectionName: "replacement-model",
            embeddingProviderName: "replacement-embedding",
            isBuiltIn: false,
            isRemote: false,
            isOrchestrator: false,
            createdAt: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            updatedAt: new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            tools:
            [
                new AgentToolReference { ServerId = "server-b", ToolName = "tool-b" },
            ]);
        AgentDefinition? persisted = null;

        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(existing);
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        await InvokeHandlerAsync("ReplaceDefinitionAsync", "route-id", replacementRequest, repository);

        Assert.NotNull(persisted);
        Assert.Equal("route-id", persisted.Id);
        Assert.Equal("replacement-name", persisted.Name);
        Assert.Equal("Replacement Display", persisted.DisplayName);
        Assert.Equal("Replacement description", persisted.Description);
        Assert.Equal("Replacement instructions", persisted.Instructions);
        Assert.False(persisted.Enabled);
        Assert.Equal("replacement-model", persisted.ModelConnectionName);
        Assert.Equal("replacement-embedding", persisted.EmbeddingProviderName);
        Assert.Single(persisted.Tools);
        Assert.Equal("server-b", persisted.Tools[0].ServerId);
        Assert.Equal("tool-b", persisted.Tools[0].ToolName);
        Assert.True(persisted.IsBuiltIn);
        Assert.True(persisted.IsRemote);
        Assert.True(persisted.IsOrchestrator);
        Assert.Equal(existing.CreatedAt, persisted.CreatedAt);
        Assert.True(persisted.UpdatedAt >= existing.UpdatedAt);
        Assert.True(persisted.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task PatchDefinitionAsync_OnlyOverwritesProvidedFields()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var existing = CreateDefinition(
            id: "existing-id",
            name: "existing-name",
            displayName: "Existing Display",
            description: "Existing description",
            instructions: "Existing instructions",
            enabled: true,
            modelConnectionName: "existing-model",
            embeddingProviderName: "existing-embedding",
            isBuiltIn: true,
            isRemote: false,
            isOrchestrator: false,
            createdAt: new DateTime(2024, 2, 2, 3, 4, 5, DateTimeKind.Utc),
            updatedAt: new DateTime(2024, 2, 3, 3, 4, 5, DateTimeKind.Utc),
            tools:
            [
                new AgentToolReference { ServerId = "server-a", ToolName = "tool-a" },
            ]);
        var patchRequest = new AgentDefinition
        {
            Enabled = false,
            DisplayName = "Patched Display",
            Tools = null!,
        };
        AgentDefinition? persisted = null;

        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(existing);
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        await InvokeHandlerAsync("PatchDefinitionAsync", "route-id", patchRequest, repository);

        Assert.NotNull(persisted);
        Assert.Same(existing, persisted);
        Assert.Equal("existing-name", persisted.Name);
        Assert.Equal("Patched Display", persisted.DisplayName);
        Assert.Equal("Existing description", persisted.Description);
        Assert.Equal("Existing instructions", persisted.Instructions);
        Assert.Equal("existing-model", persisted.ModelConnectionName);
        Assert.Equal("existing-embedding", persisted.EmbeddingProviderName);
        Assert.False(persisted.Enabled);
        Assert.Single(persisted.Tools);
        Assert.Equal("server-a", persisted.Tools[0].ServerId);
        Assert.True(persisted.IsBuiltIn);
        Assert.Equal(existing.CreatedAt, persisted.CreatedAt);
        Assert.True(persisted.UpdatedAt >= new DateTime(2024, 2, 3, 3, 4, 5, DateTimeKind.Utc));
    }

    private static async Task InvokeHandlerAsync(string methodName, string id, AgentDefinition definition, IAgentDefinitionRepository repository)
    {
        var method = typeof(AgentDefinitionApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, definition, repository]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
    }

    private static AgentDefinition CreateDefinition(
        string id,
        string name,
        string displayName,
        string description,
        string instructions,
        bool enabled,
        string? modelConnectionName,
        string? embeddingProviderName,
        bool isBuiltIn,
        bool isRemote,
        bool isOrchestrator,
        DateTime createdAt,
        DateTime updatedAt,
        List<AgentToolReference> tools)
    {
        return new AgentDefinition
        {
            Id = id,
            Name = name,
            DisplayName = displayName,
            Description = description,
            Instructions = instructions,
            Enabled = enabled,
            ModelConnectionName = modelConnectionName,
            EmbeddingProviderName = embeddingProviderName,
            IsBuiltIn = isBuiltIn,
            IsRemote = isRemote,
            IsOrchestrator = isOrchestrator,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Tools = tools,
        };
    }
}
