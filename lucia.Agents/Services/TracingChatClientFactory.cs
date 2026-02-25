using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

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
        ILoggerFactory loggerFactory,
        LiveActivityChannel liveChannel)
    {
        _repository = repository;
        _loggerFactory = loggerFactory;
        _liveChannel = liveChannel;
    }

    public TracingChatClientFactory(
        ITraceRepository repository,
        ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Wraps the given <paramref name="inner"/> client with tracing for the specified agent.
    /// </summary>
    public IChatClient Wrap(IChatClient inner, string agentId)
    {
        var logger = _loggerFactory.CreateLogger<AgentTracingChatClient>();
        return new AgentTracingChatClient(inner, agentId, _repository, logger, _liveChannel);
    }
}
