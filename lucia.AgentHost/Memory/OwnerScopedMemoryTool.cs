using AgentMemory.Abstractions.Services;
using AgentMemory.AgentFramework.Tools;
using Microsoft.Extensions.AI;

namespace lucia.AgentHost.Memory;

/// <summary>
/// Runs one Agent Memory tool in a fresh DI scope under the verified speaker's owner scope.
/// </summary>
/// <remarks>
/// Agent Memory services are scoped and read the owner from an AsyncLocal. The hook that created this
/// tool has already disposed its scope and returned, so each call re-enters both.
/// </remarks>
internal sealed class OwnerScopedMemoryTool(AIFunction template, IServiceScopeFactory scopeFactory, string userId)
    : DelegatingAIFunction(template)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var owner = services.GetRequiredService<IWritableMemoryOwnerContext>().BeginOwnerScope(userId);
        var tool = services.GetRequiredService<MemoryToolFactory>().CreateAIFunctions()
            .Single(candidate => candidate.Name == Name);
        return await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
