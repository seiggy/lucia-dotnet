using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using lucia.Agents.A2A;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using lucia.Agents.Agents.Extensions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

public class A2AClientAgent : Agent
{
    private readonly AgentCard _agentCard;
    private readonly HttpClient _httpClient;
    
    public IChatHistoryReducer? HistoryReducer { get; init; }

    public A2AClientAgent(AgentCard agentCard, IHttpClientFactory httpClientFactory)
    {
        _agentCard = agentCard;
        _httpClient = httpClientFactory.CreateClient("A2AClientAgent");
    }

    public A2AClientAgent(
        AgentCard agentCard,
        IHttpClientFactory httpClientFactory,
        PromptTemplateConfig templateConfig,
        IPromptTemplateFactory templateFactory)
    {
        _agentCard = agentCard;
        _httpClient = httpClientFactory.CreateClient("A2AClientAgent");
        Name = templateConfig.Name;
        Description = templateConfig.Description;
        Instructions = templateConfig.Template;
        Arguments = new(templateConfig.ExecutionSettings.Values);
        Template = templateFactory.Create(templateConfig);
    }
    
    /// <summary>
    /// Gets the role used for agent instructions. Defaults to "system".
    /// <remarks>
    /// Some versions of "O" series (deep reasoning) models require the instructions to be provided in the "developer"
    /// role. Other models may not support either "system" or "developer" roles, in which case the agent must send
    /// instructions as a regular user message.
    /// </remarks>
    /// </summary>
    public AuthorRole InstructionsRole { get; init; } = AuthorRole.System; 
    
    public override async IAsyncEnumerable<AgentResponseItem<ChatMessageContent>> InvokeAsync(
        ICollection<ChatMessageContent> messages, 
        AgentThread? thread = null, 
        AgentInvokeOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // verify the message collection is not null or empty
        if (messages == null || messages.Count == 0)
        {
            throw new ArgumentException("Messages collection cannot be null or empty.", nameof(messages));
        }
        
        var agentUri = new Uri(_agentCard.Uri);
        
        if (agentUri == null)
        {
            throw new InvalidOperationException("Agent URI is not specified in the agent card.");
        }
        
        if (agentUri.Scheme.Equals("lucia", StringComparison.OrdinalIgnoreCase))
        {
            // Route to local in-process agent using reflection
            await foreach (var response in InvokeLocalAgentAsync(messages, thread, options, cancellationToken))
            {
                yield return response;
            }
        }
        else if (agentUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || 
                 agentUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            // Route to remote agent using A2A protocol over HTTP
            await foreach (var response in InvokeRemoteAgentAsync(agentUri, messages, thread, options, cancellationToken))
            {
                yield return response;
            }
        }
        else
        {
            throw new NotSupportedException($"URI scheme '{agentUri.Scheme}' is not supported.");
        }
    }
    
    public override async IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> InvokeStreamingAsync(
        ICollection<ChatMessageContent> messages, 
        AgentThread? thread = null,
        AgentInvokeOptions? options = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agentUri = new Uri(_agentCard.Uri);
        
        if (agentUri == null)
        {
            throw new InvalidOperationException("Agent URI is not specified in the agent card.");
        }
        
        if (agentUri.Scheme.Equals("lucia", StringComparison.OrdinalIgnoreCase))
        {
            // Route to local in-process agent using reflection
            await foreach (var response in InvokeLocalAgentStreamingAsync(
                               messages, thread, options, cancellationToken))
            {
                yield return response;
            }
        }
        else if (agentUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || 
                 agentUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            // Route to remote agent using A2A protocol over HTTP
            await foreach (var response in InvokeRemoteAgentStreamingAsync(agentUri, messages, thread, options, cancellationToken))
            {
                yield return response;
            }
        }
        else
        {
            throw new NotSupportedException($"URI scheme '{agentUri.Scheme}' is not supported.");
        }
    }

    [Experimental("SKEXP0110")]
    public Task<bool> ReduceAsync(ChatHistory history, CancellationToken cancellationToken = default) =>
        history.ReduceInPlaceAsync(HistoryReducer, cancellationToken);

    [Experimental("SKEXP0110")]
    protected sealed override IEnumerable<string> GetChannelKeys()
    {
        yield return typeof(A2AHistoryChannel).FullName!;

        if (HistoryReducer is not null)
        {
            yield return HistoryReducer.GetType().FullName!;
            yield return HistoryReducer.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }
    }

    [Experimental("SKEXP0110")]
    protected sealed override Task<AgentChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        A2AHistoryChannel channel =
            new()
            {
                Logger = ActiveLoggerFactory.CreateLogger<A2AHistoryChannel>(),
            };
        return Task.FromResult<AgentChannel>(channel);
    }

    [Experimental("SKEXP0110")]
    protected override Task<AgentChannel> RestoreChannelAsync(string channelState, CancellationToken cancellationToken)
    {
        var history =
            JsonSerializer.Deserialize<ChatHistory>(channelState) ??
            throw new KernelException("Unable to restore channel: invalid state.");
        return Task.FromResult<AgentChannel>(new A2AHistoryChannel(history));
    }

    private async IAsyncEnumerable<AgentResponseItem<ChatMessageContent>> InvokeLocalAgentAsync(
        ICollection<ChatMessageContent> messages,
        AgentThread? thread = null,
        AgentInvokeOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
        {
            throw new ArgumentException("Messages collection cannot be null or empty.", nameof(messages));
        }

        var chatHistoryAgentThread = await EnsureThreadExistsWithMessagesAsync(
            messages,
            thread,
            () => new ChatHistoryAgentThread(),
            cancellationToken).ConfigureAwait(false);

        var kernel = this.GetKernel(options);
#pragma warning disable SKEXP0110, SKEXP0130 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        if (UseImmutableKernel)
            kernel = kernel.Clone();

        var providersContext = await chatHistoryAgentThread.AIContextProviders
            .ModelInvokingAsync(messages, cancellationToken)
            .ConfigureAwait(false);
        
        // check for compatibility with the kernel
        if (providersContext.AIFunctions is { Count : > 0 } && !UseImmutableKernel)
        {
            throw new InvalidOperationException(
                "AIContextProviders with AIFunctions are not compatible when Agent UseImmutableKernel setting is false.");
        }

        kernel.Plugins.AddFromAIContext(providersContext, "Tools");
#pragma warning restore SKEXP0110, SKEXP0130 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        
        ChatHistory chatHistory = [];

        await foreach (var existingMessage in chatHistoryAgentThread.GetMessagesAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            chatHistory.Add(existingMessage);
        }

        var agentName = _agentCard.Name;
        var invokeResults = InternalInvokeAsync(
            agentName,
            chatHistory,
            async (m) =>
            {
                await NotifyThreadOfNewMessage(chatHistoryAgentThread, m, cancellationToken).ConfigureAwait(false);
                if (options?.OnIntermediateMessage is not null)
                {
                    await options.OnIntermediateMessage(m).ConfigureAwait(false);
                }
            },
            options?.KernelArguments,
            kernel,
            FormatAdditionalInstructions(providersContext, options),
            cancellationToken);

        await foreach (var result in invokeResults.ConfigureAwait(false))
        {
            if (!result.Items.Any(i => i is FunctionCallContent || i is FunctionResultContent))
            {
                await NotifyThreadOfNewMessage(chatHistoryAgentThread, result, cancellationToken)
                    .ConfigureAwait(false);

                if (options?.OnIntermediateMessage is not null)
                {
                    await options.OnIntermediateMessage(result).ConfigureAwait(false);
                }
            }
            
            yield return new (result, chatHistoryAgentThread);
        }
    }

    private async IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> InvokeLocalAgentStreamingAsync(
        ICollection<ChatMessageContent> messages, 
        AgentThread? thread,
        AgentInvokeOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatHistoryAgentThread = await EnsureThreadExistsWithMessagesAsync(
            messages,
            thread,
            () => new ChatHistoryAgentThread(),
            cancellationToken).ConfigureAwait(false);

        var kernel = this.GetKernel(options);

#pragma warning disable SKEXP0110, SKEXP0130 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        if (UseImmutableKernel)
            kernel = kernel.Clone();

        var providersContext = await chatHistoryAgentThread.AIContextProviders
            .ModelInvokingAsync(messages, cancellationToken)
            .ConfigureAwait(false);
        
        // check for compatibility with the kernel
        if (providersContext.AIFunctions is { Count : > 0 } && !UseImmutableKernel)
        {
            throw new InvalidOperationException(
                "AIContextProviders with AIFunctions are not compatible when Agent UseImmutableKernel setting is false.");
        }

        kernel.Plugins.AddFromAIContext(providersContext, "Tools");
        
#pragma warning restore SKEXP0110, SKEXP0130 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        ChatHistory chatHistory = [];

        await foreach (var existingMessage in chatHistoryAgentThread.GetMessagesAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            chatHistory.Add(existingMessage);
        }

        var agentName = _agentCard.Name;
        var invokeResults = InternalInvokeStreamingAsync(
            agentName,
            chatHistory,
            async (m) =>
            {
                await NotifyThreadOfNewMessage(chatHistoryAgentThread, m, cancellationToken).ConfigureAwait(false);
                if (options?.OnIntermediateMessage is not null)
                {
                    await options.OnIntermediateMessage(m).ConfigureAwait(false);
                }
            },
            options?.KernelArguments,
            kernel,
            FormatAdditionalInstructions(providersContext, options),
            cancellationToken);
        
        await foreach (var result in invokeResults.ConfigureAwait(false))
        {
            yield return new(result, chatHistoryAgentThread);
        }
    }

    private async IAsyncEnumerable<AgentResponseItem<ChatMessageContent>> InvokeRemoteAgentAsync(
        Uri agentUri, 
        ICollection<ChatMessageContent> messages, 
        AgentThread? thread,
        AgentInvokeOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Prepare A2A request payload
        var requestPayload = new
        {
            Messages = messages,
            Thread = thread,
            Options = options
        };
        
        var content = new StringContent(
            JsonSerializer.Serialize(requestPayload), 
            Encoding.UTF8, 
            "application/json");
        
        // Send request to remote agent using A2A protocol
        var response = await _httpClient.PostAsync(agentUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        // Process response according to A2A protocol
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseData = JsonSerializer.Deserialize<List<AgentResponseItem<ChatMessageContent>>>(responseContent);
        
        if (responseData == null)
        {
            throw new InvalidOperationException("Failed to deserialize response from remote agent.");
        }
        
        foreach (var item in responseData)
        {
            yield return item;
        }
    }

    

    private async IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> InvokeRemoteAgentStreamingAsync(
        Uri agentUri, 
        ICollection<ChatMessageContent> messages, 
        AgentThread? thread,
        AgentInvokeOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Prepare A2A request payload for streaming
        var requestPayload = new
        {
            Messages = messages,
            Thread = thread,
            Options = options,
            Streaming = true
        };
        
        var content = new StringContent(
            JsonSerializer.Serialize(requestPayload), 
            Encoding.UTF8, 
            "application/json");
        
        // Send request to remote agent using A2A protocol with streaming support
        var response = await _httpClient.PostAsync($"{agentUri}/stream", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        // Process streaming response according to A2A protocol
        // This would typically use server-sent events or similar streaming protocol
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
                continue;
                
            if (line.StartsWith("data: "))
            {
                var data = line.Substring("data: ".Length);
                var item = JsonSerializer.Deserialize<AgentResponseItem<StreamingChatMessageContent>>(data);
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static (IChatCompletionService service, PromptExecutionSettings? settings) GetChatCompletionService(
        Kernel kernel,
        KernelArguments? arguments)
    {
        var nullPrompt = KernelFunctionFactory.CreateFromPrompt("placeholder", arguments?.ExecutionSettings?.Values);

        kernel.ServiceSelector.TrySelectAIService<IChatCompletionService>(
            kernel,
            nullPrompt,
            arguments ?? [],
            out var chatCompletionService,
            out var executionSettings
        );
        
#pragma warning disable CA2000 // Dispose objects before losing scope
        if (chatCompletionService is null
            && kernel.ServiceSelector is IChatClientSelector chatClientSelector
            && chatClientSelector.TrySelectChatClient<Microsoft.Extensions.AI.IChatClient>(
                kernel,
                nullPrompt,
                arguments ?? [],
                out var chatClient,
                out executionSettings))
        {
            chatCompletionService = chatClient.AsChatCompletionService();
        }
#pragma warning restore CA2000 // Dispose objects before losing scope

        if (chatCompletionService is null)
        {
            var message = new StringBuilder()
                .Append("No service was found for any of the supported types: ")
                .Append(typeof(IChatCompletionService))
                .Append(", ")
                .Append(typeof(Microsoft.Extensions.AI.IChatClient))
                .Append(". ");

            if (nullPrompt.ExecutionSettings is not null)
            {
                
                var serviceIds = string.Join("|", nullPrompt.ExecutionSettings.Keys);

                if (!string.IsNullOrEmpty(serviceIds))
                {
                    message.Append(" Expected serviceIds: ")
                        .Append(serviceIds)
                        .Append(". ");
                }

                var modelIds = string.Join("|", nullPrompt.ExecutionSettings.Values.Select(model => model.ModelId));
                if (!string.IsNullOrEmpty(modelIds))
                {
                    message.Append(" Expected modelIds: ")
                        .Append(modelIds)
                        .Append(". ");
                }
            }
            
            throw new KernelException(message.ToString());
        }
        
        return (chatCompletionService, executionSettings);
    }

    private async Task<ChatHistory> SetupAgentChatHistoryAsync(
        IReadOnlyList<ChatMessageContent> history,
        KernelArguments? arguments,
        Kernel kernel,
        string? additionalInstructions,
        CancellationToken cancellationToken)
    {
        ChatHistory chat = [];

        var instructions = await RenderInstructionsAsync(
            kernel, arguments, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            chat.Add(new ChatMessageContent(InstructionsRole, instructions)
            {
                AuthorName = Name
            });
        }

        if (!string.IsNullOrWhiteSpace(additionalInstructions))
        {
            chat.Add(new ChatMessageContent(AuthorRole.System, 
                additionalInstructions)
            {
                AuthorName = Name
            });
        }
        
        chat.AddRange(history);

        return chat;
    }
    
    private async IAsyncEnumerable<ChatMessageContent> InternalInvokeAsync(
        string agentName,
        ChatHistory history,
        Func<ChatMessageContent, Task> onNewToolMessage,
        KernelArguments? arguments = null,
        Kernel? kernel = null,
        string? additionalInstructions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        kernel ??= Kernel;
        var (chatCompletionService, executionSettings) =
            GetChatCompletionService(
                kernel,
                Arguments.MergeArguments(arguments));
        
        var chat = await SetupAgentChatHistoryAsync(
            history,
            arguments,
            kernel,
            additionalInstructions,
            cancellationToken)
            .ConfigureAwait(false);
        
        var messageCount = chat.Count;
        var serviceType = chatCompletionService.GetType();
        
        Logger.LogTrace(
            "Invoking agent {AgentName} with service {ServiceType} in {MethodName} for agent {AgentId}",
            nameof(InvokeAsync),
            Id,
            agentName,
            serviceType);
        
        var messages =
            await chatCompletionService.GetChatMessageContentsAsync(
                chat,
                executionSettings,
                kernel,
                cancellationToken)
            .ConfigureAwait(false);
        Logger.LogTrace(
            "Invoked agent {AgentName} with service {ServiceType} in {MethodName} for agent {AgentId}",
            nameof(InvokeAsync),
            Id,
            agentName,
            serviceType);
        
        for (var messageIndex = messageCount;
             messageIndex < chat.Count; 
             messageIndex++)
        {
            var message = chat[messageIndex];
            message.AuthorName = Name;
            history.Add(message);
            await onNewToolMessage(message).ConfigureAwait(false);
        }

        foreach (var message in messages)
        {
            message.AuthorName = Name;
            yield return message;
        }
    }
    

    private async IAsyncEnumerable<StreamingChatMessageContent> InternalInvokeStreamingAsync(
        string agentName,
        ChatHistory history,
        Func<ChatMessageContent, Task> onNewMessage,
        KernelArguments? arguments = null,
        Kernel? kernel = null,
        string? additionalInstructions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        kernel ??= Kernel;

        var (chatCompletionService, executionSettings) =
            GetChatCompletionService(
                kernel,
                Arguments.MergeArguments(arguments));
        
        var chat = await SetupAgentChatHistoryAsync(
            history,
            arguments,
            kernel,
            additionalInstructions,
            cancellationToken)
            .ConfigureAwait(false);
        
        var messageCount = chat.Count;
        var serviceType = chatCompletionService.GetType();

        Logger.LogTrace(
            "Invoking agent {AgentName} with service {ServiceType} in {MethodName} for agent {AgentId}",
            nameof(InvokeAsync),
            Id,
            agentName,
            serviceType);

        var messages =
            chatCompletionService.GetStreamingChatMessageContentsAsync(
                chat,
                executionSettings,
                kernel,
                cancellationToken);
        
        Logger.LogTrace(
            "Invoked agent {AgentName} with service {ServiceType} in {MethodName} for agent {AgentId}",
            nameof(InvokeAsync), Id, agentName, serviceType);

        var messageIndex = messageCount;
        AuthorRole? role = null;
        StringBuilder builder = new();

        await foreach (var message in messages.ConfigureAwait(false))
        {
            role = message.Role;
            message.Role ??= AuthorRole.Assistant;
            message.AuthorName = Name;

            builder.Append(message);

            for (; messageIndex < chat.Count; messageIndex++)
            {
                var chatMessage = chat[messageIndex];

                chatMessage.AuthorName = Name;
                
                await onNewMessage(chatMessage).ConfigureAwait(false);
                history.Add(chatMessage);
            }

            yield return message;
        }

        if (role != AuthorRole.Tool)
        {
            await onNewMessage(new(role ?? AuthorRole.Assistant, builder.ToString())
            {
                AuthorName = Name
            }).ConfigureAwait(false);
            
            history.Add(new (role ?? AuthorRole.Assistant,
                builder.ToString())
            {
                AuthorName = Name
            });
        }
    }
}