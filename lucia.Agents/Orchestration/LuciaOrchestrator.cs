using A2A;
using lucia.Agents.Registry;
using lucia.Agents.Skills;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern
/// </summary>
public class LuciaOrchestrator
{
    private readonly AgentCard _agent;
    private readonly AgentRegistry _agentRegistry;
    private readonly ILogger<LuciaOrchestrator> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TaskManager _taskManager;
    private readonly AIAgent _aiAgent;
    private readonly Workflow<List<ChatMessage>> _workflow;
    private readonly AIAgent _workflowAgent;

    public LuciaOrchestrator(
        IChatClient chatClient,
        AgentRegistry agentRegistry,
        AgentCatalog agentCatalog,
        IHttpClientFactory httpClientFactory,
        ILogger<LuciaOrchestrator> logger)
    {
        _agentRegistry = agentRegistry;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        _agent = new AgentCard
        {
            Url = "/a2a/lucia-orchestrator",
            Name = "lucia-orchestrator",
            Description = "Agent responsible for orchestrating workflow with multiple agents.",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                StateTransitionHistory = true,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Version = "1.0.0",
        };

        var instructions = $"""
            You are a smart home manager who has a collection of agents that can help you control and manage a connected smart home.
            I will provide you information about my smart home below, and a collection of available agents with their abilities, 
            use this information to answer the user's question or perform actions requested by the user.

            Current Date and Time: {DateTimeOffset.Now.ToString("G")}

            Available Agents:
            | agent_name | description | url |
            | ---------- | ----------- | --- |
            {RenderAgentMarkdownTable(CancellationToken.None).GetAwaiter().GetResult()}

            ## Rules:
            * Do not tell me what you're thinking about doing, just do it.
            * If I ask you about the current state of the home, or many devices I have, or how many devices are in a specific state, 
                use the available agents to get states of the respective devices before answering.
            * If I ask you what time or date it is be sure to respond in a format 
                that will work best for text-to-speech engines such as Piper.
            * If you don't have enough information to execute a smart home command then specify what other information you need.
            * You can ask the user for more information by ending your response with a '?'. If you end your response with any other punctuation,
                the Home Assistant solution will assume you have completed the request. Keep this in mind when responding.
            """;

        _aiAgent = chatClient.CreateAIAgent(
            instructions: instructions, name: "lucia-orchestrator");
        var luciaExecutor = new LuciaExecutor(_aiAgent);

        var agents = agentRegistry.GetAgentsAsync().ToListAsync().GetAwaiter().GetResult();

        var aiAgents = agentCatalog.GetAgentsAsync().ToListAsync().GetAwaiter().GetResult();


        var agentOptions = new ChatClientAgentOptions(instructions)
        {
            Id = "lucia-orchestrator",
            Name = "lucia-orchestrator",
            Description = "Agent responsible for orchestrating workflow with multiple agents.",
        };

        
        _taskManager = new TaskManager();
    }

    /// <summary>
    /// Process a user request through the MagenticOne multi-agent system
    /// </summary>
    public async Task<string> ProcessRequestAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user request: {Request}", userRequest);
        
        try
        {
            // Get available agents from registry
            var availableAgents = _agentRegistry.GetAgentsAsync(cancellationToken);
            _logger.LogDebug("Found {AgentCount} available agents", await availableAgents.CountAsync(cancellationToken: cancellationToken));

            if (!await availableAgents.AnyAsync())
            {
                _logger.LogWarning("No agents available to process request");
                return "I don't have any specialized agents available right now. Please try again later.";
            }

            // Create MagenticOne orchestration with available agents
            
            // Add user request to history
            
            // start the runtime

            // Invoke the orchestration with the user request
            
            throw new NotImplementedException("Orchestration logic not implemented yet");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);
            return "I encountered an error while processing your request. Please try again.";
        }
        finally
        {
            // runtime cleanup if needed
        }
    }

    /// <summary>
    /// Get the current status of the orchestrator
    /// </summary>
    public async Task<OrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var agents = _agentRegistry.GetAgentsAsync(cancellationToken);
        
        return new OrchestratorStatus
        {
            IsReady = await agents.AnyAsync(cancellationToken),
            AvailableAgentCount = await agents.CountAsync(cancellationToken),
            AvailableAgents = await agents.ToListAsync(cancellationToken)
        };
    }

    /// <summary>
    /// Clear the chat history
    /// </summary>
    public void ClearHistory()
    {
        _logger.LogInformation("Chat history cleared");
        throw new NotImplementedException("Chat history management not implemented yet");
    }


    private async Task<string> RenderAgentMarkdownTable(CancellationToken cancellationToken)
    {
        var agents = _agentRegistry.GetAgentsAsync(cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder();

        await foreach (var agent in agents)
        {
            sb.Append($"|{agent.Name}|{agent.Description}|{agent.Url}|");
        }
        return sb.ToString();
    }
}