using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Extensions;
using Microsoft.SemanticKernel.Agents.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;

namespace lucia.Agents.Agents;

[Experimental("SKEXP0110")]
internal sealed class A2AHistoryChannel : AgentChannel
{
    private static readonly HashSet<Type> s_contentMap =
    [
        typeof(FunctionCallContent),
        typeof(FunctionResultContent),
        typeof(ImageContent),
    ];

    private readonly ChatHistory _history;
    
    public A2AHistoryChannel(ChatHistory? history = null)
    {
        _history = history ?? [];
    }

    protected override async IAsyncEnumerable<(bool IsVisible, ChatMessageContent Message)> InvokeAsync(
        Agent agent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (agent is not A2AClientAgent clientAgent)
        {
            throw new KernelException($"Invalid channel binding for agent: {agent.Id} ({agent.GetType().FullName}). Expected A2AClientAgent.");
            ;
        }

        await clientAgent.ReduceAsync(_history, cancellationToken).ConfigureAwait(false);

        var messageCount = _history.Count;
        HashSet<ChatMessageContent> mutatedHistory = [];
        Queue<ChatMessageContent> messageQueue = [];

        ChatMessageContent? yieldMessage = null;

        await foreach (ChatMessageContent responseMessage in clientAgent
                           .InvokeAsync(_history, null, null, cancellationToken).ConfigureAwait(false))
        {
            for (var messageIndex = messageCount; messageIndex < _history.Count; messageIndex++)
            {
                var mutatedMessage = _history[messageIndex];
                mutatedHistory.Add(mutatedMessage);
                messageQueue.Enqueue(mutatedMessage);
            }
            
            messageCount = _history.Count;

            if (!mutatedHistory.Contains(responseMessage))
            {
                _history.Add(responseMessage);
                messageQueue.Enqueue(responseMessage);
            }
            
            yieldMessage = messageQueue.Dequeue();
            yield return (IsMessageVisible(yieldMessage), yieldMessage);
        }

        while (messageQueue.Count > 0)
        {
            yieldMessage = messageQueue.Dequeue();
            yield return (IsMessageVisible(yieldMessage), yieldMessage);       
        }

        bool IsMessageVisible(ChatMessageContent message) =>
            (!message.Items.Any(i => i is FunctionCallContent || i is FunctionResultContent) ||
             messageQueue.Count == 0);
    }

    protected override async IAsyncEnumerable<StreamingChatMessageContent> InvokeStreamingAsync(
        Agent agent,
        IList<ChatMessageContent> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (agent is not A2AClientAgent clientAgent)
        {
            throw new KernelException($"Invalid channel binding for agent: {agent.Id} ({agent.GetType().FullName}). Expected A2AClientAgent.");
        }
        
        await clientAgent.ReduceAsync(_history, cancellationToken).ConfigureAwait(false);
        
        var messageCount = _history.Count;

        await foreach (StreamingChatMessageContent streamingMessage in clientAgent
                           .InvokeStreamingAsync(_history, null, null, cancellationToken).ConfigureAwait(false))
        {
            yield return streamingMessage;
        }

        for (var index = messageCount; index < _history.Count; index++)
        {
            messages.Add(_history[index]);
        }
    }

    protected override Task ReceiveAsync(IEnumerable<ChatMessageContent> history, CancellationToken cancellationToken = new CancellationToken())
    {
        _history.AddRange(
            history.Where(
                m => !string.IsNullOrEmpty(m.Content) ||
                m.Items.Any(i => s_contentMap.Contains(i.GetType()))
            )
        );
        
        return Task.CompletedTask;
    }

    protected override IAsyncEnumerable<ChatMessageContent> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        return _history.ToDescendingAsync();
    }

    protected override Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _history.Clear();
        return Task.CompletedTask;
    }

    protected override string Serialize()
        => JsonSerializer.Serialize(ChatMessageReference.Prepare(_history));
}