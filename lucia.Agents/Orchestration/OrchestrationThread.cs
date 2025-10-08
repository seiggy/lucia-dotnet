using Microsoft.Agents.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace lucia.Agents.Orchestration
{
    public class OrchestrationThread : AgentThread
    {
        public OrchestrationThread() : base()
        {
        }

        public OrchestrationThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null) : 
            base()
        {
            // Custom deserialization logic here
        }

        public string? ConversationId { get; internal set; }
    }
}
