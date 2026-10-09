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
            id: "route-id",
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
            name: "route-id",
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

        await InvokeReplaceHandlerAsync("route-id", replacementRequest, repository);

        Assert.NotNull(persisted);
        Assert.Equal("route-id", persisted.Id);
        Assert.Equal("route-id", persisted.Name);
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
            id: "route-id",
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

        await InvokePatchHandlerAsync("route-id", patchRequest, repository);

        Assert.NotNull(persisted);
        Assert.Same(existing, persisted);
        // Identity is the route Id: a legacy Name drift self-heals to the Id on any patch.
        Assert.Equal("route-id", persisted.Name);
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
            id: "route-id",
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

        await InvokePatchHandlerAsync("route-id", patchRequest, repository);

        Assert.NotNull(persisted);
        Assert.False(persisted.Enabled);
    }

    [Fact]
    public async Task PatchDefinitionAsync_ClearsNullableFieldsListedInClearFields()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var existing = CreateDefinition(
            id: "route-id",
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

        await InvokePatchHandlerAsync("route-id", patchRequest, repository);

        Assert.NotNull(persisted);
        Assert.Equal("Patched Display", persisted.DisplayName);
        Assert.Null(persisted.ModelConnectionName);
        Assert.Null(persisted.Description);
        Assert.Equal("Existing instructions", persisted.Instructions);
        Assert.Equal("existing-embedding", persisted.EmbeddingProviderName);
    }

    [Fact]
    public async Task CreateDefinitionAsync_DerivesNameFromId_WhenNameMissing()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        AgentDefinition? persisted = null;
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        var result = await InvokeCreateHandlerAsync(
            CreateDefinition(
                id: "research-agent", name: null!, displayName: "Research Agent",
                description: "A custom agent", instructions: "Be helpful", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []),
            repository);

        Assert.NotNull(persisted);
        Assert.Equal("research-agent", persisted.Name);
        NotBadResult(result);
    }

    [Fact]
    public async Task CreateDefinitionAsync_RejectsNonKebabCaseId()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();

        var result = await InvokeCreateHandlerAsync(
            CreateDefinition(
                id: "My Agent 1", name: "my-agent-1", displayName: "My Agent",
                description: "A custom agent", instructions: "Be helpful", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []),
            repository);

        IsBadRequest(result, "kebab-case");
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateDefinitionAsync_RejectsNameDifferentFromId()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();

        var result = await InvokeCreateHandlerAsync(
            CreateDefinition(
                id: "research-agent", name: "Research Agent", displayName: "Research Agent",
                description: "A custom agent", instructions: "Be helpful", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []),
            repository);

        IsBadRequest(result, "must equal its id");
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ReplaceDefinitionAsync_RejectsNameDifferentFromRouteId()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "route-id", name: "route-id", displayName: "Existing Display",
                description: "Existing description", instructions: "Existing instructions", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1), tools: []));

        var result = await InvokeReplaceHandlerAsync(
            "route-id",
            CreateDefinition(
                id: "route-id", name: "other-name", displayName: "Replacement Display",
                description: "Replacement description", instructions: "Replacement instructions", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, tools: []),
            repository);

        IsBadRequest(result, "must equal its id");
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task PatchDefinitionAsync_RejectsNameDifferentFromRouteId()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "route-id", name: "route-id", displayName: "Existing Display",
                description: "Existing description", instructions: "Existing instructions", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1), tools: []));

        var result = await InvokePatchHandlerAsync("route-id", new PatchAgentDefinitionRequest { Name = "other-name" }, repository);

        IsBadRequest(result, "must equal its id");
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task PatchDefinitionAsync_SelfHealsLegacyNameToRouteId()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        AgentDefinition? persisted = null;
        // Legacy document where Name drifted away from Id (the OW-agent incident shape).
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "route-id", name: "legacy display name", displayName: "Existing Display",
                description: "Existing description", instructions: "Existing instructions", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1), tools: []));
        A.CallTo(() => repository.UpsertAgentDefinitionAsync(A<AgentDefinition>._, A<CancellationToken>._))
            .Invokes(call => persisted = call.GetArgument<AgentDefinition>(0))
            .Returns(Task.CompletedTask);

        var result = await InvokePatchHandlerAsync("route-id", new PatchAgentDefinitionRequest { DisplayName = "Patched" }, repository);

        NotBadResult(result);
        Assert.NotNull(persisted);
        Assert.Equal("route-id", persisted.Name);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_UnregistersByAgentId_NotByName()
    {
        var repository = A.Fake<IAgentDefinitionRepository>();
        var provider = A.Fake<IDynamicAgentProvider>();
        var registry = A.Fake<IAgentRegistry>();
        // Legacy document with drifted Name: deregistration must key on the Id
        // because that is what the dynamic agent provider and A2A URL use.
        A.CallTo(() => repository.GetAgentDefinitionAsync("route-id", A<CancellationToken>._))
            .Returns(CreateDefinition(
                id: "route-id", name: "legacy display name", displayName: "Custom",
                description: "A custom agent", instructions: "Be helpful", enabled: true,
                modelConnectionName: null, embeddingProviderName: null, isBuiltIn: false,
                isRemote: false, isOrchestrator: false, createdAt: DateTime.UtcNow.AddDays(-1), updatedAt: DateTime.UtcNow.AddDays(-1), tools: []));

        await InvokeDeleteHandlerAsync("route-id", repository, provider, registry);

        A.CallTo(() => provider.Unregister("route-id")).MustHaveHappenedOnceExactly();
        A.CallTo(() => provider.Unregister("legacy display name")).MustNotHaveHappened();
        A.CallTo(() => registry.UnregisterAgentAsync("/a2a/route-id", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Unwraps the concrete result (Created/Ok/Conflict/BadRequest/NotFound) from a
    /// handler's Task&lt;Results&lt;...&gt;&gt; without referencing the generic Results type.
    /// </summary>
    private static object? GetHandlerResultValue(Task task)
    {
        // Handlers return Task<Results<...>> (Microsoft.AspNetCore.Http.HttpResults.Results),
        // whose concrete result is exposed via the 'Result' property. Task<T>.GetAwaiter() hides
        // (does not override) Task.GetAwaiter(), so calling it through a static Task reference
        // yields the non-generic awaiter — invoke GetAwaiter on the concrete type instead.
        var getAwaiter = task.GetType().GetMethod("GetAwaiter")!;
        var awaiter = getAwaiter.Invoke(task, null)!;
        var results = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);
        return results?.GetType().GetProperty("Result")?.GetValue(results);
    }

    private static void NotBadResult(object? result)
    {
        Assert.NotNull(result);
        Assert.False(
            result.GetType().Name.StartsWith("BadRequest", StringComparison.Ordinal),
            $"Expected a successful result but got {result.GetType().Name}: {GetResultPayload(result)}");
    }

    private static void IsBadRequest(object? result, string expectedMessagePart)
    {
        Assert.NotNull(result);
        Assert.True(
            result.GetType().Name.StartsWith("BadRequest", StringComparison.Ordinal),
            $"Expected BadRequest but got {result.GetType().Name}: {GetResultPayload(result)}");
        var payload = GetResultPayload(result);
        Assert.Contains(expectedMessagePart, payload);
    }

    private static string? GetResultPayload(object? result)
        => result?.GetType().GetProperty("Value")?.GetValue(result) as string;

    private static async Task<object?> InvokeReplaceHandlerAsync(string id, AgentDefinition definition, IAgentDefinitionRepository repository)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("ReplaceDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, definition, repository]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
        return GetHandlerResultValue(task);
    }

    private static async Task<object?> InvokePatchHandlerAsync(string id, PatchAgentDefinitionRequest request, IAgentDefinitionRepository repository)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("PatchDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, request, repository]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
        return GetHandlerResultValue(task);
    }

    private static async Task<object?> InvokeCreateHandlerAsync(AgentDefinition definition, IAgentDefinitionRepository repository)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("CreateDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [definition, repository]) as Task;

        Assert.NotNull(task);
        await task.ConfigureAwait(false);
        return GetHandlerResultValue(task);
    }

    private static async Task InvokeDeleteHandlerAsync(string id, IAgentDefinitionRepository repository, IDynamicAgentProvider provider, IAgentRegistry registry)
    {
        var method = typeof(AgentDefinitionApi).GetMethod("DeleteDefinitionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = method.Invoke(null, [id, repository, provider, registry]) as Task;

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
