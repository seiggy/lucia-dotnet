using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Registry;
using lucia.Agents.Reducers;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using lucia.Agents.Agents.Extensions;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern
/// </summary>
public class LuciaOrchestrator
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly Kernel _managerKernel;
    private readonly ILogger<LuciaOrchestrator> _logger;
    private readonly StandardMagenticManager _manager;
    private readonly ChatHistory _chatHistory;
    private readonly IChatCompletionService _chatService;
    private readonly IHttpClientFactory _httpClientFactory;

    public LuciaOrchestrator(
        IAgentRegistry agentRegistry,
        Kernel managerKernel,
        IChatHistoryReducer chatHistoryReducer,
        IHttpClientFactory httpClientFactory,
        ILogger<LuciaOrchestrator> logger)
    {
        _agentRegistry = agentRegistry;
        _managerKernel = managerKernel;
        _logger = logger;
        _chatHistory = new ChatHistory();
        _httpClientFactory = httpClientFactory;
        
        // Use the provided chat history reducer for automatic token management
        var baseChatService = managerKernel.GetRequiredService<IChatCompletionService>();
        _chatService = baseChatService.UsingChatHistoryReducer(chatHistoryReducer);
        
        // Create the MagenticOne manager for orchestration
        _manager = new StandardMagenticManager(
            _chatService,
            new OpenAIPromptExecutionSettings()
            {
                MaxTokens = 2000,
                Temperature = 0.1 // Lower temperature for more consistent orchestration
            })
        {
            MaximumInvocationCount = 10, // Allow up to 10 agent interactions
        };
    }

    /// <summary>
    /// Process a user request through the MagenticOne multi-agent system
    /// </summary>
    public async Task<string> ProcessRequestAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user request: {Request}", userRequest);
        var runtime = new InProcessRuntime();
        try
        {
            // Get available agents from registry
            var availableAgents = await _agentRegistry.GetAgentsAsync(cancellationToken);
            _logger.LogDebug("Found {AgentCount} available agents", availableAgents.Count);

            if (!availableAgents.Any())
            {
                _logger.LogWarning("No agents available to process request");
                return "I don't have any specialized agents available right now. Please try again later.";
            }

            // Create MagenticOne orchestration with available agents
            var orchestration = new MagenticOrchestration(
                _manager,
                availableAgents.ToSemanticKernelAgents(_httpClientFactory))
            {
                // Capture all agent responses in chat history
                ResponseCallback = (ChatMessageContent content) =>
                {
                    _chatHistory.Add(content);
                    _logger.LogDebug("Agent {AgentName} responded: {Content}", content.AuthorName, content.Content);
                    return ValueTask.CompletedTask;
                }
            };

            // Add user request to history
            _chatHistory.AddUserMessage(userRequest);

            
            await runtime.StartAsync(cancellationToken);

            // Invoke the orchestration with the user request
            var result = await orchestration.InvokeAsync(userRequest, runtime);
            var output = await result.GetValueAsync(TimeSpan.FromSeconds(300));

            _logger.LogInformation("Request processed successfully");
            _logger.LogDebug("Orchestration history contains {MessageCount} messages", _chatHistory.Count);

            return output ?? "I was able to process your request, but didn't generate a specific response.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);
            return "I encountered an error while processing your request. Please try again.";
        }
        finally
        {
            await runtime.RunUntilIdleAsync();
        }
    }

    /// <summary>
    /// Get the current status of the orchestrator
    /// </summary>
    public async Task<OrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var agents = await _agentRegistry.GetAgentsAsync(cancellationToken);
        
        return new OrchestratorStatus
        {
            IsReady = agents.Any(),
            AvailableAgentCount = agents.Count,
            AvailableAgents = agents.ToList()
        };
    }

    /// <summary>
    /// Get the current chat history
    /// </summary>
    public ChatHistory GetChatHistory() => _chatHistory;

    /// <summary>
    /// Clear the chat history
    /// </summary>
    public void ClearHistory()
    {
        _chatHistory.Clear();
        _logger.LogInformation("Chat history cleared");
    }
}