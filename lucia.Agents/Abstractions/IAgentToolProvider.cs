using Microsoft.Extensions.AI;

namespace lucia.Agents.Abstractions;

/// <summary>
/// A tool capability that a plugin contributes to agents without a core change.
/// Register any number of providers with
/// <c>builder.Services.AddSingleton&lt;IAgentToolProvider&gt;(...)</c> in
/// <see cref="ILuciaPlugin.ConfigureServices"/>. The general agent merges every
/// provider's tools, lists each provider as a skill on its agent card, and appends
/// non-null instruction hints to its instructions. Custom agents can select
/// individual tools from the dashboard alongside MCP tools.
/// </summary>
public interface IAgentToolProvider
{
    /// <summary>
    /// Unique, stable provider name shown on agent cards and in the tool picker.
    /// Saved custom agents reference this name. Renaming it breaks those references.
    /// </summary>
    string Name { get; }

    /// <summary>What the capability does. Shown on the agent card.</summary>
    string Description { get; }

    /// <summary>Optional hint appended to instructions when the agent uses this provider's tools.</summary>
    string? InstructionHint => null;

    /// <summary>Returns the AI tools exposed by this provider.</summary>
    IList<AITool> GetTools();

    /// <summary>
    /// Performs one-time initialization shared by all agents. On failure, tools are
    /// unavailable until host restart and the dashboard shows the initialization error.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
