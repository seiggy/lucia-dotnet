using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Providers;
using lucia.Agents.Registry;
using lucia.Agents.Services;

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

        await InvokeReplaceHandlerAsync("ReplaceDefinitionAsync", "route-id", replacementRequest, repository, A.Fake<IPromptCacheService>());

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
        var patchRequest = new PatchAgentDefinitionRequest
        {
            DisplayName = "Patched Display",
        };
        AgentDefinition? persisted = null;

        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(existing);
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        await InvokePatchHandlerAsync("route-id", patchRequest, repository, A.Fake<IPromptCacheService>());

        Assert.NotNull(persisted);
        Assert.Same(existing, persisted);
        Assert.Equal("existing-name", persisted.Name);
        Assert.Equal("Patched Display", persisted.DisplayName);
        Assert.Equal("Existing description", persisted.Description);
        Assert.Equal("Existing instructions", persisted.Instructions);
        Assert.Equal("existing-model", persisted.ModelConnectionName);
        Assert.Equal("existing-embedding", persisted.EmbeddingProviderName);
        Assert.True(persisted.Enabled);
        Assert.Single(persisted.Tools);
        Assert.Equal("server-a", persisted.Tools[0].ServerId);
        Assert.True(persisted.IsBuiltIn);
        Assert.Equal(existing.CreatedAt, persisted.CreatedAt);
        Assert.True(persisted.UpdatedAt >= new DateTime(2024, 2, 3, 3, 4, 5, DateTimeKind.Utc));
    }

    [Fact]
    public async Task PatchDefinitionAsync_UpdatesEnabledWhenExplicitlyProvided()
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
        var patchRequest = new PatchAgentDefinitionRequest
        {
            Enabled = false,
        };
        AgentDefinition? persisted = null;

        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(existing);
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        await InvokePatchHandlerAsync("route-id", patchRequest, repository, A.Fake<IPromptCacheService>());

        Assert.NotNull(persisted);
        Assert.False(persisted.Enabled);
    }

    [Fact]
    public async Task PatchDefinitionAsync_ClearsNullableFieldsListedInClearFields()
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
        var patchRequest = new PatchAgentDefinitionRequest
        {
            DisplayName = "Patched Display",
            ClearFields = [nameof(AgentDefinition.ModelConnectionName), nameof(AgentDefinition.Description)],
        };
        AgentDefinition? persisted = null;

        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(existing);
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        await InvokePatchHandlerAsync("route-id", patchRequest, repository, A.Fake<IPromptCacheService>());

        Assert.NotNull(persisted);
        Assert.Equal("Patched Display", persisted.DisplayName);
        Assert.Null(persisted.ModelConnectionName);
        Assert.Null(persisted.Description);
        Assert.Equal("Existing instructions", persisted.Instructions);
        Assert.Equal("existing-embedding", persisted.EmbeddingProviderName);
    }

    [Fact]
    public async Task PatchDefinitionAsync_EvictsPromptCachesOnSuccess()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var promptCache = A.Fake<IPromptCacheService>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "existing-id", name: "existing-name", displayName: "Existing Display",
                description: "Existing description", instructions: "Existing instructions", enabled: true,
                modelConnectionName: "existing-model", embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1),
                tools: []));
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);

        await InvokePatchHandlerAsync("route-id", new PatchAgentDefinitionRequest { DisplayName = "Patched" }, repository, promptCache);

        AssertEvicted(promptCache);
    }

    [Fact]
    public async Task ReplaceDefinitionAsync_EvictsPromptCachesOnSuccess()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var promptCache = A.Fake<IPromptCacheService>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "existing-id", name: "existing-name", displayName: "Existing Display",
                description: "Existing description", instructions: "Existing instructions", enabled: true,
                modelConnectionName: "existing-model", embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1),
                tools: []));
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);

        await InvokeReplaceHandlerAsync("ReplaceDefinitionAsync", "route-id", CreateDefinition(
            id: "client-id", name: "replacement-name", displayName: "Replacement Display",
            description: "Replacement description", instructions: "Replacement instructions", enabled: true,
            modelConnectionName: "new-model", embeddingProviderName: null, isBuiltIn: false,
            isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []), repository, promptCache);

        AssertEvicted(promptCache);
    }

    [Fact]
    public async Task CreateDefinitionAsync_EvictsPromptCachesOnSuccess()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var promptCache = A.Fake<IPromptCacheService>();
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);

        var definition = CreateDefinition(
            id: "custom-agent", name: "custom-agent", displayName: "Custom",
            description: "A custom agent", instructions: "Be helpful", enabled: true,
            modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
            isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []);

        await InvokeCreateHandlerAsync(definition, repository, promptCache);

        AssertEvicted(promptCache);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_EvictsPromptCachesOnSuccess()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var promptCache = A.Fake<IPromptCacheService>();
        var provider = A.Fake<IDynamicAgentProvider>();
        var registry = A.Fake<IAgentRegistry>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "custom-agent", name: "custom-agent", displayName: "Custom",
                description: "A custom agent", instructions: "Be helpful", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1),
                tools: []));

        await InvokeDeleteHandlerAsync("route-id", repository, provider, registry, promptCache);

        AssertEvicted(promptCache);
    }

    [Fact]
    public async Task ReloadAgentsAsync_EvictsPromptCachesAfterReload()
    {
        var loader = A.Fake<IDynamicAgentLoader>();
        var promptCache = A.Fake<IPromptCacheService>();

        await InvokeReloadHandlerAsync(loader, promptCache);

        AssertEvicted(promptCache);
    }

    private static void AssertEvicted(IPromptCacheService promptCache)
    {
        // Any catalog-affecting change must drop cached routing decisions (their keys contain
        // no agent fingerprint) and cached chat decisions (semantic hits ignore instructions).
        A.CallTo(() => promptCache.EvictAllAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => promptCache.EvictAllChatEntriesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static async Task InvokeReplaceHandlerAsync(string methodName, string id, AgentDefinition definition, IAgentDefinitionRepository repository, IPromptCacheService promptCache)
    {
        var method = typeof(AgentDefinitionApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, definition, repository, promptCache]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
    }

    private static async Task InvokePatchHandlerAsync(string id, PatchAgentDefinitionRequest request, IAgentDefinitionRepository repository, IPromptCacheService promptCache)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("PatchDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, request, repository, promptCache]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
    }

    private static async Task InvokeCreateHandlerAsync(AgentDefinition definition, IAgentDefinitionRepository repository, IPromptCacheService promptCache)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("CreateDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [definition, repository, promptCache]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
    }

    private static async Task InvokeDeleteHandlerAsync(string id, IAgentDefinitionRepository repository, IDynamicAgentProvider provider, IAgentRegistry registry, IPromptCacheService promptCache)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("DeleteDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, repository, provider, registry, promptCache]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
    }

    private static async Task InvokeReloadHandlerAsync(IDynamicAgentLoader loader, IPromptCacheService promptCache)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("ReloadAgentsAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [loader, promptCache]) as Task;

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
