using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Mcp;
using lucia.Agents.Orchestration;
using lucia.Agents.Services;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Specialized agent that creates timed announcements on Home Assistant assist satellite devices.
/// </summary>
public sealed class TimerAgent : ILuciaAgent
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.Timer", "1.0.0");

    private readonly AgentCard _agent;
    private readonly ILogger<TimerAgent> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly IServer _server;
    private volatile AIAgent _aiAgent;
    private string? _lastModelConnectionName;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    public TimerAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        TimerSkill timerSkill,
        AlarmSkill alarmSkill,
        IServer server,
        IConfiguration configuration,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TimerAgent>();
        _server = server;
        _configuration = configuration;

        var timerControlSkill = new AgentSkill
        {
            Id = "id_timer_agent",
            Name = "TimerControl",
            Description = "Skill for setting timed announcements and reminders on Home Assistant satellite devices",
            Tags = ["timer", "reminder", "alarm", "announcement", "tts", "satellite"],
            Examples =
            [
                "Set a timer for 5 minutes",
                "Remind me in 30 minutes that dinner is ready",
                "Set a 10-minute pizza timer",
                "Cancel my timer",
                "What timers are active?"
            ]
        };

        var alarmControlSkill = new AgentSkill
        {
            Id = "id_alarm_agent",
            Name = "AlarmControl",
            Description = "Skill for setting, dismissing, and snoozing alarm clocks on Home Assistant media player devices",
            Tags = ["alarm", "wake", "schedule", "cron", "recurring", "media_player"],
            Examples =
            [
                "Set an alarm for 7 AM in the bedroom",
                "Wake me up at 6:30 on weekdays",
                "Dismiss my alarm",
                "Snooze for 5 minutes",
                "What alarms do I have?"
            ]
        };

        _agent = new AgentCard
        {
            Url = "pending",
            Name = "timer-agent",
            Description = "Agent that manages #timers, #alarms, and #reminders on Home Assistant devices using TTS and media playback",
            DocumentationUrl = "https://github.com/seiggy/lucia-dotnet/",
            IconUrl = "https://github.com/seiggy/lucia-dotnet/blob/master/lucia.png?raw=true",
            SupportsAuthenticatedExtendedCard = false,
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                StateTransitionHistory = true,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [timerControlSkill, alarmControlSkill],
            Version = "1.0.0",
            ProtocolVersion = "0.2.5"
        };

        var instructions = """
            You are Lucia's Timer & Alarm Agent. You handle two categories of time-based tasks:

            ## Timers (countdown-based)
            - Parse timer duration from user requests (e.g. "5 minutes", "1 hour and 30 minutes", "90 seconds").
            - Create a friendly announcement message based on what the user is timing.
            - Use the SetTimer tool with the parsed duration (in seconds), the announcement message, and the entity_id.
            - List active timers when asked.
            - Cancel timers when requested.

            ## Alarms (wall-clock time-based)
            - Set alarms for specific times (e.g., "7 AM", "6:30").
            - Support recurring alarms via CRON schedules (e.g., weekdays at 7 AM → "0 7 * * 1-5").
            - Dismiss ringing alarms or disable scheduled alarms.
            - Snooze ringing alarms (default 9 minutes).
            - List all configured alarms.
            - Alarms play on media_player devices, not TTS satellites.
            - Use "presence" as location if the user wants the alarm to play wherever they are.

            ## Choosing Timer vs Alarm
            - "Set a timer for X minutes" → use SetTimer (countdown from now)
            - "Set an alarm for 7 AM" → use SetAlarm (fires at wall-clock time)
            - "Wake me up at 6:30" → use SetAlarm
            - "Remind me in 30 minutes" → use SetTimer
            - "Set a daily alarm for 7 AM on weekdays" → use SetAlarm with cronSchedule "0 7 * * 1-5"

            The entity_id or location will be provided in the request context.
            If no location is available, ask the user which device to use.

            ## IMPORTANT
            * Keep responses short and confirmatory.
            * Do not offer additional assistance after setting a timer or alarm.
            * If you need clarification, ask concisely.
            * For alarm times, use 24-hour HH:mm format when calling SetAlarm.
            """;

        Instructions = instructions;

        // Merge both skill tool sets
        Tools = [.. timerSkill.GetTools(), .. alarmSkill.GetTools()];

        var agentOptions = new ChatClientAgentOptions
        {
            Id = "timer-agent",
            Name = "timer-agent",
            Description = "Sets timed announcements on satellite devices",
            ChatOptions = new()
            {
                Instructions = Instructions,
                Tools = Tools
            }
        };

        // _aiAgent is built during InitializeAsync via ApplyDefinitionAsync
        _aiAgent = null!;
    }

    public AgentCard GetAgentCard() => _agent;

    public AIAgent GetAIAgent() => _aiAgent;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TimerAgent.Initialize", ActivityKind.Internal);
        activity?.SetTag("agent.id", "timer-agent");
        _logger.LogInformation("Initializing TimerAgent...");

        var selfUrl = _configuration["services:selfUrl"];
        if (!string.IsNullOrWhiteSpace(selfUrl))
        {
            _agent.Url = selfUrl;
        }
        else
        {
            var addressesFeature = _server?.Features?.Get<IServerAddressesFeature>();
            if (addressesFeature?.Addresses != null && addressesFeature.Addresses.Any())
            {
                _agent.Url = addressesFeature.Addresses.First();
            }
            else
            {
                _agent.Url = "unknown";
            }
        }

        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("TimerAgent initialized successfully");
    }

    public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDefinitionAsync(CancellationToken cancellationToken)
    {
        var definition = await _definitionRepository.GetAgentDefinitionAsync("timer-agent", cancellationToken).ConfigureAwait(false);
        var newConnectionName = definition?.ModelConnectionName;

        if (_aiAgent is not null && string.Equals(_lastModelConnectionName, newConnectionName, StringComparison.Ordinal))
            return;

        var client = await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
        _aiAgent = BuildAgent(client);
        _logger.LogInformation("TimerAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
        _lastModelConnectionName = newConnectionName;
    }

    private ChatClientAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, "timer-agent");
        var agentOptions = new ChatClientAgentOptions
        {
            Id = "timer-agent",
            Name = "timer-agent",
            Description = "Sets timed announcements on satellite devices",
            ChatOptions = new()
            {
                Instructions = Instructions,
                Tools = Tools
            }
        };
        return new ChatClientAgent(traced, agentOptions, _loggerFactory);
    }
}
