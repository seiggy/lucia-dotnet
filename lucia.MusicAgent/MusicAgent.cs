using A2A;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.Logging;
using lucia.Agents.Skills;
using lucia.Agents.Configuration;
using lucia.Agents.Abstractions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace lucia.MusicAgent;

/// <summary>
/// Specialized agent that controls Satellite1 music playback through the Home Assistant Music Assistant integration.
/// </summary>
public class MusicAgent : IAgent
{
    private readonly AgentCard _agent;
    private readonly MusicPlaybackSkill _musicSkill;
    private readonly ILogger<MusicAgent> _logger;
    private readonly TaskManager _taskManager;
    private AIAgent _aiAgent;
    private IServer _server;

    public MusicAgent(
        IChatClient chatClient,
        MusicPlaybackSkill musicSkill,
        IServer server,
        ILoggerFactory loggerFactory)
    {
        _musicSkill = musicSkill;
        _logger = loggerFactory.CreateLogger<MusicAgent>();
        _server = server;
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
                "Play the song Shivers by Ed Sheeran int the bedroom"
            ]
        };

        _agent = new AgentCard
        {
            Url = "pending", // Set to a non-empty placeholder; updated in InitializeAsync
            Name = "music-agent",
            Description = "Agent that orchestrates #Music Assistant #playback on #speaker endpoints",
            DocumentationUrl = "https://github.com/seiggy/lucia-dotnet/",
            IconUrl = "https://github.com/seiggy/lucia-dotnet/blob/master/lucia.png?raw=true",
            SupportsAuthenticatedExtendedCard = false,
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = false,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [musicControlSkill],
            Version = "1.0.0",
            ProtocolVersion = "0.2.5"
        };

        var instructions = """
            You are Lucia's dedicated Music Playback Agent for Satellite1 speakers powered by Home Assistant's Music Assistant integration.

            Responsibilities:
            - Resolve media speaker endpoints by friendly name or description.
            - Play music by artist, album, genre, or specific song requests.
            - Offer shuffle and radio mixes when users ask to "just shuffle" or "play something fitting".
            - Confirm the selected device, the requested media, and whether shuffle/radio mode is enabled.
            - Stay focused on music playback. For other smart home tasks, politely route to the appropriate specialist agent.

            When users refer to a Satellite speaker (e.g. "Satellite1 kitchen", "satellite loft"), locate the best matching endpoint before invoking any action.
            If the user refers to a location (e.g. "Office"), use the FindPlayer tool to search for a player that may be in that location.
            Use the FindPlayer tool to find the device the user requested to have the music played on.
            If you are unsure which endpoint to use, ask a clarifying question before starting playback. If you are at least 50% sure, just choose the endpoint you think is correct.
            If the user does not specify any details about the type of music, simply shuffle music from their library.
            
            ## IMPORTANT
            * Keep your responses short and informative only. Examples: "Shuffling some music!", "Playing 'The Hanging Garden' by 'The Cure'."
            * Do not offer to provide other assistance.
            * If you need to ask for user feedback, ensure your response ends in a '?'. Examples: "Did you mean the Bedroom Speaker?", "I'm sorry, I couldn't find a speaker named 'Living Room Speakers'; Is it known by another name?"
            """;

        var agentOptions = new ChatClientAgentOptions
        {
            Id = "music-agent",
            Name = "music-agent",
            Description = "Handles music playback for MusicAssistant",
            ChatOptions = new()
            {
                Instructions = instructions,
                Tools = _musicSkill.GetTools()
            }
        };

        _aiAgent = new ChatClientAgent(chatClient, agentOptions, loggerFactory);
        _taskManager = new TaskManager();
    }

    /// <summary>
    /// Provides the agent card for registration with the registry and A2A endpoints.
    /// </summary>
    public AgentCard GetAgentCard() => _agent;
    
    public AIAgent GetAIAgent() => _aiAgent;

    /// <summary>
    /// Initializes the agent and primes any dependent caches.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing MusicAgent...");
        var addressesFeature = _server?.Features?.Get<IServerAddressesFeature>();
        if (addressesFeature?.Addresses != null && addressesFeature.Addresses.Any())
        {
            _agent.Url = addressesFeature.Addresses.First();
        }
        else
        {
            _agent.Url = "unknown";
        }
        
        await _musicSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MusicAgent initialized successfully");
    }
}
