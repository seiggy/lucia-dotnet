using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Extensions;

/// <summary>
/// Adapts the GitHub Copilot SDK's <see cref="CopilotClient"/>/<see cref="CopilotSession"/>
/// to the <see cref="IChatClient"/> interface.
/// <para>
/// The Copilot SDK requires the <c>copilot</c> CLI binary installed and authenticated on the host.
/// Each adapter instance manages a single <see cref="CopilotClient"/> process and reuses sessions.
/// </para>
/// </summary>
public sealed class CopilotChatClientAdapter : IChatClient
{
    private readonly CopilotClient _client;
    private readonly string _model;
    private readonly string? _systemMessage;
    private CopilotSession? _session;
    private bool _started;
    private bool _disposed;

    public CopilotChatClientAdapter(CopilotClientOptions options, string model, string? systemMessage = null)
    {
        _client = new CopilotClient(options);
        _model = model;
        _systemMessage = systemMessage;
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new("github-copilot", defaultModelId: _model);

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        // Extract the last user message as the prompt
        var lastUserMessage = chatMessages.LastOrDefault(m => m.Role == ChatRole.User);
        var prompt = lastUserMessage?.Text ?? string.Empty;

        var messageOptions = new MessageOptions { Prompt = prompt };
        var response = await _session!.SendAndWaitAsync(messageOptions, timeout: null, cancellationToken).ConfigureAwait(false);

        var content = response?.Data?.Content ?? string.Empty;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, content))
        {
            ModelId = _model,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Copilot SDK supports streaming via the On() event handler, but for simplicity
        // we use SendAndWaitAsync and yield the full response as a single update.
        var response = await GetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);
        var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
            ModelId = _model,
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(CopilotChatClientAdapter) && serviceKey is null)
            return this;

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_session is not null)
            {
                _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _session = null;
            }

            if (_started)
            {
                _client.StopAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best-effort cleanup of the CLI process
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_started)
        {
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }

        if (_session is not null) return;

        var config = new SessionConfig
        {
            SystemMessage = _systemMessage is not null
                ? new SystemMessageConfig { }
                : null,
        };

        _session = await _client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);
    }
}
