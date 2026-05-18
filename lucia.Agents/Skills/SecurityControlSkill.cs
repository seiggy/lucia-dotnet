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
public sealed partial class SecurityControlSkill : IAgentSkill, IOptimizableSkill, ICommandPatternProvider
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
    public IReadOnlyList<string> SearchToolNames { get; } = [nameof(ArmAlarm), nameof(DisarmAlarm), nameof(LockDoor), nameof(UnlockDoor), nameof(GetSecurityStatus)];

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
        var start = Stopwatch.GetTimestamp();

        try
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
        }
        catch (Exception ex)
        {
            AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogSecurityOperationFailed(_logger, ex, "arm alarm", area ?? "all visible areas");
            return "Unable to arm the alarm right now.";
        }
        finally
        {
            RecordDuration(activity, start, "arm_alarm");
        }
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
        var start = Stopwatch.GetTimestamp();

        try
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
        }
        catch (Exception ex)
        {
            AlarmFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogSecurityOperationFailed(_logger, ex, "disarm alarm", area ?? "all visible areas");
            return "Unable to disarm the alarm right now.";
        }
        finally
        {
            RecordDuration(activity, start, "disarm_alarm");
        }
    }

    /// <summary>
    /// Locks one or more doors.
    /// </summary>
    [Description("Lock one or more Home Assistant lock entities by area and optional entity name.")]
    public async Task<string> LockDoor(
        [Description("Optional area name that contains the door lock, such as 'front porch' or 'garage'.")] string? area,
        [Description("Optional lock name, such as 'front door' or 'garage entry'.")] string? entityName)
    {
        if (string.IsNullOrWhiteSpace(area) && string.IsNullOrWhiteSpace(entityName))
        {
            return "Please specify which door or area to lock.";
        }

        using var activity = ActivitySource.StartActivity(nameof(LockDoor));
        activity?.SetTag("security.operation", "lock_door");
        activity?.SetTag("security.area", area);
        activity?.SetTag("security.entity_name", entityName);
        LockRequests.Add(1, [new KeyValuePair<string, object?>("action", "lock")]);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var locks = await ResolveEntitiesAsync(["lock"], area, entityName).ConfigureAwait(false);
            if (locks.Count == 0)
            {
                LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return BuildNoMatchResponse("locks", area, entityName);
            }

            LogLockOperationStarted(_logger, "lock", BuildTarget(area, entityName), locks.Count);

            foreach (var lockEntity in locks)
            {
                var request = CreateSecurityRequest(lockEntity.EntityId, null);
                await _homeAssistantClient.CallServiceAsync("lock", "lock", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Locked", locks);
        }
        catch (Exception ex)
        {
            LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogSecurityOperationFailed(_logger, ex, "lock door", BuildTarget(area, entityName));
            return "Unable to lock the requested door right now.";
        }
        finally
        {
            RecordDuration(activity, start, "lock_door");
        }
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
        var start = Stopwatch.GetTimestamp();

        try
        {
            var locks = await ResolveEntitiesAsync(["lock"], area, entityName).ConfigureAwait(false);
            if (locks.Count == 0)
            {
                LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return BuildNoMatchResponse("locks", area, entityName);
            }

            var resolvedCode = ResolveCode(code);
            LogLockOperationStarted(_logger, "unlock", BuildTarget(area, entityName), locks.Count);

            foreach (var lockEntity in locks)
            {
                var request = CreateSecurityRequest(lockEntity.EntityId, resolvedCode);
                await _homeAssistantClient.CallServiceAsync("lock", "unlock", request: request).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return BuildActionResponse("Unlocked", locks);
        }
        catch (Exception ex)
        {
            LockFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogSecurityOperationFailed(_logger, ex, "unlock door", BuildTarget(area, entityName));
            return "Unable to unlock the requested door right now.";
        }
        finally
        {
            RecordDuration(activity, start, "unlock_door");
        }
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
        var start = Stopwatch.GetTimestamp();

        try
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
        }
        catch (Exception ex)
        {
            StatusFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogSecurityOperationFailed(_logger, ex, "get security status", area ?? "all visible areas");
            return "Unable to retrieve security status right now.";
        }
        finally
        {
            RecordDuration(activity, start, "get_status");
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
            "camera" => _options.CurrentValue.EnableCameraSnapshot
                ? $"camera is {status}{locationSuffix}; snapshots enabled"
                : $"camera is {status}{locationSuffix}",
            _ => $"state is {status}{locationSuffix}"
        };
    }

    private IReadOnlyList<string> GetStatusDomains()
    {
        var domains = new List<string>();
        foreach (var domain in _options.CurrentValue.AllowedDomains)
        {
            if (string.Equals(domain, "alarm_control_panel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(domain, "lock", StringComparison.OrdinalIgnoreCase))
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

    private void RecordDuration(Activity? activity, long start, string operation)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        OperationDurationMs.Record(elapsedMs, [new KeyValuePair<string, object?>("operation", operation)]);
        activity?.SetTag("elapsed_ms", elapsedMs);
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

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "Security operation '{Operation}' failed for {Target}.")]
    private static partial void LogSecurityOperationFailed(ILogger logger, Exception exception, string operation, string target);
}
