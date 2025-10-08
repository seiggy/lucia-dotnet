using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucia.Agents.Orchestration
{
    internal class LuciaExecutor : ReflectingExecutor<LuciaExecutor>, IMessageHandler<ChatMessage, AgentChoiceResult>
    {
        private readonly AIAgent _orchestrationAgent;

        public LuciaExecutor(AIAgent orchestrationAgent) : base ("LuciaExecutor")
        {
            _orchestrationAgent = orchestrationAgent;
        }

        public async ValueTask<AgentChoiceResult> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var response = await _orchestrationAgent.RunAsync(message, cancellationToken: cancellationToken);
            var agentDecision = JsonSerializer.Deserialize<AgentChoiceResult>(response.Text);

            return agentDecision;
        }

        public ValueTask<AgentChoiceResult> HandleAsync(ChatMessage message, IWorkflowContext context)
        {
            return HandleAsync(message, context, CancellationToken.None);
        }
    }

    public sealed class AgentChoiceResult
    {
        [JsonPropertyName("agentId")]
        public string AgentId { get; set; }

        /// <summary>
        /// Used for debugging and tracing, not for programmatic use.
        /// </summary>
        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; }
    }
}
