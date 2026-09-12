using lucia.Agents.Orchestration;
using lucia.Agents.Services;
using lucia.Agents.Training;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Integration;

/// <summary>
/// Factory that wraps an <see cref="IChatClient"/> with <see cref="AgentTracingChatClient"/>
/// to enable tool-call event streaming and conversation trace persistence.
/// </summary>
public sealed class TracingChatClientFactory
{
    private readonly ITraceRepository _repository;
    private readonly ILoggerFactory _loggerFactory;
    private readonly LiveActivityChannel? _liveChannel;

    public TracingChatClientFactory(
        ITraceRepository repository,
        ILoggerFactory loggerFactory)
        : this(repository, loggerFactory, null, null)
    {
    }

    public TracingChatClientFactory(
        ITraceRepository repository,
        ILoggerFactory loggerFactory,
        LiveActivityChannel liveChannel)
        : this(repository, loggerFactory, liveChannel, null)
    {
    }

    public TracingChatClientFactory(
        ITraceRepository repository,
        ILoggerFactory loggerFactory,
        LiveActivityChannel? liveChannel = null,
        UserContextProvider? userContextProvider = null)
    {
        _repository = repository;
        _loggerFactory = loggerFactory;
        _liveChannel = liveChannel;
        AIContextProviders = userContextProvider is null ? [] : [userContextProvider];
        if (userContextProvider is not null)
        {
            // RC4 stores context-provider messages in session history unless explicitly filtered.
            ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
            {
                StorageInputRequestMessageFilter = messages => messages.Where(message =>
                    message.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
                    && message.GetAgentRequestMessageSourceId() != typeof(UserContextProvider).FullName)
            });
        }
    }

    public IReadOnlyList<AIContextProvider> AIContextProviders { get; }

    public Microsoft.Agents.AI.ChatHistoryProvider? ChatHistoryProvider { get; }

    /// <summary>
    /// Wraps the given <paramref name="inner"/> client with tracing for the specified agent.
    /// </summary>
    public IChatClient Wrap(IChatClient inner, string agentId)
    {
        var logger = _loggerFactory.CreateLogger<AgentTracingChatClient>();
        return new AgentTracingChatClient(inner, agentId, _repository, logger, _liveChannel);
    }
}
