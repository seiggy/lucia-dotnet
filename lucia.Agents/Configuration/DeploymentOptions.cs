using System;

namespace lucia.Agents.Configuration;

/// <summary>
/// Controls deployment topology: standalone (all agents in-process) or mesh (distributed A2A agents).
/// </summary>
public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>
    /// Deployment mode. "standalone" embeds all plugin agents in the AgentHost process.
    /// "mesh" expects external A2A agent containers to register over the network.
    /// </summary>
    public string Mode { get; set; } = "standalone";

    public bool IsStandalone => Mode.Equals("standalone", StringComparison.OrdinalIgnoreCase);
}
