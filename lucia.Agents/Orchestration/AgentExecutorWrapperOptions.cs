using System;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration for agent executor wrappers.
/// </summary>
public sealed class AgentExecutorWrapperOptions
{
    /// <summary>
    /// Maximum execution duration before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional size cap for preserved history entries.
    /// </summary>
    public int HistoryLimit { get; set; } = 20;
}
