using System;
using System.Collections.Generic;
using System.Text;

namespace lucia.Agents.Configuration
{
    public sealed class AgentConfiguration
    {
        /// <summary>
        /// Name of the Agent Class
        /// e.g. "Lucia.Agents.LightAgent, Lucia.Agents"
        /// </summary>
        public string AgentType { get; set; } = default!;

        public string AgentName { get; set; } = default!;

        /// <summary>
        /// Collection of Skills to create and assign to the Agent
        /// e.g. "Lucis.Skills.LightControlSkill, Lucia.Skills"
        /// </summary>
        public List<string> AgentSkills { get; set; } = [];

        /// <summary>
        /// Configuration section name for this agent's options
        /// e.g. "MusicAssistant" (will bind to the agent's options type)
        /// </summary>
        public string? AgentConfig { get; set; }

        /// <summary>
        /// Optional connection name for the AI model to use with this agent.
        /// When set, a keyed IChatClient is resolved from DI using this name.
        /// If null, the default (unkeyed) IChatClient is used.
        /// </summary>
        public string? ModelConnectionName { get; set; }
    }
}
