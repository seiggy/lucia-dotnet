using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for activating Home Assistant scenes (scene.turn_on).
/// </summary>
public sealed class SceneControlSkill : IAgentSkill
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEntityLocationService _locationService;
    private readonly ILogger<SceneControlSkill> _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.SceneControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.SceneControl", "1.0.0");
    private static readonly Counter<long> SceneListRequests = Meter.CreateCounter<long>("scene.list.requests", "{count}", "Number of scene list requests.");
    private static readonly Counter<long> SceneActivateRequests = Meter.CreateCounter<long>("scene.activate.requests", "{count}", "Number of scene activate requests.");
    private static readonly Counter<long> SceneActivateFailures = Meter.CreateCounter<long>("scene.activate.failures", "{count}", "Number of failed scene activations.");
    private static readonly Histogram<double> SceneActivateDurationMs = Meter.CreateHistogram<double>("scene.activate.duration", "ms", "Duration of scene activation.");

    public SceneControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEntityLocationService locationService,
        ILogger<SceneControlSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _locationService = locationService;
        _logger = logger;
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(ListScenesAsync),
            AIFunctionFactory.Create(FindScenesByAreaAsync),
            AIFunctionFactory.Create(ActivateSceneAsync)
        ];
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SceneControlSkill initialized.");
        return Task.CompletedTask;
    }

    [Description("List all available Home Assistant scenes. Returns scene names and entity IDs.")]
    public async Task<string> ListScenesAsync(
        CancellationToken cancellationToken = default)
    {
        SceneListRequests.Add(1);
        using var activity = ActivitySource.StartActivity();

        try
        {
            var states = await _homeAssistantClient.GetStatesAsync(cancellationToken).ConfigureAwait(false);
            var scenes = states
                .Where(s => s.EntityId.StartsWith("scene.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => GetFriendlyName(s))
                .Select(s => $"- {GetFriendlyName(s)} ({s.EntityId})")
                .ToList();

            if (scenes.Count == 0)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return "No scenes found in Home Assistant.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {scenes.Count} scene(s):");
            sb.AppendLine(string.Join(Environment.NewLine, scenes));
            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing scenes");
            return $"Error listing scenes: {ex.Message}";
        }
    }

    [Description("Find scenes in a specific area/room. Use the area name (e.g., 'living room', 'bedroom', 'kitchen').")]
    public async Task<string> FindScenesByAreaAsync(
        [Description("The area/room name (e.g., 'living room', 'bedroom', 'upstairs')")] string areaName,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        try
        {
            var locationEntities = await _locationService.FindEntitiesByLocationAsync(
                areaName, (IReadOnlyList<string>)["scene"], cancellationToken).ConfigureAwait(false);

            if (locationEntities.Count == 0)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return $"No scenes found in area '{areaName}'.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {locationEntities.Count} scene(s) in '{areaName}':");
            foreach (var entity in locationEntities)
            {
                sb.AppendLine($"- {entity.FriendlyName ?? entity.EntityId} ({entity.EntityId})");
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding scenes in area: {AreaName}", areaName);
            return $"Error searching for scenes in '{areaName}': {ex.Message}";
        }
    }

    [Description("Activate a Home Assistant scene by entity ID. Use scene.ENTITY_ID format (e.g., scene.movie_mode, scene.romantic).")]
    public async Task<string> ActivateSceneAsync(
        [Description("The scene entity ID (e.g., scene.movie_mode, scene.night_mode, scene.romantic)")] string entityId,
        CancellationToken cancellationToken = default)
    {
        SceneActivateRequests.Add(1);
        var start = Stopwatch.GetTimestamp();
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        try
        {
            var entityIdNorm = entityId.StartsWith("scene.", StringComparison.OrdinalIgnoreCase)
                ? entityId
                : $"scene.{entityId.Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant()}";

            var request = new ServiceCallRequest
            {
                EntityId = entityIdNorm
            };

            await _homeAssistantClient.CallServiceAsync("scene", "turn_on", request: request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var durationMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            SceneActivateDurationMs.Record(durationMs);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation("Activated scene {EntityId}", entityIdNorm);
            return $"Scene '{entityIdNorm}' activated successfully.";
        }
        catch (Exception ex)
        {
            SceneActivateFailures.Add(1);
            _logger.LogError(ex, "Error activating scene {EntityId}", entityId);
            return $"Error activating scene '{entityId}': {ex.Message}";
        }
    }

    private static string GetFriendlyName(HomeAssistantState state)
    {
        if (state.Attributes.TryGetValue("friendly_name", out var fn) && fn is not null)
            return fn.ToString() ?? state.EntityId;
        return state.EntityId;
    }
}
