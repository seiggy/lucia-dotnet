using A2A;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.Logging;
using lucia.Agents.Skills;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent that controls Satellite1 music playback through the Home Assistant Music Assistant integration.
/// </summary>
public class MusicAgent
{
    private readonly AgentCard _agent;
    private readonly MusicPlaybackSkill _musicSkill;
    private readonly ILogger<MusicAgent> _logger;
    private readonly TaskManager _taskManager;
    private AIAgent _aiAgent;

    public MusicAgent(
        IChatClient chatClient,
        MusicPlaybackSkill musicSkill,
        ILoggerFactory loggerFactory)
    {
        _musicSkill = musicSkill;
        _logger = loggerFactory.CreateLogger<MusicAgent>();

        var musicControlSkill = new AgentSkill
        {
            Id = "id_music_agent",
            Name = "MusicPlayback",
            Description = "Skillset for controlling Satellite1 Music Assistant playback across artists, albums, genres, and tracks",
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
                "Play the album Random Access Memories on the Satellite1 office speaker",
                "Play the song Shivers by Ed Sheeran on Satellite1 bedroom"
            ]
        };

        _agent = new AgentCard
        {
            Url = "/a2a/music-agent",
            Name = "music-agent",
            Description = "Agent that orchestrates Music Assistant playback on Satellite1 endpoints",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                StateTransitionHistory = true,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [musicControlSkill],
            Version = "1.0.0"
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
            """;

        var agentOptions = new ChatClientAgentOptions(instructions)
        {
            Id = "music-agent",
            Name = "music-agent",
            Description = "Handles music playback for Satellite1 speakers",
            ChatOptions = new()
            {
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
        await _musicSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MusicAgent initialized successfully");
    }
}
