using A2A;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;


namespace lucia.Agents.Extensions
{
    public static class AIAgentExtensions
    {
        /// <summary>
        /// Core A2A setup: wires the AIAgent into a TaskManager with message handling,
        /// and optionally registers the AgentCard query handler.
        /// </summary>
        private static ITaskManager MapA2ACore(
            AIAgent agent,
            ITaskManager? taskManager,
            ILoggerFactory? loggerFactory,
            AgentSessionStore? agentSessionStore,
            AgentCard? agentCard)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentNullException.ThrowIfNull(agent.Name);

            var hostAgent = new AIHostAgent(
                innerAgent: agent,
                sessionStore: agentSessionStore ?? new NoopAgentSessionStore());

            taskManager ??= new TaskManager();
            taskManager.OnMessageReceived += OnMessageReceivedAsync;

            if (agentCard is not null)
            {
                taskManager.OnAgentCardQuery += (context, query) =>
                {
                    if (string.IsNullOrEmpty(agentCard.Url))
                    {
                        var agentCardUrl = context.TrimEnd('/');
                        if (!context.EndsWith("/v1/card", StringComparison.Ordinal))
                        {
                            agentCardUrl += "/v1/card";
                        }
                        agentCard.Url = agentCardUrl;
                    }
                    return Task.FromResult(agentCard);
                };
            }

            return taskManager;

            async Task<A2AResponse> OnMessageReceivedAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken)
            {
                var contextId = messageSendParams.Message.ContextId ?? Guid.NewGuid().ToString("N");
                var session = await hostAgent.GetOrCreateSessionAsync(contextId, cancellationToken).ConfigureAwait(false);

                // Tag chat messages with A2A contextId so the orchestrator can use it
                // as a stable Redis session key for multi-turn conversation continuity
                var chatMessages = messageSendParams.ToChatMessages();
                foreach (var msg in chatMessages)
                {
                    msg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                    msg.AdditionalProperties["a2a.contextId"] = contextId;
                }

                var response = await hostAgent.RunAsync(
                    chatMessages,
                    session: session,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await hostAgent.SaveSessionAsync(contextId, session, cancellationToken).ConfigureAwait(false);

                // Check if any response message signals that further user input is needed
                var needsInput = response.Messages
                    .Any(m => m.AdditionalProperties?.TryGetValue("lucia.needsInput", out var val) == true
                              && val is true);

                var parts = response.Messages.ToParts();
                var responseId = response.ResponseId ?? Guid.NewGuid().ToString("N");

                if (needsInput)
                {
                    // Return an AgentTask with InputRequired state so the caller
                    // knows to keep the conversation open for user follow-up
                    var taskId = Guid.NewGuid().ToString("N");
                    var agentMessage = new AgentMessage
                    {
                        MessageId = responseId,
                        ContextId = contextId,
                        TaskId = taskId,
                        Role = MessageRole.Agent,
                        Parts = parts
                    };

                    return new AgentTask
                    {
                        Id = taskId,
                        ContextId = contextId,
                        Status = new AgentTaskStatus
                        {
                            State = TaskState.InputRequired,
                            Message = agentMessage,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        History = [agentMessage]
                    };
                }

                // Agents that support push notifications or state history are expected
                // to produce long-running tasks (e.g. timers, background jobs). Return
                // an AgentTask with Working state so callers can track progress.
                var hasLongRunningCapabilities = agentCard?.Capabilities is { } caps
                    && (caps.PushNotifications == true || caps.StateTransitionHistory == true);

                // Detect if the response contains tool call results — signals that the
                // agent performed an action (created a timer, etc.) vs. just answering
                var performedAction = response.Messages
                    .Any(m => m.Contents.OfType<FunctionResultContent>().Any());

                if (hasLongRunningCapabilities && performedAction)
                {
                    var taskId = Guid.NewGuid().ToString("N");
                    var agentMessage = new AgentMessage
                    {
                        MessageId = responseId,
                        ContextId = contextId,
                        TaskId = taskId,
                        Role = MessageRole.Agent,
                        Parts = parts
                    };

                    return new AgentTask
                    {
                        Id = taskId,
                        ContextId = contextId,
                        Status = new AgentTaskStatus
                        {
                            State = TaskState.Working,
                            Message = agentMessage,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        History = [agentMessage]
                    };
                }

                return new AgentMessage
                {
                    MessageId = responseId,
                    ContextId = contextId,
                    Role = MessageRole.Agent,
                    Parts = parts
                };
            }
        }

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path)
        => endpoints.MapA2A(agent, path, agentCard: null, _ => { });

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path, AgentCard agentCard)
        => endpoints.MapA2A(agent, path, agentCard, _ => { });

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path, AgentCard? agentCard, Action<ITaskManager> configureTaskManager)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(agent);

            var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var agentSessionStore = endpoints.ServiceProvider.GetKeyedService<AgentSessionStore>(agent.Name);
            var taskManager = MapA2ACore(agent, taskManager: null, loggerFactory, agentSessionStore, agentCard);
            var endpointConventionBuilder = endpoints.MapA2A(taskManager, path);

            configureTaskManager(taskManager);

            return endpointConventionBuilder;
        }

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path, Action<ITaskManager> configureTaskManager)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(agent);

            var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var agentSessionStore = endpoints.ServiceProvider.GetKeyedService<AgentSessionStore>(agent.Name);
            var taskManager = MapA2ACore(agent, taskManager: null, loggerFactory, agentSessionStore, agentCard: null);
            var endpointConventionBuilder = endpoints.MapA2A(taskManager, path);

            configureTaskManager(taskManager);
            return endpointConventionBuilder;
        }

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, ITaskManager taskManager, string path)
        {
            // note: current SDK version registers multiple `.well-known/agent.json` handlers here.
            // it makes app return HTTP 500, but will be fixed once new A2A SDK is released.
            // see https://github.com/microsoft/agent-framework/issues/476 for details
            A2ARouteBuilderExtensions.MapA2A(endpoints, taskManager, path);
            return endpoints.MapHttpA2A(taskManager, path);
        }


        // Below are helper methods that can be removed when the next version of Agent Framework releases
        internal static List<Part> ToParts(this IList<ChatMessage> chatMessages)
        {
            if (chatMessages is null || chatMessages.Count == 0)
            {
                return [];
            }

            var parts = new List<Part>();
            foreach (var chatMessage in chatMessages)
            {
                foreach (var content in chatMessage.Contents)
                {
                    var part = content.ToPart();
                    if (part is not null)
                    {
                        parts.Add(part);
                    }
                }
            }

            return parts;
        }

        internal static List<ChatMessage> ToChatMessages(this MessageSendParams messageSendParams)
        {
            if (messageSendParams is null)
            {
                return [];
            }

            var result = new List<ChatMessage>();
            if (messageSendParams.Message?.Parts is not null)
            {
                result.Add(messageSendParams.Message.ToChatMessage());
            }

            return result;
        }

        internal static IList<ChatMessage> ToChatMessages(this AgentTask agentTask)
        {
            ArgumentNullException.ThrowIfNull(agentTask);

            List<ChatMessage> messages = [];

            if (agentTask.Artifacts is not null)
            {
                foreach (var artifact in agentTask.Artifacts)
                {
                    messages.Add(artifact.ToChatMessage());
                }
            }

            return messages;
        }

        internal static ChatMessage ToChatMessage(this Artifact artifact)
        {
            List<AIContent>? aiContents = null;

            foreach (var part in artifact.Parts)
            {
                var content = part.ToAIContent();
                if (content is not null)
                {
                    (aiContents ??= []).Add(content);
                }
            }

            return new ChatMessage(ChatRole.Assistant, aiContents)
            {
                AdditionalProperties = artifact.Metadata.ToAdditionalProperties(),
                RawRepresentation = artifact,
            };
        }

        internal static AdditionalPropertiesDictionary? ToAdditionalProperties(this Dictionary<string, JsonElement>? metadata)
        {
            if (metadata is not { Count: > 0 })
            {
                return null;
            }

            var additionalProperties = new AdditionalPropertiesDictionary();
            foreach (var kvp in metadata)
            {
                additionalProperties[kvp.Key] = kvp.Value;
            }
            return additionalProperties;
        }
    }
}
