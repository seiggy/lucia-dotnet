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

    // ── Config Registries ────────────────────────────────────────────

    /// <summary>Returns all floor entries from the config registry.</summary>
    Task<FloorRegistryEntry[]> GetFloorRegistryAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all area entries from the config registry.</summary>
    Task<AreaRegistryEntry[]> GetAreaRegistryAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all entity entries from the config registry.</summary>
    Task<EntityRegistryEntry[]> GetEntityRegistryAsync(CancellationToken cancellationToken = default);

    // ── Media Source ────────────────────────────────────────────────

    /// <summary>
    /// Browse the HA media library. Pass a media-source:// path to browse a subdirectory,
    /// or null/empty to browse the root.
    /// </summary>
    Task<MediaBrowseResult?> BrowseMediaAsync(string? mediaContentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a media file to a HA media directory.
    /// </summary>
    /// <param name="targetDirectory">Target directory as media-source:// URI (e.g., "media-source://media_source/local/alarms").</param>
    /// <param name="fileName">File name including extension (e.g., "alarm.wav").</param>
    /// <param name="fileContent">The file content stream.</param>
    /// <param name="contentType">MIME type (e.g., "audio/wav").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upload response with the media_content_id of the uploaded file.</returns>
    Task<MediaUploadResponse> UploadMediaAsync(
        string targetDirectory,
        string fileName,
        Stream fileContent,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a media file from HA media source via WebSocket command.
    /// </summary>
    /// <param name="mediaContentId">The media-source:// URI of the file to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteMediaAsync(string mediaContentId, CancellationToken cancellationToken = default);

    // ── Shopping List ─────────────────────────────────────────────────

    /// <summary>Returns items from the Home Assistant shopping list (GET /api/shopping_list).</summary>
    Task<ShoppingListItem[]> GetShoppingListItemsAsync(CancellationToken cancellationToken = default);

    // ── Todo Lists ───────────────────────────────────────────────────

    /// <summary>Returns todo entity IDs (e.g. todo.grocery) from HA.</summary>
    Task<string[]> GetTodoListEntityIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns items from a todo list entity via todo.get_items.</summary>
    Task<TodoItem[]> GetTodoItemsAsync(string entityId, CancellationToken cancellationToken = default);
}