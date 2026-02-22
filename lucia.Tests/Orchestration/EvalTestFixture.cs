#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Azure.AI.OpenAI;
using Azure.Identity;
using FakeItEasy;
using lucia.Agents.Agents;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using lucia.Agents.Skills;
using lucia.Agents.Services;
using lucia.HomeAssistant.Services;
using lucia.MusicAgent;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using A2A;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Shared test fixture for evaluation tests. Loads configuration from
/// <c>appsettings.json</c> (with environment variable overrides), creates
/// Azure OpenAI clients, and provides factory methods that construct real
/// agent instances backed by eval-model <see cref="IChatClient"/>s —
/// ensuring eval tests exercise the actual agent code paths.
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
    private ILoggerFactory _loggerFactory = null!;
    private IServer _mockServer = null!;
    private readonly IDeviceCacheService _mockDeviceCache = A.Fake<IDeviceCacheService>();

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
        _loggerFactory = LoggerFactory.Create(_ => { });
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
        return new ChatClientBuilder(AzureClient.GetChatClient(deploymentName).AsIChatClient())
            .UseFunctionInvocation()
            .Build();
    }

    /// <summary>
    /// Creates a raw <see cref="IChatClient"/> without function invocation middleware.
    /// Used for the orchestrator's router which needs structured JSON output, not tool execution.
    /// </summary>
    public IChatClient CreateRawChatClient(string deploymentName)
    {
        return AzureClient.GetChatClient(deploymentName).AsIChatClient();
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> pipeline with a <see cref="ChatHistoryCapture"/>
    /// layer that records raw model responses (including tool calls) before they are
    /// processed by <see cref="FunctionInvokingChatClient"/>.
    /// <para>
    /// Pipeline: <c>FunctionInvokingChatClient → ChatHistoryCapture → AzureOpenAI</c>
    /// </para>
    /// </summary>
    public (IChatClient ChatClient, ChatHistoryCapture Capture) CreateCapturingChatClient(string deploymentName)
    {
        var capture = new ChatHistoryCapture(AzureClient.GetChatClient(deploymentName).AsIChatClient());
        var chatClient = new ChatClientBuilder(capture)
            .UseFunctionInvocation()
            .Build();
        return (chatClient, capture);
    }

    // ─── Agent Factories ──────────────────────────────────────────────

    /// <summary>
    /// Creates a real <see cref="LightAgent"/> backed by the given deployment.
    /// </summary>
    public LightAgent CreateLightAgent(string deploymentName)
    {
        var chatClient = CreateFunctionInvokingChatClient(deploymentName);
        var lightSkill = new LightControlSkill(
            _mockHaClient,
            _mockEmbedding,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache);
        return new LightAgent(chatClient, lightSkill, _loggerFactory);
    }

    /// <summary>
    /// Creates a real <see cref="MusicAgent"/> backed by the given deployment.
    /// </summary>
    public lucia.MusicAgent.MusicAgent CreateMusicAgent(string deploymentName)
    {
        var chatClient = CreateFunctionInvokingChatClient(deploymentName);
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient,
            musicConfig,
            _mockEmbedding,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        return new lucia.MusicAgent.MusicAgent(chatClient, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _loggerFactory);
    }

    /// <summary>
    /// Creates a real <see cref="LightAgent"/> with a <see cref="ChatHistoryCapture"/>
    /// that records intermediate tool calls for evaluation.
    /// </summary>
    public (LightAgent Agent, ChatHistoryCapture Capture) CreateLightAgentWithCapture(string deploymentName)
    {
        var (chatClient, capture) = CreateCapturingChatClient(deploymentName);
        var lightSkill = new LightControlSkill(
            _mockHaClient,
            _mockEmbedding,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache);
        return (new LightAgent(chatClient, lightSkill, _loggerFactory), capture);
    }

    /// <summary>
    /// Creates a real <see cref="MusicAgent"/> with a <see cref="ChatHistoryCapture"/>
    /// that records intermediate tool calls for evaluation.
    /// </summary>
    public (lucia.MusicAgent.MusicAgent Agent, ChatHistoryCapture Capture) CreateMusicAgentWithCapture(string deploymentName)
    {
        var (chatClient, capture) = CreateCapturingChatClient(deploymentName);
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient,
            musicConfig,
            _mockEmbedding,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        return (new lucia.MusicAgent.MusicAgent(chatClient, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _loggerFactory), capture);
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
    public LuciaEngine CreateLuciaOrchestrator(
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
        var lightAgent = CreateLightAgent(deploymentName);
        var musicAgent = CreateMusicAgent(deploymentName);
        var generalAgent = new GeneralAgent(CreateFunctionInvokingChatClient(deploymentName), _loggerFactory);

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

        var workflowFactory = new WorkflowFactory(
            routerChatClient,
            mockRegistry,
            A.Fake<IServiceProvider>(),
            _loggerFactory,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentInvokerOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System,
            taskManager,
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
        var fakeChatClient = A.Fake<IChatClient>();

        // LightAgent card
        var lightSkill = new LightControlSkill(
            _mockHaClient, _mockEmbedding,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _mockDeviceCache);
        var lightAgent = new LightAgent(fakeChatClient, lightSkill, _loggerFactory);
        _lightAgentCard = lightAgent.GetAgentCard();

        // MusicAgent card
        var musicConfig = CreateMusicAssistantOptionsMonitor();
        var musicSkill = new MusicPlaybackSkill(
            _mockHaClient, musicConfig, _mockEmbedding,
            _mockDeviceCache,
            _loggerFactory.CreateLogger<MusicPlaybackSkill>());
        var musicAgent = new lucia.MusicAgent.MusicAgent(fakeChatClient, musicSkill, _mockServer, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), _loggerFactory);
        _musicAgentCard = musicAgent.GetAgentCard();

        // GeneralAgent card
        var generalAgent = new GeneralAgent(fakeChatClient, _loggerFactory);
        _generalAgentCard = generalAgent.GetAgentCard();
    }
}
