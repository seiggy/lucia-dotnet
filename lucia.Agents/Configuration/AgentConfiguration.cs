using System;
using System.Collections.Generic;
using System.Text;

namespace lucia.Agents.Configuration
{
    public class AgentConfiguration
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
    }
}
