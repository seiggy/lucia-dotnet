using System.Diagnostics;
using A2A;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;

namespace lucia.MusicAgent;

/// <summary>
/// Specialized agent that controls Satellite1 music playback through the Home Assistant Music Assistant integration.
/// </summary>
public class MusicAgent : ILuciaAgent, ISkillConfigProvider
{
    private const string AgentId = "music-agent";
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.Music", "1.0.0");

    private readonly AgentCard _agent;
    private readonly MusicPlaybackSkill _musicSkill;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MusicAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServer _server;
    private volatile AIAgent _aiAgent;
    private DateTime? _lastConfigUpdate;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    public MusicAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        MusicPlaybackSkill musicSkill,
        IServer server,
        IConfiguration configuration,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _musicSkill = musicSkill;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MusicAgent>();
        _server = server;
        _configuration = configuration;
        var musicControlSkill = new AgentSkill
        {
            Id = "id_music_agent",
            Name = "MusicPlayback",
            Description = "Skillset for controlling Music Assistant playback across artists, albums, genres, and tracks",
            Tags = [
                "music",
                "media",
                "home assistant",
                "music assistant",
                "satellite"
            ],
            Examples =
            [
                "Play relaxed jazz on Satellite1 kitchen",
                "Shuffle Satellite1 loft",
                "Play the album Random Access Memories on the office speaker",
                "Play the song Shivers by Ed Sheeran in the bedroom",
                "Turn up the volume on the kitchen speaker",
                "Set volume to 50% on Loft",
                "Stop the music",
                "Turn off the music on the loft"
            ]
        };

        _agent = new AgentCard
        {
            Name = AgentId,
            Description = "Agent that orchestrates #Music Assistant #playback on #speaker endpoints",
            DocumentationUrl = "https://github.com/seiggy/lucia-dotnet/",
            IconUrl = "https://github.com/seiggy/lucia-dotnet/blob/master/lucia.png?raw=true",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [musicControlSkill],
            Version = "1.0.0"
        };
        _agent.SetUrl("pending"); // Updated in InitializeAsync

        var instructions = """
            You are Lucia's dedicated Music Playback Agent for Satellite1 speakers powered by Home Assistant's Music Assistant integration.

            Responsibilities:
            - Resolve media speaker endpoints by friendly name or description.
            - Play music by artist, album, genre, or specific song requests.
            - Offer shuffle and radio mixes when users ask to "just shuffle" or "play something fitting".
            - Control volume: set a specific level (0–100%), turn volume up, or turn volume down when the user asks.
            - Stop or turn off playback when the user says stop, turn off, pause, or stop the music. Use the StopMusic tool immediately; do not refuse or explain that you cannot — just call the tool (use the player name if given, otherwise a generic term like "speaker" to resolve a default).
            - Confirm the selected device, the requested media, and whether shuffle/radio mode is enabled.
            - Stay focused on music playback. For other smart home tasks, politely route to the appropriate specialist agent.

            When users refer to a Satellite speaker (e.g. "Satellite1 kitchen", "satellite loft"), locate the best matching endpoint before invoking any action.
            If the user refers to a location (e.g. "Office"), use the FindPlayer tool to search for a player that may be in that location.
            Use the FindPlayer tool to find the device the user requested to have the music played on.
            If you are unsure which endpoint to use, ask a clarifying question before starting playback. If you are at least 50% sure, just choose the endpoint you think is correct.
            If the user does not specify any details about the type of music, simply shuffle music from their library.
            
            ## IMPORTANT
            * Keep your responses short and informative only. Examples: "Shuffling some music!", "Playing 'The Hanging Garden' by 'The Cure'.", "Stopped."
            * When the user asks to stop, turn off, or pause music: call StopMusic (with the player name if given, or e.g. "speaker" to pick a default), then reply briefly e.g. "Stopped." or "Music is off." Do not say you cannot do it or ask unnecessary questions.
            * Do not offer to provide other assistance.
            * If you need to ask for user feedback, ensure your response ends in a '?'. Examples: "Did you mean the Bedroom Speaker?", "I'm sorry, I couldn't find a speaker named 'Living Room Speakers'; Is it known by another name?"
            """;

        Instructions = instructions;
        _musicSkill.AgentId = AgentId;
        Tools = _musicSkill.GetTools();

        // _aiAgent is built during InitializeAsync via ApplyDefinitionAsync
        _aiAgent = null!;
    }

    /// <summary>
    /// Provides the agent card for registration with the registry and A2A endpoints.
    /// </summary>
    public AgentCard GetAgentCard() => _agent;

    /// <inheritdoc/>
    public IReadOnlyList<SkillConfigSection> GetSkillConfigSections() =>
    [
        new()
        {
            SectionName = MusicPlaybackSkillOptions.SectionName,
            DisplayName = "Music Playback",
            OptionsType = typeof(MusicPlaybackSkillOptions)
        }
    ];

    public AIAgent GetAIAgent() => _aiAgent;

    /// <summary>
    /// Initializes the agent and primes any dependent caches.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("agent.id", AgentId);
        _logger.LogInformation("Initializing MusicAgent...");
        var selfUrl = _configuration["services:selfUrl"];
        var deploymentMode = _configuration["Deployment:Mode"];
        var isStandalone = string.IsNullOrWhiteSpace(deploymentMode)
            || deploymentMode.Equals("standalone", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(selfUrl))
        {
            // Explicit selfUrl takes precedence — used in mesh mode with Aspire service discovery
            _agent.SetUrl(selfUrl);
        }
        else if (isStandalone)
        {
            // In standalone mode, plugin runs in-process — use relative path
            _agent.SetUrl("/a2a/music-agent");
        }
        else
        {
            var addressesFeature = _server.Features.Get<IServerAddressesFeature>();
            if (addressesFeature?.Addresses != null &&  addressesFeature.Addresses.Count != 0)
            {
                _agent.SetUrl(addressesFeature.Addresses.First());
            }
            else
            {
                _agent.SetUrl("unknown");
            }
        }
        
        await _musicSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("MusicAgent initialized successfully");
        _lastConfigUpdate = DateTime.Now;
    }

    /// <inheritdoc />
    public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDefinitionAsync(CancellationToken cancellationToken)
    {
        var definition = await _definitionRepository.GetAgentDefinitionAsync(AgentId, cancellationToken).ConfigureAwait(false);
        var newConnectionName = definition?.ModelConnectionName;

        if (_lastConfigUpdate == null || _lastConfigUpdate < definition?.UpdatedAt)
        {
            var client = await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
            _aiAgent = BuildAgent(client);
            _logger.LogInformation("MusicAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
            _lastConfigUpdate = DateTime.Now;
        }
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Handles music playback for MusicAssistant",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = Tools
            }
        };

        return new ChatClientAgent(traced, agentOptions, _loggerFactory)
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
    }
}
