#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Azure.AI.OpenAI;
using Azure.Identity;
using FakeItEasy;
using lucia.Agents.Agents;
using lucia.Agents.Mcp;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using lucia.Agents.Skills;
using lucia.Agents.Services;
using lucia.HomeAssistant.Services;
using lucia.MusicAgent;
using lucia.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using A2A;
using lucia.Agents.Abstractions;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Shared test fixture for evaluation tests. Loads configuration from
/// <c>appsettings.json</c> (with environment variable overrides), creates
/// LLM clients for configured providers (Azure OpenAI, Ollama, OpenAI, etc.),
/// and provides factory methods that construct real agent instances backed by
/// eval-model <see cref="IChatClient"/>s — ensuring eval tests exercise the
/// actual agent code paths. The judge evaluator always uses Azure OpenAI.
/// </summary>
public sealed class EvalTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Typed eval configuration loaded from <c>appsettings.json</c> with env var overrides.
    /// </summary>
    public EvalConfiguration Configuration { get; private set; } = new();

    /// <summary>
    /// Azure OpenAI client for creating per-deployment chat clients.
    /// </summary>
    public OpenAIClient AzureClient { get; private set; } = null!;

    /// <summary>
    /// Judge chat client used by LLM-based evaluators.
    /// </summary>
    public IChatClient JudgeChatClient { get; private set; } = null!;

    /// <summary>
    /// Chat configuration for the judge model, used by evaluators.
    /// </summary>
    public ChatConfiguration JudgeChatConfiguration { get; private set; } = null!;

    // --- Shared faked dependencies for agent construction ---

    private IHomeAssistantClient _mockHaClient = null!;
    private IEmbeddingGenerator<string, Embedding<float>> _mockEmbedding = null!;
    private IEmbeddingProviderResolver _mockEmbeddingResolver = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IServer _mockServer = null!;
    private readonly IDeviceCacheService _mockDeviceCache = A.Fake<IDeviceCacheService>();
    private readonly IEntityLocationService _mockLocationService = A.Fake<IEntityLocationService>();
    private readonly IEmbeddingSimilarityService _mockSimilarity = A.Fake<IEmbeddingSimilarityService>();
    private readonly IChatClientResolver _mockChatClientResolver = A.Fake<IChatClientResolver>();
    private readonly IAgentDefinitionRepository _mockDefinitionRepo = A.Fake<IAgentDefinitionRepository>();
    private TracingChatClientFactory _tracingFactory = null!;

    private static IOptionsMonitor<MusicAssistantConfig> CreateMusicAssistantOptionsMonitor()
    {
        var monitor = A.Fake<IOptionsMonitor<MusicAssistantConfig>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new MusicAssistantConfig());
        return monitor;
    }

    // --- Agent cards for registry (extracted once) ---

    private AgentCard _lightAgentCard = null!;
    private AgentCard _musicAgentCard = null!;
    private AgentCard _generalAgentCard = null!;

    public Task InitializeAsync()
    {
        // Build configuration: appsettings.json → environment variables (override)
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        var configRoot = configBuilder.Build();
        Configuration = new EvalConfiguration();
        configRoot.GetSection("EvalConfiguration").Bind(Configuration);

        // Validate endpoint is configured (JSON or env var)
        var endpoint = Configuration.AzureOpenAI.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new Xunit.SkipException(
                "Azure OpenAI endpoint is not configured. Set EvalConfiguration:AzureOpenAI:Endpoint " +
                "in appsettings.json or the EvalConfiguration__AzureOpenAI__Endpoint environment variable.");
        }

        AzureClient = Configuration.AzureOpenAI.ApiKey is not null
            ? new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(Configuration.AzureOpenAI.ApiKey))
            : new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());

        JudgeChatClient = AzureClient.GetChatClient(Configuration.JudgeModel).AsIChatClient();
        JudgeChatConfiguration = new ChatConfiguration(JudgeChatClient);

        // Initialize shared dependencies — snapshot-backed fakes for HA and embeddings
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json");
        _mockHaClient = File.Exists(snapshotPath)
            ? FakeHomeAssistantClient.FromSnapshotFile(snapshotPath)
            : A.Fake<IHomeAssistantClient>();
        _mockEmbedding = new DeterministicEmbeddingGenerator();
        _mockEmbeddingResolver = new StubEmbeddingProviderResolver(_mockEmbedding);
        _loggerFactory = LoggerFactory.Create(_ => { });
        _tracingFactory = new TracingChatClientFactory(
            A.Fake<lucia.Agents.Training.ITraceRepository>(), _loggerFactory);
        _mockServer = A.Fake<IServer>();

        // Extract agent cards once (used for orchestrator registry)
        ExtractAgentCards();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for the given deployment name,
    /// wrapped with <see cref="FunctionInvokingChatClient"/> so that tool
    /// calls are actually executed during agent runs.
    /// </summary>
    public IChatClient CreateFunctionInvokingChatClient(string deploymentName)
    {
        return new ChatClientBuilder(CreateBaseChatClient(deploymentName))
            .UseFunctionInvocation()
            .Build();
    }

    /// <summary>
    /// Creates a raw <see cref="IChatClient"/> without function invocation middleware.
    /// Used for the orchestrator's router which needs structured JSON output, not tool execution.
    /// </summary>
    public IChatClient CreateRawChatClient(string deploymentName)
    {
        return CreateBaseChatClient(deploymentName);
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> pipeline with a <see cref="ChatHistoryCapture"/>
    /// layer that records raw model responses (including tool calls) before they are
    /// processed by <see cref="FunctionInvokingChatClient"/>.
    /// <para>
    /// Pipeline: <c>FunctionInvokingChatClient → ChatHistoryCapture → LLM Provider</c>
    /// </para>
    /// </summary>
    public (IChatClient ChatClient, ChatHistoryCapture Capture) CreateCapturingChatClient(string deploymentName)
    {
        var capture = new ChatHistoryCapture(CreateBaseChatClient(deploymentName));
        var chatClient = new ChatClientBuilder(capture)
            .UseFunctionInvocation()
            .Build();
        return (chatClient, capture);
    }

    // ─── Provider-Aware Chat Client Factory ───────────────────────────

    /// <summary>
    /// Resolves the <see cref="EvalModelConfig"/> for the given deployment name
    /// and creates the appropriate <see cref="IChatClient"/> based on the configured provider.
    /// Falls back to Azure OpenAI when no explicit provider is set.
    /// </summary>
    private IChatClient CreateBaseChatClient(string deploymentName)
    {
        var model = Configuration.Models
            .FirstOrDefault(m => string.Equals(m.DeploymentName, deploymentName, StringComparison.OrdinalIgnoreCase));

        return CreateBaseChatClient(model ?? new EvalModelConfig { DeploymentName = deploymentName });
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for the given model configuration,
    /// dispatching to the correct provider SDK.
    /// </summary>
    private IChatClient CreateBaseChatClient(EvalModelConfig model)
    {
        var provider = model.Provider ?? EvalProviderType.AzureOpenAI;

        return provider switch
        {
            EvalProviderType.AzureOpenAI => AzureClient.GetChatClient(model.DeploymentName).AsIChatClient(),
            EvalProviderType.Ollama => CreateOllamaChatClient(model),
            EvalProviderType.OpenAI => CreateOpenAIChatClient(model),
            _ => throw new NotSupportedException($"Provider type '{provider}' is not supported in eval tests.")
        };
    }

    private static IChatClient CreateOllamaChatClient(EvalModelConfig model)
    {
        var endpoint = new Uri(model.Endpoint ?? "http://localhost:11434");
        return new OllamaApiClient(endpoint, model.DeploymentName);
    }

    private static IChatClient CreateOpenAIChatClient(EvalModelConfig model)
    {
        var apiKey = model.ApiKey ?? throw new InvalidOperationException(
            $"OpenAI provider for model '{model.DeploymentName}' requires an API key. " +
            "Set ApiKey in the model configuration.");

        var credential = new System.ClientModel.ApiKeyCredential(apiKey);
        var options = new OpenAI.OpenAIClientOptions();

        if (!string.IsNullOrWhiteSpace(model.Endpoint))
            options.Endpoint = new Uri(model.Endpoint);

        var client = new OpenAI.OpenAIClient(credential, options);
        return client.GetChatClient(model.DeploymentName).AsIChatClient();
    }

    // ─── Agent Factories ──────────────────────────────────────────────

    /// <summary>
    /// Creates a per-agent <see cref="IChatClientResolver"/> configured to return
    /// the given chat client for any connection name.
    /// </summary>
    private static IChatClientResolver CreateAgentResolver(IChatClient chatClient)
    {
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(chatClient);
        return resolver;
    }

    /// <summary>
    /// Creates a real <see cref="LightAgent"/> backed by the given deployment,
    /// fully initialized with its AI agent wired to the correct provider.
    /// </summary>
    public async Task<LightAgent> CreateLightAgentAsync(string deploymentName)
    {
        var resolver = CreateAgentResolver(CreateBaseChatClient(deploymentName));
        var lightSkill = new LightControlSkill(
            _mockHaClient,
            _mockEmbeddingResolver,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache,
            _mockLocationService,
            _mockSimilarity);
        var agent = new LightAgent(resolver, _mockDefinitionRepo, lightSkill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return agent;
    }

    /// <summary>
    /// Creates a real <see cref="MusicAgent"/> backed by the given deployment,
    /// fully initialized with its AI agent wired to the correct provider.
    /// </summary>
    public async Task<lucia.MusicAgent.MusicAgent> CreateMusicAgentAsync(string deploymentName)
    {
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var resolver = CreateAgentResolver(CreateBaseChatClient(deploymentName));
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient,
            musicConfig,
            _mockEmbeddingResolver,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        var agent = new lucia.MusicAgent.MusicAgent(resolver, _mockDefinitionRepo, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return agent;
    }

    /// <summary>
    /// Creates a real <see cref="LightAgent"/> with a <see cref="ChatHistoryCapture"/>
    /// that records intermediate tool calls for evaluation. The capture layer sits
    /// between the agent's tracing wrapper and the raw LLM client.
    /// </summary>
    public async Task<(LightAgent Agent, ChatHistoryCapture Capture)> CreateLightAgentWithCaptureAsync(string deploymentName)
    {
        var capture = new ChatHistoryCapture(CreateBaseChatClient(deploymentName));
        var resolver = CreateAgentResolver(capture);
        var lightSkill = new LightControlSkill(
            _mockHaClient,
            _mockEmbeddingResolver,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache,
            _mockLocationService,
            _mockSimilarity);
        var agent = new LightAgent(resolver, _mockDefinitionRepo, lightSkill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return (agent, capture);
    }

    /// <summary>
    /// Creates a real <see cref="MusicAgent"/> with a <see cref="ChatHistoryCapture"/>
    /// that records intermediate tool calls for evaluation. The capture layer sits
    /// between the agent's tracing wrapper and the raw LLM client.
    /// </summary>
    public async Task<(lucia.MusicAgent.MusicAgent Agent, ChatHistoryCapture Capture)> CreateMusicAgentWithCaptureAsync(string deploymentName)
    {
        var capture = new ChatHistoryCapture(CreateBaseChatClient(deploymentName));
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var resolver = CreateAgentResolver(capture);
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient,
            musicConfig,
            _mockEmbeddingResolver,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        var agent = new lucia.MusicAgent.MusicAgent(resolver, _mockDefinitionRepo, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return (agent, capture);
    }

    /// <summary>
    /// Creates a real <see cref="RouterExecutor"/> backed by the given deployment.
    /// </summary>
    public RouterExecutor CreateRouterExecutor(string deploymentName)
    {
        var chatClient = CreateRawChatClient(deploymentName);
        var mockRegistry = A.Fake<IAgentRegistry>();

        var allAgents = new List<AgentCard> { _lightAgentCard, _musicAgentCard, _generalAgentCard };

        A.CallTo(() => mockRegistry.GetAllAgentsAsync(A<CancellationToken>.Ignored))
            .Returns(allAgents.AsReadOnly());
        A.CallTo(() => mockRegistry.GetEnumerableAgentsAsync(A<CancellationToken>.Ignored))
            .Returns(allAgents.ToAsyncEnumerable());

        var options = Options.Create(new RouterExecutorOptions());

        return new RouterExecutor(
            chatClient,
            mockRegistry,
            _loggerFactory.CreateLogger<RouterExecutor>(),
            options);
    }

    /// <summary>
    /// Creates a fully-wired <see cref="LuciaEngine"/> that exercises
    /// the complete Router → AgentDispatch → ResultAggregator pipeline.
    /// All agents use the given <paramref name="deploymentName"/>.
    /// An optional <see cref="IOrchestratorObserver"/> is attached so eval tests
    /// can inspect intermediate routing decisions, per-agent responses, and
    /// the final aggregated result.
    /// </summary>
    public async Task<LuciaEngine> CreateLuciaOrchestratorAsync(
        string deploymentName,
        IOrchestratorObserver? observer = null)
    {
        var routerChatClient = CreateRawChatClient(deploymentName);
        var mockRegistry = A.Fake<IAgentRegistry>();

        var allCards = new List<AgentCard> { _lightAgentCard, _musicAgentCard, _generalAgentCard };

        A.CallTo(() => mockRegistry.GetAllAgentsAsync(A<CancellationToken>.Ignored))
            .Returns(allCards.AsReadOnly());
        A.CallTo(() => mockRegistry.GetEnumerableAgentsAsync(A<CancellationToken>.Ignored))
            .Returns(allCards.ToAsyncEnumerable());

        // Build real agents — all backed by the same deployment for this iteration.
        var lightAgent = await CreateLightAgentAsync(deploymentName);
        var musicAgent = await CreateMusicAgentAsync(deploymentName);

        var generalResolver = CreateAgentResolver(CreateBaseChatClient(deploymentName));
        var webSearchSkill = new WebSearchSkill(
            A.Fake<IHttpClientFactory>(),
            Options.Create(new lucia.Agents.Configuration.SearXngOptions()),
            _loggerFactory.CreateLogger<WebSearchSkill>());
        var generalAgent = new GeneralAgent(generalResolver, _mockDefinitionRepo, webSearchSkill, _tracingFactory, _loggerFactory);
        await generalAgent.InitializeAsync();

        var agentProvider = new EvalAgentProvider(
        [
            lightAgent.GetAIAgent(),
            musicAgent.GetAIAgent(),
            generalAgent.GetAIAgent()
        ]);

        var taskManager = new StubTaskManager();

        var sessionManager = new SessionManager(
            taskManager,
            _loggerFactory.CreateLogger<SessionManager>());

        // Configure mock resolver to return the router's chat client for the orchestrator
        var orchestratorResolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => orchestratorResolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(routerChatClient);

        var workflowFactory = new WorkflowFactory(
            orchestratorResolver,
            _mockDefinitionRepo,
            mockRegistry,
            A.Fake<IServiceProvider>(),
            _loggerFactory,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentInvokerOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System,
            A.Fake<IHttpClientFactory>(),
            observer,
            agentProvider);

        return new LuciaEngine(
            mockRegistry,
            sessionManager,
            workflowFactory,
            Options.Create(new ResultAggregatorOptions()),
            _loggerFactory.CreateLogger<LuciaEngine>(),
            observer);
    }

    public Task DisposeAsync()
    {
        JudgeChatClient?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extracts agent cards from real agent instances (with faked deps) so the
    /// orchestrator's mock registry returns accurate agent metadata.
    /// </summary>
    private void ExtractAgentCards()
    {
        // LightAgent card
        var lightSkill = new LightControlSkill(
            _mockHaClient, _mockEmbeddingResolver,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache, _mockLocationService, _mockSimilarity);
        var lightAgent = new LightAgent(_mockChatClientResolver, _mockDefinitionRepo, lightSkill, _tracingFactory, _loggerFactory);
        _lightAgentCard = lightAgent.GetAgentCard();

        // MusicAgent card
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient, musicConfig, _mockEmbeddingResolver,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        var musicAgent = new lucia.MusicAgent.MusicAgent(_mockChatClientResolver, _mockDefinitionRepo, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _tracingFactory, _loggerFactory);
        _musicAgentCard = musicAgent.GetAgentCard();

        // GeneralAgent card
        var webSearchSkill2 = new WebSearchSkill(
            A.Fake<IHttpClientFactory>(),
            Options.Create(new lucia.Agents.Configuration.SearXngOptions()),
            _loggerFactory.CreateLogger<WebSearchSkill>());
        var generalAgent = new GeneralAgent(_mockChatClientResolver, _mockDefinitionRepo, webSearchSkill2, _tracingFactory, _loggerFactory);
        _generalAgentCard = generalAgent.GetAgentCard();
    }
}
