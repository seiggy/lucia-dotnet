using lucia.HomeAssistant.Services;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for alarm clock CRUD and alarm sound management.
/// </summary>
public static class AlarmClockApi
{
    public static IEndpointRouteBuilder MapAlarmClockApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/alarms")
            .WithTags("AlarmClocks")
            .RequireAuthorization();

        // Alarm Clocks
        group.MapGet("/", ListAlarmsAsync);
        group.MapGet("/{id}", GetAlarmAsync);
        group.MapPost("/", CreateAlarmAsync);
        group.MapPut("/{id}", UpdateAlarmAsync);
        group.MapDelete("/{id}", DeleteAlarmAsync);
        group.MapPut("/{id}/enable", EnableAlarmAsync);
        group.MapPut("/{id}/disable", DisableAlarmAsync);
        group.MapPost("/{id}/dismiss", DismissAlarmAsync);
        group.MapPost("/{id}/snooze", SnoozeAlarmAsync);

        // Alarm Sounds
        group.MapGet("/sounds", ListSoundsAsync);
        group.MapPost("/sounds", CreateSoundAsync);
        group.MapPost("/sounds/upload", UploadSoundAsync).DisableAntiforgery();
        group.MapGet("/sounds/{id}", GetSoundAsync);
        group.MapDelete("/sounds/{id}", DeleteSoundAsync);
        group.MapPut("/sounds/{id}/default", SetDefaultSoundAsync);

        return endpoints;
    }

    // -- Alarm Clock endpoints --

    private static async Task<Ok<IReadOnlyList<AlarmClock>>> ListAlarmsAsync(
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        var alarms = await repo.GetAllAlarmsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(alarms);
    }

    private static async Task<Results<Ok<AlarmClock>, NotFound>> GetAlarmAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        return alarm is not null
            ? TypedResults.Ok(alarm)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<AlarmClock>, BadRequest<string>>> CreateAlarmAsync(
        [FromBody] CreateAlarmRequest request,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] CronScheduleService cronService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.TargetEntity))
            return TypedResults.BadRequest("TargetEntity is required");

        // Validate CRON if provided
        if (request.CronSchedule is not null && !cronService.IsValid(request.CronSchedule))
            return TypedResults.BadRequest($"Invalid CRON expression: {request.CronSchedule}");

        // Require either CronSchedule or NextFireAt
        if (request.CronSchedule is null && request.NextFireAt is null)
            return TypedResults.BadRequest("Either CronSchedule or NextFireAt is required");

        var alarm = new AlarmClock
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = request.Name,
            TargetEntity = request.TargetEntity,
            AlarmSoundId = request.AlarmSoundId,
            CronSchedule = request.CronSchedule,
            NextFireAt = request.NextFireAt,
            PlaybackInterval = request.PlaybackInterval ?? TimeSpan.FromSeconds(30),
            AutoDismissAfter = request.AutoDismissAfter ?? TimeSpan.FromMinutes(10),
            IsEnabled = request.IsEnabled ?? true,
            VolumeStart = request.VolumeStart,
            VolumeEnd = request.VolumeEnd,
            VolumeRampDuration = request.VolumeRampDuration ?? TimeSpan.FromSeconds(30)
        };

        // Compute NextFireAt from CRON if schedule is provided
        if (alarm.CronSchedule is not null)
        {
            cronService.InitializeNextFireAt(alarm);
        }

        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);
        return TypedResults.Created($"/api/alarms/{alarm.Id}", alarm);
    }

    private static async Task<Results<Ok<AlarmClock>, NotFound, BadRequest<string>>> UpdateAlarmAsync(
        [FromRoute] string id,
        [FromBody] UpdateAlarmRequest request,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] CronScheduleService cronService,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        // Validate CRON if provided
        if (request.CronSchedule is not null && !cronService.IsValid(request.CronSchedule))
            return TypedResults.BadRequest($"Invalid CRON expression: {request.CronSchedule}");

        if (request.Name is not null)
            alarm.Name = request.Name;
        if (request.TargetEntity is not null)
            alarm.TargetEntity = request.TargetEntity;
        if (request.AlarmSoundId is not null)
            alarm.AlarmSoundId = request.AlarmSoundId;
        if (request.PlaybackInterval is not null)
            alarm.PlaybackInterval = request.PlaybackInterval.Value;
        if (request.AutoDismissAfter is not null)
            alarm.AutoDismissAfter = request.AutoDismissAfter.Value;
        if (request.IsEnabled is not null)
            alarm.IsEnabled = request.IsEnabled.Value;
        if (request.VolumeStart is not null)
            alarm.VolumeStart = request.VolumeStart;
        if (request.VolumeEnd is not null)
            alarm.VolumeEnd = request.VolumeEnd;
        if (request.VolumeRampDuration is not null)
            alarm.VolumeRampDuration = request.VolumeRampDuration.Value;

        // Update schedule
        if (request.CronSchedule is not null)
        {
            alarm.CronSchedule = request.CronSchedule;
            cronService.InitializeNextFireAt(alarm);
        }
        else if (request.NextFireAt is not null)
        {
            alarm.CronSchedule = null;
            alarm.NextFireAt = request.NextFireAt;
        }

        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);
        return TypedResults.Ok(alarm);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAlarmAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] ScheduledTaskStore taskStore,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        // Cancel any active ringing tasks for this alarm
        CancelActiveAlarmTasks(taskStore, id);

        await repo.DeleteAlarmAsync(id, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AlarmClock>, NotFound>> EnableAlarmAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] CronScheduleService cronService,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        alarm.IsEnabled = true;

        // Recompute next fire time when re-enabling
        if (alarm.CronSchedule is not null)
        {
            cronService.InitializeNextFireAt(alarm);
        }

        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);
        return TypedResults.Ok(alarm);
    }

    private static async Task<Results<Ok<AlarmClock>, NotFound>> DisableAlarmAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] ScheduledTaskStore taskStore,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        alarm.IsEnabled = false;

        // Cancel any active ringing tasks
        CancelActiveAlarmTasks(taskStore, id);

        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);
        return TypedResults.Ok(alarm);
    }

    private static async Task<Results<Ok<object>, NotFound>> DismissAlarmAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] ScheduledTaskStore taskStore,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        var cancelled = CancelActiveAlarmTasks(taskStore, id);
        alarm.LastDismissedAt = DateTimeOffset.UtcNow;
        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            alarmId = id,
            dismissed = true,
            cancelledTasks = cancelled
        });
    }

    private static async Task<Results<Ok<AlarmClock>, NotFound, BadRequest<string>>> SnoozeAlarmAsync(
        [FromRoute] string id,
        [FromBody] SnoozeRequest? request,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] ScheduledTaskStore taskStore,
        CancellationToken ct)
    {
        var alarm = await repo.GetAlarmAsync(id, ct).ConfigureAwait(false);
        if (alarm is null)
            return TypedResults.NotFound();

        // Dismiss active ringing
        CancelActiveAlarmTasks(taskStore, id);

        // Re-schedule for snooze duration (default 9 minutes)
        var snoozeDuration = request?.Duration ?? TimeSpan.FromMinutes(9);
        if (snoozeDuration <= TimeSpan.Zero)
            return TypedResults.BadRequest("Snooze duration must be positive");

        alarm.NextFireAt = DateTimeOffset.UtcNow + snoozeDuration;
        alarm.LastDismissedAt = DateTimeOffset.UtcNow;
        await repo.UpsertAlarmAsync(alarm, ct).ConfigureAwait(false);

        return TypedResults.Ok(alarm);
    }

    // -- Alarm Sound endpoints --

    private static async Task<Ok<IReadOnlyList<AlarmSound>>> ListSoundsAsync(
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        var sounds = await repo.GetAllSoundsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(sounds);
    }

    private static async Task<Results<Ok<AlarmSound>, NotFound>> GetSoundAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        var sound = await repo.GetSoundAsync(id, ct).ConfigureAwait(false);
        return sound is not null
            ? TypedResults.Ok(sound)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<AlarmSound>, BadRequest<string>>> CreateSoundAsync(
        [FromBody] CreateSoundRequest request,
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.MediaSourceUri))
            return TypedResults.BadRequest("MediaSourceUri is required");

        var sound = new AlarmSound
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = request.Name,
            MediaSourceUri = request.MediaSourceUri,
            UploadedViaLucia = request.UploadedViaLucia ?? false,
            IsDefault = request.IsDefault ?? false
        };

        // If setting as default, clear existing defaults
        if (sound.IsDefault)
        {
            await ClearDefaultSoundsAsync(repo, ct).ConfigureAwait(false);
        }

        await repo.UpsertSoundAsync(sound, ct).ConfigureAwait(false);
        return TypedResults.Created($"/api/alarms/sounds/{sound.Id}", sound);
    }

    private static async Task<Results<Created<AlarmSound>, BadRequest<string>>> UploadSoundAsync(
        IFormFile file,
        [FromForm] string name,
        [FromForm] bool isDefault,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] IHomeAssistantClient haClient,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return TypedResults.BadRequest("Name is required");

        if (file is null || file.Length == 0)
            return TypedResults.BadRequest("A file is required");

        const string targetDirectory = "media-source://media_source/local/alarms";

        using var stream = file.OpenReadStream();
        var uploadResult = await haClient.UploadMediaAsync(
            targetDirectory, file.FileName, stream, file.ContentType, ct).ConfigureAwait(false);

        var sound = new AlarmSound
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            MediaSourceUri = uploadResult.MediaContentId,
            UploadedViaLucia = true,
            IsDefault = isDefault
        };

        if (sound.IsDefault)
        {
            await ClearDefaultSoundsAsync(repo, ct).ConfigureAwait(false);
        }

        await repo.UpsertSoundAsync(sound, ct).ConfigureAwait(false);
        return TypedResults.Created($"/api/alarms/sounds/{sound.Id}", sound);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSoundAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        [FromServices] IHomeAssistantClient haClient,
        CancellationToken ct)
    {
        var sound = await repo.GetSoundAsync(id, ct).ConfigureAwait(false);
        if (sound is null)
            return TypedResults.NotFound();

        // Clean up HA media file if it was uploaded via Lucia
        if (sound.UploadedViaLucia && !string.IsNullOrWhiteSpace(sound.MediaSourceUri))
        {
            try
            {
                await haClient.DeleteMediaAsync(sound.MediaSourceUri, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup — don't fail the delete if HA media removal fails
            }
        }

        await repo.DeleteSoundAsync(id, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AlarmSound>, NotFound>> SetDefaultSoundAsync(
        [FromRoute] string id,
        [FromServices] IAlarmClockRepository repo,
        CancellationToken ct)
    {
        var sound = await repo.GetSoundAsync(id, ct).ConfigureAwait(false);
        if (sound is null)
            return TypedResults.NotFound();

        await ClearDefaultSoundsAsync(repo, ct).ConfigureAwait(false);

        sound.IsDefault = true;
        await repo.UpsertSoundAsync(sound, ct).ConfigureAwait(false);
        return TypedResults.Ok(sound);
    }

    // -- Helpers --

    /// <summary>
    /// Cancels all active alarm scheduled tasks for a given alarm clock.
    /// Returns the number of tasks cancelled.
    /// </summary>
    private static int CancelActiveAlarmTasks(ScheduledTaskStore taskStore, string alarmClockId)
    {
        var alarmTasks = taskStore.GetByType(ScheduledTaskType.Alarm)
            .OfType<AlarmScheduledTask>()
            .Where(t => t.AlarmClockId == alarmClockId)
            .ToList();

        var cancelled = 0;
        foreach (var task in alarmTasks)
        {
            if (taskStore.TryRemove(task.Id, out _))
                cancelled++;
        }

        return cancelled;
    }

    private static async Task ClearDefaultSoundsAsync(IAlarmClockRepository repo, CancellationToken ct)
    {
        var allSounds = await repo.GetAllSoundsAsync(ct).ConfigureAwait(false);
        foreach (var existing in allSounds.Where(s => s.IsDefault))
        {
            existing.IsDefault = false;
            await repo.UpsertSoundAsync(existing, ct).ConfigureAwait(false);
        }
    }
}
