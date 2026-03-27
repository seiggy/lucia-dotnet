using A2A;
using A2A.AspNetCore;
using lucia.Agents.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;


namespace lucia.Agents.Extensions
{
    public static class AIAgentExtensions
    {
        /// <summary>
        /// Core A2A setup: wires the AIAgent into an A2AServer with message handling,
        /// and optionally registers the AgentCard endpoint.
        /// </summary>
        private static IA2ARequestHandler MapA2ACore(
            AIAgent agent,
            ILoggerFactory? loggerFactory,
            AgentSessionStore? agentSessionStore,
            AgentCard? agentCard)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentNullException.ThrowIfNull(agent.Name);

            var hostAgent = new AIHostAgent(
                innerAgent: agent,
                sessionStore: agentSessionStore ?? new NoopAgentSessionStore());

            var handler = new DelegatingAgentHandler(
                async (context, eventQueue, cancellationToken) =>
                {
                    await HandleRequestAsync(hostAgent, agentCard, context, eventQueue, cancellationToken)
                        .ConfigureAwait(false);
                });

            var logger = loggerFactory?.CreateLogger<A2AServer>();
            return new A2AServer(handler, new InMemoryTaskStore(), new ChannelEventNotifier(),
                logger!, new A2AServerOptions());
        }

        /// <summary>
        /// Lazy variant that defers AIHostAgent creation until the first request,
        /// allowing agents to be initialized after endpoint mapping.
        /// </summary>
        private static IA2ARequestHandler MapA2ACoreLazy(
            Func<AIAgent> agentFactory,
            ILoggerFactory? loggerFactory,
            AgentCard? agentCard)
        {
            AIHostAgent? hostAgent = null;
            object lockObj = new();

            AIHostAgent EnsureHostAgent()
            {
                if (hostAgent is not null) return hostAgent;
                lock (lockObj)
                {
                    hostAgent ??= new AIHostAgent(
                        innerAgent: agentFactory(),
                        sessionStore: new NoopAgentSessionStore());
                }
                return hostAgent;
            }

            var handler = new DelegatingAgentHandler(
                async (context, eventQueue, cancellationToken) =>
                {
                    var resolved = EnsureHostAgent();
                    await HandleRequestAsync(resolved, agentCard, context, eventQueue, cancellationToken)
                        .ConfigureAwait(false);
                });

            var logger = loggerFactory?.CreateLogger<A2AServer>();
            return new A2AServer(handler, new InMemoryTaskStore(), new ChannelEventNotifier(),
                logger!, new A2AServerOptions());
        }

        /// <summary>
        /// Shared request processing logic used by both eager and lazy agent wiring.
        /// Processes an incoming A2A request through the AIHostAgent and writes
        /// the appropriate response to the event queue.
        /// </summary>
        private static async Task HandleRequestAsync(
            AIHostAgent hostAgent,
            AgentCard? agentCard,
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken)
        {
            var contextId = context.ContextId ?? Guid.NewGuid().ToString("N");
            var session = await hostAgent.GetOrCreateSessionAsync(contextId, cancellationToken).ConfigureAwait(false);

            // Convert the A2A message to chat messages
            var chatMessages = new List<ChatMessage>();
            if (context.Message?.Parts is not null)
            {
                var chatMsg = context.Message.ToChatMessage();
                chatMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                chatMsg.AdditionalProperties["a2a.contextId"] = contextId;
                chatMessages.Add(chatMsg);
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
                var taskUpdater = new TaskUpdater(eventQueue, context.TaskId ?? Guid.NewGuid().ToString("N"), contextId);
                var message = new Message
                {
                    MessageId = responseId,
                    ContextId = contextId,
                    TaskId = taskUpdater.TaskId,
                    Role = Role.Agent,
                    Parts = parts
                };
                await taskUpdater.RequireInputAsync(message, cancellationToken).ConfigureAwait(false);
                await taskUpdater.SubmitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Detect long-running capabilities and performed actions
            var hasLongRunningCapabilities = agentCard?.Capabilities is { } caps
                && caps.PushNotifications == true;

            var performedAction = response.Messages
                .Any(m => m.Contents.OfType<FunctionResultContent>().Any());

            if (hasLongRunningCapabilities && performedAction)
            {
                var taskUpdater = new TaskUpdater(eventQueue, context.TaskId ?? Guid.NewGuid().ToString("N"), contextId);
                var message = new Message
                {
                    MessageId = responseId,
                    ContextId = contextId,
                    TaskId = taskUpdater.TaskId,
                    Role = Role.Agent,
                    Parts = parts
                };
                await taskUpdater.StartWorkAsync(message, cancellationToken).ConfigureAwait(false);
                await taskUpdater.SubmitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Simple reply - no task tracking needed
            var responder = new MessageResponder(eventQueue, contextId);
            await responder.ReplyAsync(parts, cancellationToken).ConfigureAwait(false);
        }

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path)
        => endpoints.MapA2A(agent, path, agentCard: null);

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, AIAgent agent, string path, AgentCard? agentCard)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(agent);

            var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var agentSessionStore = endpoints.ServiceProvider.GetKeyedService<AgentSessionStore>(agent.Name);
            var requestHandler = MapA2ACore(agent, loggerFactory, agentSessionStore, agentCard);

            if (agentCard is not null)
            {
                return endpoints.MapHttpA2A(requestHandler, agentCard, path);
            }

            return A2ARouteBuilderExtensions.MapA2A(endpoints, requestHandler, path);
        }

        public static IEndpointConventionBuilder MapA2ALazy(this IEndpointRouteBuilder endpoints, Func<AIAgent> agentFactory, string path, AgentCard agentCard)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(agentFactory);

            var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var requestHandler = MapA2ACoreLazy(agentFactory, loggerFactory, agentCard);

            return endpoints.MapHttpA2A(requestHandler, agentCard, path);
        }

        public static IEndpointConventionBuilder MapA2A(this IEndpointRouteBuilder endpoints, IA2ARequestHandler requestHandler, string path)
        {
            return A2ARouteBuilderExtensions.MapA2A(endpoints, requestHandler, path);
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
    }
}
