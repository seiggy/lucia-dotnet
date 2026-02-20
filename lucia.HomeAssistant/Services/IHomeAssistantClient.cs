using lucia.HomeAssistant.Models;

namespace lucia.HomeAssistant.Services;

public interface IHomeAssistantClient
{
    // ── Existing high-level abstractions ─────────────────────────────

    Task<IEnumerable<HomeAssistantState>> GetAllEntityStatesAsync(CancellationToken cancellationToken = default);
    Task<HomeAssistantState?> GetEntityStateAsync(string entityId, CancellationToken cancellationToken = default);
    Task<HomeAssistantState> SetEntityStateAsync(string entityId, string state, Dictionary<string, object>? attributes = null, CancellationToken cancellationToken = default);
    Task<object[]> CallServiceAsync(string domain, string service, string? parameters = null, ServiceCallRequest? request = null, CancellationToken cancellationToken = default);
    Task<T> CallServiceAsync<T>(string domain, string service, string? parameters = null, ServiceCallRequest? request = null, CancellationToken cancellationToken = default);
    Task<T> RunTemplateAsync<T>(string jinjaTemplate, CancellationToken cancellationToken = default);

    // ── Status & Config ─────────────────────────────────────────────

    /// <summary>Returns a message if the API is up and running.</summary>
    Task<ApiStatusResponse> GetApiRootAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current Home Assistant configuration.</summary>
    Task<ConfigResponse> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a list of currently loaded components.</summary>
    Task<string[]> GetComponentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Trigger a check of configuration.yaml.</summary>
    Task<CheckConfigResponse> CheckConfigAsync(CancellationToken cancellationToken = default);

    // ── Events ──────────────────────────────────────────────────────

    /// <summary>Returns an array of event objects with listener counts.</summary>
    Task<EventInfo[]> GetEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fires an event with the specified event_type.</summary>
    Task<FireEventResponse> FireEventAsync(string eventType, object? data = null, CancellationToken cancellationToken = default);

    // ── Services ────────────────────────────────────────────────────

    /// <summary>Returns an array of service domain objects.</summary>
    Task<ServiceDomainInfo[]> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Calls a service within a specific domain. Returns raw JSON response.</summary>
    Task<string> CallServiceRawAsync(
        string domain,
        string service,
        ServiceCallRequest? request = null,
        bool returnResponse = false,
        CancellationToken cancellationToken = default);

    // ── History & Logbook ───────────────────────────────────────────

    /// <summary>Returns state changes in the past for requested entities.</summary>
    Task<HomeAssistantState[][]> GetHistoryAsync(
        string timestamp,
        string? filterEntityId = null,
        string? endTime = null,
        bool? minimalResponse = null,
        bool? noAttributes = null,
        bool? significantChangesOnly = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an array of logbook entries.</summary>
    Task<LogbookEntry[]> GetLogbookAsync(
        string timestamp,
        string? entity = null,
        string? endTime = null,
        CancellationToken cancellationToken = default);

    // ── States ──────────────────────────────────────────────────────

    /// <summary>Returns an array of state objects for all entities.</summary>
    Task<HomeAssistantState[]> GetStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a state object for the specified entity, or null if not found.</summary>
    Task<HomeAssistantState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>Updates or creates a state for the specified entity.</summary>
    Task<HomeAssistantState> SetStateAsync(string entityId, object? payload = null, CancellationToken cancellationToken = default);

    // ── Camera ──────────────────────────────────────────────────────

    /// <summary>Returns the camera image data.</summary>
    Task<byte[]> GetCameraProxyAsync(string cameraEntityId, CancellationToken cancellationToken = default);

    // ── Calendars ───────────────────────────────────────────────────

    /// <summary>Returns the list of calendar entities.</summary>
    Task<CalendarEntity[]> GetCalendarsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the list of calendar events for the specified calendar.</summary>
    Task<CalendarEvent[]> GetCalendarEventsAsync(
        string calendarEntityId,
        string? start = null,
        string? end = null,
        CancellationToken cancellationToken = default);

    // ── Templates ───────────────────────────────────────────────────

    /// <summary>Render a Home Assistant Jinja2 template.</summary>
    Task<string> RenderTemplateAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default);

    // ── Intents ─────────────────────────────────────────────────────

    /// <summary>Handle an intent.</summary>
    Task<string> HandleIntentAsync(IntentRequest request, CancellationToken cancellationToken = default);
}