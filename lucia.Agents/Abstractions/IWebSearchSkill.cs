using Microsoft.Extensions.AI;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Abstraction for a web search skill that can be optionally provided by a plugin.
/// Agents that support web search accept an optional <see cref="IWebSearchSkill"/>
/// via DI and expose search tools only when a provider is registered.
/// </summary>
public interface IWebSearchSkill
{
    /// <summary>Returns the AI tools exposed by this search provider.</summary>
    IList<AITool> GetTools();

    /// <summary>Performs one-time initialization (e.g. connectivity check, logging).</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
