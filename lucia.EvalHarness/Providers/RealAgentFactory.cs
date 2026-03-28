using FakeItEasy;
using lucia.Agents;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// Describes a real lucia agent instance ready for evaluation.
/// Contains the actual agent with its real system prompt, tools, and skills —
/// only the LLM backend is swapped to Ollama.
/// </summary>
public sealed class RealAgentInstance
{
    public required string AgentName { get; init; }
    public required ILuciaAgent Agent { get; init; }
    public required string DatasetFile { get; init; }

    /// <summary>
    /// When conversation tracing is enabled, this captures the full ordered
    /// conversation history (system prompt, user, assistant, tool calls/results).
    /// Call <see cref="ConversationTracer.Reset"/> between test cases.
    /// </summary>
    public ConversationTracer? Tracer { get; init; }
}

/// <summary>
/// Constructs real lucia agent instances backed by Ollama models.
/// Mirrors the <c>EvalTestFixture</c> construction pattern: real skills, real tools,
/// real system prompts — only the <see cref="IChatClientResolver"/> is faked to return
/// an Ollama-backed <see cref="IChatClient"/> instead of Azure OpenAI.
/// </summary>
public sealed class RealAgentFactory : IAsyncDisposable
{
    private readonly string _ollamaEndpoint;
    private readonly IHomeAssistantClient _haClient;
    private readonly IEntityLocationService _locationService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAgentDefinitionRepository _definitionRepo;
    private readonly IDeviceCacheService _deviceCache;
    private readonly TracingChatClientFactory _tracingFactory;

    /// <summary>
    /// The Home Assistant client used by skills. Exposed so scenario runners
    /// can set up initial state and validate final state.
    /// </summary>
    public IHomeAssistantClient HomeAssistantClient => _haClient;

    /// <summary>
    /// When true, agents are constructed with a <see cref="ConversationTracer"/>
    /// in the chat client pipeline to capture full conversation history.
    /// </summary>
    public bool EnableTracing { get; set; }

    /// <summary>
    /// The parameter profile to use for model inference. Applied to all
    /// <see cref="OllamaApiClient"/> instances created by this factory.
    /// Defaults to <see cref="ModelParameterProfile.Default"/>.
    /// </summary>
    public ModelParameterProfile ParameterProfile { get; set; } = ModelParameterProfile.Default;

    public RealAgentFactory(string ollamaEndpoint, string haSnapshotPath, ILoggerFactory loggerFactory)
    {
        _ollamaEndpoint = ollamaEndpoint.TrimEnd('/');
        _loggerFactory = loggerFactory;

        // Load HA snapshot for skill entity resolution
        _haClient = File.Exists(haSnapshotPath)
            ? FakeHomeAssistantClient.FromSnapshotFile(haSnapshotPath)
            : A.Fake<IHomeAssistantClient>();

        // Real entity location service backed by snapshot data (simple string matching)
        _locationService = File.Exists(haSnapshotPath)
            ? SnapshotEntityLocationService.FromSnapshotFile(haSnapshotPath)
            : A.Fake<IEntityLocationService>();

        // Faked dependencies (same pattern as EvalTestFixture)
        _definitionRepo = A.Fake<IAgentDefinitionRepository>();
        _deviceCache = CreateNullDeviceCache();
        _tracingFactory = new TracingChatClientFactory(
            A.Fake<lucia.Agents.Training.ITraceRepository>(), _loggerFactory);
    }

    /// <summary>
    /// Creates a real <see cref="LightAgent"/> backed by the given Ollama model.
    /// </summary>
    public async Task<RealAgentInstance> CreateLightAgentAsync(string modelName)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        var skill = new LightControlSkill(
            _haClient,
            _loggerFactory.CreateLogger<LightControlSkill>(),
            _locationService,
            CreateOptionsMonitor<LightControlSkillOptions>());
        var agent = new LightAgent(resolver, _definitionRepo, skill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = "LightAgent", Agent = agent, DatasetFile = "TestData/light-agent.yaml", Tracer = tracer };
    }

    /// <summary>
    /// Creates a real <see cref="ClimateAgent"/> backed by the given Ollama model.
    /// </summary>
    public async Task<RealAgentInstance> CreateClimateAgentAsync(string modelName)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        var similarity = new EmbeddingSimilarityService();
        var entityMatcher = new HybridEntityMatcher(
            similarity, _loggerFactory.CreateLogger<HybridEntityMatcher>());
        var embeddingResolver = A.Fake<IEmbeddingProviderResolver>();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var climateSkill = new ClimateControlSkill(
            _haClient,
            embeddingResolver,
            _loggerFactory.CreateLogger<ClimateControlSkill>(),
            _deviceCache,
            _locationService,
            entityMatcher,
            CreateOptionsMonitor<ClimateControlSkillOptions>(),
            configuration);

        var fanSkill = new FanControlSkill(
            _haClient,
            embeddingResolver,
            _deviceCache,
            _locationService,
            entityMatcher,
            CreateOptionsMonitor<FanControlSkillOptions>(),
            _loggerFactory.CreateLogger<FanControlSkill>());

        var agent = new ClimateAgent(resolver, _definitionRepo, climateSkill, fanSkill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = "ClimateAgent", Agent = agent, DatasetFile = "TestData/climate-agent.yaml", Tracer = tracer };
    }

    /// <summary>
    /// Creates a real <see cref="ListsAgent"/> backed by the given Ollama model.
    /// </summary>
    public async Task<RealAgentInstance> CreateListsAgentAsync(string modelName)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        var skill = new ListSkill(_haClient, _loggerFactory.CreateLogger<ListSkill>());
        var agent = new ListsAgent(resolver, _definitionRepo, skill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = "ListsAgent", Agent = agent, DatasetFile = "TestData/lists-agent.yaml", Tracer = tracer };
    }

    /// <summary>
    /// Creates a real <see cref="SceneAgent"/> backed by the given Ollama model.
    /// </summary>
    public async Task<RealAgentInstance> CreateSceneAgentAsync(string modelName)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        var skill = new SceneControlSkill(
            _haClient,
            _locationService,
            CreateOptionsMonitor<SceneControlSkillOptions>(),
            _loggerFactory.CreateLogger<SceneControlSkill>());
        var agent = new SceneAgent(resolver, _definitionRepo, skill, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = "SceneAgent", Agent = agent, DatasetFile = "TestData/scene-agent.yaml", Tracer = tracer };
    }

    /// <summary>
    /// Creates a real <see cref="GeneralAgent"/> backed by the given Ollama model.
    /// </summary>
    public async Task<RealAgentInstance> CreateGeneralAgentAsync(string modelName)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        var mcpRegistry = A.Fake<IMcpToolRegistry>();
        var agent = new GeneralAgent(resolver, _definitionRepo, mcpRegistry, _tracingFactory, _loggerFactory);
        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = "GeneralAgent", Agent = agent, DatasetFile = "TestData/general-agent.yaml", Tracer = tracer };
    }

    /// <summary>
    /// Creates a real <see cref="DynamicAgent"/> backed by the given Ollama model.
    /// The agent is built from its MongoDB definition, which must exist in the repository.
    /// </summary>
    public async Task<RealAgentInstance> CreateDynamicAgentAsync(string modelName, string agentId)
    {
        var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
        
        // Load agent definition from repository
        var definition = await _definitionRepo.GetAgentDefinitionAsync(agentId, default);
        if (definition is null)
        {
            throw new InvalidOperationException($"Agent definition '{agentId}' not found in repository");
        }

        // Create mock dependencies for DynamicAgent
        var providerResolver = A.Fake<IModelProviderResolver>();
        var providerRepository = A.Fake<IModelProviderRepository>();
        var telemetrySource = new lucia.Agents.AgentsTelemetrySource();

        var agent = new DynamicAgent(
            agentId,
            definition,
            _definitionRepo,
            A.Fake<IMcpToolRegistry>(),
            resolver,
            providerResolver,
            providerRepository,
            _tracingFactory,
            telemetrySource,
            _loggerFactory);

        await agent.InitializeAsync();
        return new RealAgentInstance { AgentName = $"DynamicAgent[{agentId}]", Agent = agent, DatasetFile = $"TestData/{agentId}.yaml", Tracer = tracer };
    }

    /// <summary>
    /// All supported agent factory methods, keyed by display name.
    /// </summary>
    public IReadOnlyDictionary<string, Func<string, Task<RealAgentInstance>>> AgentFactories =>
        new Dictionary<string, Func<string, Task<RealAgentInstance>>>
        {
            ["LightAgent"] = CreateLightAgentAsync,
            ["ClimateAgent"] = CreateClimateAgentAsync,
            ["ListsAgent"] = CreateListsAgentAsync,
            ["SceneAgent"] = CreateSceneAgentAsync,
            ["GeneralAgent"] = CreateGeneralAgentAsync,
        };

    // ─── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Creates a faked <see cref="IChatClientResolver"/> that returns an Ollama
    /// <see cref="IChatClient"/> for the specified model, regardless of connection name.
    /// When <see cref="EnableTracing"/> is true, wraps the client with a
    /// <see cref="ConversationTracer"/> to capture full conversation history.
    /// </summary>
    private (IChatClientResolver Resolver, ConversationTracer? Tracer) CreateOllamaResolverWithTracer(string modelName)
    {
        IChatClient chatClient = CreateOllamaChatClient(modelName);
        ConversationTracer? tracer = null;

        if (EnableTracing)
        {
            tracer = new ConversationTracer(chatClient);
            chatClient = tracer;
        }

        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(chatClient);
        // Return null so agents fall through to the IChatClient path
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(null));
        return (resolver, tracer);
    }

    private IChatClient CreateOllamaChatClient(string modelName)
    {
        var uri = new Uri(_ollamaEndpoint);
        var ollamaClient = new OllamaApiClient(uri, modelName);

        // Wrap with parameter-injecting client that applies the profile
        return new ParameterInjectingChatClient(ollamaClient, ParameterProfile);
    }

    private static IDeviceCacheService CreateNullDeviceCache()
    {
        var fake = A.Fake<IDeviceCacheService>();
        A.CallTo(() => fake.GetCachedLightsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<LightEntity>?>(null));
        return fake;
    }

    private static IOptionsMonitor<T> CreateOptionsMonitor<T>() where T : class, new()
    {
        var monitor = A.Fake<IOptionsMonitor<T>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new T());
        return monitor;
    }

    public ValueTask DisposeAsync()
    {
        if (_haClient is IDisposable disposable)
            disposable.Dispose();
        return ValueTask.CompletedTask;
    }
}
