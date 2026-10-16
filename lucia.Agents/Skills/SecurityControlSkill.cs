using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for controlling Home Assistant security devices such as alarms and locks.
/// Camera entities can be included in status responses when enabled by configuration.
/// </summary>
public sealed partial class SecurityControlSkill : ILuciaSkill, IOptimizableSkill, ICommandPatternProvider
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.SecurityControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.SecurityControl", "1.0.0");
    private static readonly Counter<long> AlarmRequests = Meter.CreateCounter<long>("security.alarm.requests", "{count}", "Number of alarm control requests.");
    private static readonly Counter<long> AlarmFailures = Meter.CreateCounter<long>("security.alarm.failures", "{count}", "Number of failed alarm control requests.");
    private static readonly Counter<long> LockRequests = Meter.CreateCounter<long>("security.lock.requests", "{count}", "Number of lock control requests.");
    private static readonly Counter<long> LockFailures = Meter.CreateCounter<long>("security.lock.failures", "{count}", "Number of failed lock control requests.");
    private static readonly Counter<long> StatusRequests = Meter.CreateCounter<long>("security.status.requests", "{count}", "Number of security status queries.");
    private static readonly Counter<long> StatusFailures = Meter.CreateCounter<long>("security.status.failures", "{count}", "Number of failed security status queries.");
    private static readonly Histogram<double> OperationDurationMs = Meter.CreateHistogram<double>("security.operation.duration", "ms", "Duration of security operations.");

    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<SecurityControlSkill> _logger;
    private readonly IEntityLocationService _locationService;
    private readonly IOptionsMonitor<SecurityControlSkillOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityControlSkill"/> class.
    /// </summary>
    public SecurityControlSkill(
        IHomeAssistantClient homeAssistantClient,
        ILogger<SecurityControlSkill> logger,
        IEntityLocationService locationService,
        IOptionsMonitor<SecurityControlSkillOptions> options)
    {
        _homeAssistantClient = homeAssistantClient;
        _logger = logger;
        _locationService = locationService;
        _options = options;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains => _options.CurrentValue.AllowedDomains;

    /// <inheritdoc/>
    public string SkillDisplayName => "Security Control";

    /// <inheritdoc/>
    public string SkillId => "security-control";

    /// <inheritdoc/>
    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<string> SearchToolNames { get; } = [nameof(ArmAlarm), nameof(DisarmAlarm), nameof(LockDoor), nameof(UnlockDoor), nameof(GetSecurityStatus), nameof(GetSecurityStateAsync), nameof(ControlCoverAsync)];

    /// <inheritdoc/>
    public string ConfigSectionName => SecurityControlSkillOptions.SectionName;

    /// <inheritdoc/>
    public IList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ArmAlarm),
            AIFunctionFactory.Create(DisarmAlarm),
            AIFunctionFactory.Create(LockDoor),
            AIFunctionFactory.Create(UnlockDoor),
            AIFunctionFactory.Create(GetSecurityStatus),
            AIFunctionFactory.Create(GetSecurityStateAsync),
            AIFunctionFactory.Create(ControlCoverAsync),
        ];
    }

    /// <inheritdoc/>
    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
    [
        new()
        {
            Id = "security-arm",
            SkillId = nameof(SecurityControlSkill),
            Action = "arm",
            Templates =
            [
                "arm [the] alarm",
                "arm [the] alarm [in] [the] {area}",
                "set [the] alarm [in] [the] {area} [to] armed",
            ],
        },
        new()
        {
            Id = "security-disarm",
            SkillId = nameof(SecurityControlSkill),
            Action = "disarm",
            Templates =
            [
                "disarm [the] alarm",
                "disarm [the] alarm [in] [the] {area}",
            ],
        },
        new()
        {
            Id = "security-lock",
            SkillId = nameof(SecurityControlSkill),
            Action = "lock",
            Templates =
            [
                "lock [the] {entity}",
                "lock [the] {entity} [in] [the] {area}",
                "lock [the] doors [in] [the] {area}",
            ],
        },
        new()
        {
            Id = "security-unlock",
            SkillId = nameof(SecurityControlSkill),
            Action = "unlock",
            Templates =
            [
                "unlock [the] {entity}",
                "unlock [the] {entity} [in] [the] {area}",
                "unlock [the] doors [in] [the] {area}",
            ],
        },
        new()
        {
            Id = "security-status",
            SkillId = nameof(SecurityControlSkill),
            Action = "status",
            Templates =
            [
                "security status",
                "what is [the] security status",
                "what is [the] security status [in] [the] {area}",
            ],
        },
    ];

    /// <inheritdoc/>
    public HybridMatchOptions GetCurrentMatchOptions()
    {
        return new HybridMatchOptions
        {
            Threshold = 0.55,
            EmbeddingWeight = 0.4,
            ScoreDropoffRatio = 0.80,
            DisagreementPenalty = 0.4,
            EmbeddingResolutionMargin = 0.10,
        };
    }

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        LogSkillInitialized(_logger);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Arms alarm control panels for the requested area.
    /// </summary>
    [Description("Arm Home Assistant alarm panels. Provide an area when available and a code only when the user supplied one.")]
    public async Task<string> ArmAlarm(
        [Description("Area name that contains the alarm panel, such as 'downstairs' or 'garage'. Leave empty to use all visible alarm panels.")] string? area,
        [Description("Optional alarm code required by some panels.")] string? code)
    {
        using var activity = ActivitySource.StartActivity(nameof(ArmAlarm));
        activity?.SetTag("security.operation", "arm_alarm");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.has_code", !string.IsNullOrWhiteSpace(code));
        AlarmRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to arm the alarm right now.", async () =>
        {
            var alarms = await ResolveEntitiesAsync(["alarm_control_panel"], area, null).ConfigureAwait(false);
            if (alarms.Count == 0)
            {
                AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return string.IsNullOrWhiteSpace(area)
                    ? "No alarm panels were found."
                    : $"No alarm panels were found for '{area}'.";
            }

            var resolvedCode = ResolveCode(code);
            LogAlarmOperationStarted(_logger, "arm", area ?? "all visible areas", alarms.Count);

            foreach (var alarm in alarms)
            {
                var request = CreateSecurityRequest(alarm.EntityId, resolvedCode);
                await _homeAssistantClient.CallServiceAsync("alarm_control_panel", "alarm_arm_away", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Armed", alarms);
        },
            onFailure: () => AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: area ?? "all visible areas",
            durationTag: new("operation", "arm_alarm")).ConfigureAwait(false);
    }

    /// <summary>
    /// Disarms alarm control panels for the requested area.
    /// </summary>
    [Description("Disarm Home Assistant alarm panels. Provide an area when available and a code only when the user supplied one.")]
    public async Task<string> DisarmAlarm(
        [Description("Area name that contains the alarm panel, such as 'downstairs' or 'garage'. Leave empty to use all visible alarm panels.")] string? area,
        [Description("Optional alarm code required by some panels.")] string? code)
    {
        using var activity = ActivitySource.StartActivity(nameof(DisarmAlarm));
        activity?.SetTag("security.operation", "disarm_alarm");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.has_code", !string.IsNullOrWhiteSpace(code));
        AlarmRequests.Add(1, [new KeyValuePair<string, object?>("action", "disarm")]);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to disarm the alarm right now.", async () =>
        {
            var alarms = await ResolveEntitiesAsync(["alarm_control_panel"], area, null).ConfigureAwait(false);
            if (alarms.Count == 0)
            {
                AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return string.IsNullOrWhiteSpace(area)
                    ? "No alarm panels were found."
                    : $"No alarm panels were found for '{area}'.";
            }

            var resolvedCode = ResolveCode(code);
            LogAlarmOperationStarted(_logger, "disarm", area ?? "all visible areas", alarms.Count);

            foreach (var alarm in alarms)
            {
                var request = CreateSecurityRequest(alarm.EntityId, resolvedCode);
                await _homeAssistantClient.CallServiceAsync("alarm_control_panel", "alarm_disarm", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Disarmed", alarms);
        },
            onFailure: () => AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: area ?? "all visible areas",
            durationTag: new("operation", "disarm_alarm")).ConfigureAwait(false);
    }

    /// <summary>
    /// Locks one or more doors.
    /// </summary>
    [Description("Lock one or more Home Assistant lock entities by area and optional entity name. Provide a code only when the user supplied one.")]
    public async Task<string> LockDoor(
        [Description("Optional area name that contains the door lock, such as 'front porch' or 'garage'.")] string? area,
        [Description("Optional lock name, such as 'front door' or 'garage entry'.")] string? entityName,
        [Description("Optional lock code required by some locks.")] string? code = null)
    {
        if (string.IsNullOrWhiteSpace(area) && string.IsNullOrWhiteSpace(entityName))
        {
            return "Please specify which door or area to lock.";
        }

        using var activity = ActivitySource.StartActivity(nameof(LockDoor));
        activity?.SetTag("security.operation", "lock_door");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.entity_name", entityName);
        activity?.SetTag("security.has_code", !string.IsNullOrWhiteSpace(code));
        LockRequests.Add(1, [new KeyValuePair<string, object?>("action", "lock")]);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to lock the requested door right now.", async () =>
        {
            var locks = await ResolveEntitiesAsync(["lock"], area, entityName).ConfigureAwait(false);
            if (locks.Count == 0)
            {
                LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return BuildNoMatchResponse("locks", area, entityName);
            }

            var resolvedCode = ResolveLockCode(code);
            LogLockOperationStarted(_logger, "lock", BuildTarget(area, entityName), locks.Count);

            foreach (var lockEntity in locks)
            {
                var request = CreateSecurityRequest(lockEntity.EntityId, resolvedCode);
                await _homeAssistantClient.CallServiceAsync("lock", "lock", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Locked", locks);
        },
            onFailure: () => LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: BuildTarget(area, entityName),
            durationTag: new("operation", "lock_door")).ConfigureAwait(false);
    }

    /// <summary>
    /// Unlocks one or more doors.
    /// </summary>
    [Description("Unlock one or more Home Assistant lock entities by area and optional entity name. Provide a code only when the user supplied one.")]
    public async Task<string> UnlockDoor(
        [Description("Optional area name that contains the door lock, such as 'front porch' or 'garage'.")] string? area,
        [Description("Optional lock name, such as 'front door' or 'garage entry'.")] string? entityName,
        [Description("Optional lock code required by some locks.")] string? code)
    {
        if (string.IsNullOrWhiteSpace(area) && string.IsNullOrWhiteSpace(entityName))
        {
            return "Please specify which door or area to unlock.";
        }

        using var activity = ActivitySource.StartActivity(nameof(UnlockDoor));
        activity?.SetTag("security.operation", "unlock_door");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.entity_name", entityName);
        activity?.SetTag("security.has_code", !string.IsNullOrWhiteSpace(code));
        LockRequests.Add(1, [new KeyValuePair<string, object?>("action", "unlock")]);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to unlock the requested door right now.", async () =>
        {
            var locks = await ResolveEntitiesAsync(["lock"], area, entityName).ConfigureAwait(false);
            if (locks.Count == 0)
            {
                LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return BuildNoMatchResponse("locks", area, entityName);
            }

            var resolvedCode = ResolveLockCode(code);
            LogLockOperationStarted(_logger, "unlock", BuildTarget(area, entityName), locks.Count);

            foreach (var lockEntity in locks)
            {
                var request = CreateSecurityRequest(lockEntity.EntityId, resolvedCode);
                await _homeAssistantClient.CallServiceAsync("lock", "unlock", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Unlocked", locks);
        },
            onFailure: () => LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: BuildTarget(area, entityName),
            durationTag: new("operation", "unlock_door")).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current security status for alarms, locks, and optionally cameras.
    /// </summary>
    [Description("Get the current status of alarm panels, locks, and configured cameras for an area. Leave area empty for a whole-home summary.")]
    public async Task<string> GetSecurityStatus(
        [Description("Optional area name to filter security devices, such as 'downstairs' or 'front porch'.")] string? area)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetSecurityStatus));
        activity?.SetTag("security.operation", "get_status");
        activity?.SetTag("security.area", area);
        StatusRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to retrieve security status right now.", async () =>
        {
            var statusDomains = GetStatusDomains();
            var entities = await ResolveEntitiesAsync(statusDomains, area, null).ConfigureAwait(false);
            if (entities.Count == 0)
            {
                StatusFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return string.IsNullOrWhiteSpace(area)
                    ? "No security devices were found."
                    : $"No security devices were found for '{area}'.";
            }

            var orderedEntities = entities
                .OrderBy(entity => entity.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entity => entity.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine(string.IsNullOrWhiteSpace(area)
                ? "Security status:"
                : $"Security status for {area}:");

            foreach (var entity in orderedEntities)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(entity.EntityId).ConfigureAwait(false);
                if (state is null)
                {
                    continue;
                }

                builder.AppendLine($"- {entity.FriendlyName}: {FormatStatus(entity, state, string.IsNullOrWhiteSpace(area))}");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return builder.ToString().TrimEnd();
        },
            onFailure: () => StatusFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: area ?? "all visible areas",
            durationTag: new("operation", "get_status")).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current state of security devices for an area, or for a single named device.
    /// </summary>
    [Description("Get the current state of alarm panels, doors, locks, and garage doors. Provide an entity name to target one device (e.g., 'front door'), or an area to cover all devices in it. Leave both empty for a whole-home summary.")]
    public async Task<string> GetSecurityStateAsync(
        [Description("Optional area name to filter security devices, such as 'garage' or 'downstairs'.")] string? area,
        [Description("Optional device name to target one security entity, such as 'front door' or 'garage doors'.")] string? entityName)
    {
        using var activity = ActivitySource.StartActivity(nameof(GetSecurityStateAsync));
        activity?.SetTag("security.operation", "get_state");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.entity", entityName);
        StatusRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to retrieve security state right now.", async () =>
        {
            var entities = await ResolveEntitiesAsync(GetStatusDomains(), area, entityName).ConfigureAwait(false);
            if (entities.Count == 0)
            {
                StatusFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return BuildNoMatchResponse("security device", area, entityName);
            }

            var orderedEntities = entities
                .OrderBy(entity => entity.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entity => entity.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine(string.IsNullOrWhiteSpace(area) && string.IsNullOrWhiteSpace(entityName)
                ? "Security status:"
                : "Security state:");

            foreach (var entity in orderedEntities)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(entity.EntityId).ConfigureAwait(false);
                if (state is null)
                {
                    continue;
                }

                builder.AppendLine($"- {entity.FriendlyName}: {FormatStatus(entity, state, string.IsNullOrWhiteSpace(area))}");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return builder.ToString().TrimEnd();
        },
            onFailure: () => StatusFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]),
            duration: OperationDurationMs,
            detail: area ?? entityName ?? "all visible areas",
            durationTag: new("operation", "get_state")).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens, closes, or stops cover entities such as garage doors. A position value applies a
    /// partial movement: 'open' targets that openness percentage, 'close' targets its complement.
    /// </summary>
    [Description("Open, close, or stop cover entities such as garage doors. Use position for partial movement (0-100 openness).")]
    public async Task<string> ControlCoverAsync(
        [Description("Search terms to find covers (e.g., ['garage doors', 'cover.garage_left'])")] string[] searchTerms,
        [Description("Desired action: 'open', 'close', or 'stop'")] string command,
        [Description("Optional target openness 0-100 for partial open/close; omit for full movement")] int? position = null,
        bool preResolved = false)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.terms", string.Join(", ", searchTerms));
        activity?.SetTag("cover.command", command);
        activity?.SetTag("pre_resolved", preResolved);

        var normalizedCommand = command.ToLowerInvariant();
        if (normalizedCommand is not ("open" or "close" or "stop"))
        {
            return $"Invalid cover command '{command}'. Use 'open', 'close', or 'stop'.";
        }

        try
        {
            var allEntities = new Dictionary<string, HomeAssistantEntity>(StringComparer.OrdinalIgnoreCase);
            var domains = (IReadOnlyList<string>)["cover"];

            if (preResolved)
            {
                foreach (var entityId in searchTerms)
                {
                    var matches = _locationService.ExactMatchEntities(entityId, domains);
                    if (matches.Count > 0)
                    {
                        foreach (var entity in matches)
                            if (entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId))
                                allEntities.TryAdd(entity.EntityId, entity);
                    }
                }

                activity?.SetTag("resolution.strategy", "pre_resolved");
            }
            else
            {
                var matchOptions = GetCurrentMatchOptions();
                foreach (var term in searchTerms)
                {
                    var hierarchyResult = await _locationService.SearchHierarchyAsync(
                        term, matchOptions, domains).ConfigureAwait(false);

                    activity?.SetTag($"resolution.{term}", hierarchyResult.ResolutionStrategy.ToString());

                    foreach (var entity in hierarchyResult.ResolvedEntities)
                        if (entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId))
                            allEntities.TryAdd(entity.EntityId, entity);
                }
            }

            if (allEntities.Count == 0)
            {
                return searchTerms.Length == 1 && !preResolved
                    ? BuildNoMatchResponse("garage doors", null, searchTerms[0])
                    : "No garage doors were found.";
            }

            activity?.SetTag("match.count", allEntities.Count);

            var sb = new StringBuilder();
            foreach (var entity in allEntities.Values)
            {
                var request = new ServiceCallRequest
                {
                    ["entity_id"] = entity.EntityId
                };

                string service;
                if (normalizedCommand == "stop")
                {
                    service = "stop_cover";
                }
                else if (position is not null)
                {
                    service = "set_cover_position";
                    var openness = Math.Clamp(position.Value, 0, 100);
                    request["position"] = normalizedCommand == "open" ? openness : 100 - openness;
                }
                else
                {
                    service = normalizedCommand == "open" ? "open_cover" : "close_cover";
                }

                await _homeAssistantClient.CallServiceAsync("cover", service, parameters: null, request)
                    .ConfigureAwait(false);
                sb.AppendLine($"'{entity.FriendlyName}' {normalizedCommand} requested.");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling covers for: {SearchTerms}", string.Join(", ", searchTerms));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"Failed to control garage doors: {ex.Message}";
        }
    }

    private static ServiceCallRequest CreateSecurityRequest(string entityId, string? code)
    {
        var request = new ServiceCallRequest
        {
            EntityId = entityId,
        };

        if (!string.IsNullOrWhiteSpace(code))
        {
            request["code"] = code.Trim();
        }

        return request;
    }

    private static string BuildActionResponse(string action, IReadOnlyList<HomeAssistantEntity> entities)
    {
        var names = entities.Select(entity => entity.FriendlyName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count switch
        {
            0 => action,
            1 => $"{action} {names[0]}.",
            2 => $"{action} {names[0]} and {names[1]}.",
            _ => $"{action} {string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}."
        };
    }

    private static string BuildNoMatchResponse(string deviceType, string? area, string? entityName)
    {
        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(entityName))
        {
            return $"No {deviceType} named '{entityName}' were found in '{area}'.";
        }

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            return $"No {deviceType} named '{entityName}' were found.";
        }

        return $"No {deviceType} were found for '{area}'.";
    }

    private string BuildTarget(string? area, string? entityName)
    {
        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(entityName))
        {
            return $"{entityName} in {area}";
        }

        return !string.IsNullOrWhiteSpace(entityName) ? entityName : area ?? "unspecified target";
    }

    private string FormatStatus(HomeAssistantEntity entity, HomeAssistantState state, bool includeArea)
    {
        var status = state.State.Replace("_", " ", StringComparison.Ordinal);
        var areaName = includeArea ? _locationService.GetAreaForEntity(entity.EntityId)?.Name : null;
        var locationSuffix = string.IsNullOrWhiteSpace(areaName) ? string.Empty : $" in {areaName}";

        return entity.Domain switch
        {
            "alarm_control_panel" => $"alarm is {status}{locationSuffix}",
            "lock" => $"door is {status}{locationSuffix}",
            "cover" => $"{CoverNoun(state)} is {status}{locationSuffix}",
            "camera" => _options.CurrentValue.EnableCameraSnapshot
                ? $"camera is {status}{locationSuffix}; snapshots enabled"
                : $"camera is {status}{locationSuffix}",
            _ => $"state is {status}{locationSuffix}"
        };
    }

    /// <summary>
    /// Labels a cover by its HA device class (garage, gate, window, …) so status
    /// output reads "garage door is open" instead of a blanket "door"; covers without
    /// a device class are reported as generic covers.
    /// </summary>
    private static string CoverNoun(HomeAssistantState state)
    {
        var deviceClass = state.Attributes.TryGetValue("device_class", out var value) ? value?.ToString() : null;

        return deviceClass switch
        {
            "garage" => "garage door",
            "gate" => "gate",
            "door" => "door",
            "window" => "window",
            "blind" => "blind",
            "shutter" => "shutter",
            "curtain" => "curtain",
            "shade" => "shade",
            "awning" => "awning",
            "damper" => "damper",
            _ => "cover",
        };
    }

    private IReadOnlyList<string> GetStatusDomains()
    {
        var domains = new List<string>();
        foreach (var domain in _options.CurrentValue.AllowedDomains)
        {
            if (string.Equals(domain, "alarm_control_panel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(domain, "lock", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(domain, "cover", StringComparison.OrdinalIgnoreCase))
            {
                domains.Add(domain);
            }

            if (_options.CurrentValue.EnableCameraSnapshot &&
                string.Equals(domain, "camera", StringComparison.OrdinalIgnoreCase))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    private string? ResolveCode(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim();
        }

        return string.IsNullOrWhiteSpace(_options.CurrentValue.DefaultAlarmCode)
            ? null
            : _options.CurrentValue.DefaultAlarmCode!.Trim();
    }

    private static string? ResolveLockCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? null
            : code.Trim();
    }

    private async Task<IReadOnlyList<HomeAssistantEntity>> ResolveEntitiesAsync(
        IReadOnlyList<string> domains,
        string? area,
        string? entityName,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(entityName))
        {
            var areaMatches = await ResolveByQueryAsync(area, domains, cancellationToken).ConfigureAwait(false);
            var namedMatches = await ResolveByQueryAsync(entityName, domains, cancellationToken).ConfigureAwait(false);
            var areaIds = areaMatches.Select(entity => entity.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var intersection = namedMatches.Where(entity => areaIds.Contains(entity.EntityId)).ToList();
            if (intersection.Count > 0)
            {
                return intersection;
            }

            var filteredAreaMatches = areaMatches
                .Where(entity => entity.FriendlyName.Contains(entityName, StringComparison.OrdinalIgnoreCase) ||
                    entity.EntityId.Contains(entityName.Replace(" ", "_", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filteredAreaMatches.Count > 0)
            {
                return filteredAreaMatches;
            }

            return [];
        }

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            return await ResolveByQueryAsync(entityName, domains, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(area))
        {
            return await ResolveByQueryAsync(area, domains, cancellationToken).ConfigureAwait(false);
        }

        var entities = await _locationService.GetEntitiesAsync(cancellationToken).ConfigureAwait(false);
        return FilterEntities(entities, domains);
    }

    private async Task<IReadOnlyList<HomeAssistantEntity>> ResolveByQueryAsync(
        string query,
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken)
    {
        var result = await _locationService.SearchHierarchyAsync(
            query,
            GetCurrentMatchOptions(),
            domains,
            cancellationToken).ConfigureAwait(false);

        return FilterEntities(result.ResolvedEntities, domains);
    }

    private IReadOnlyList<HomeAssistantEntity> FilterEntities(
        IEnumerable<HomeAssistantEntity> entities,
        IReadOnlyList<string> domains)
    {
        var domainSet = domains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entities
            .Where(entity => domainSet.Contains(entity.Domain))
            .Where(entity => entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId))
            .GroupBy(entity => entity.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "SecurityControlSkill initialized.")]
    private static partial void LogSkillInitialized(ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Starting {Operation} for {Target} across {EntityCount} entity/entities.")]
    private static partial void LogAlarmOperationStarted(ILogger logger, string operation, string target, int entityCount);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Starting {Operation} for {Target} across {EntityCount} lock entity/entities.")]
    private static partial void LogLockOperationStarted(ILogger logger, string operation, string target, int entityCount);
}
