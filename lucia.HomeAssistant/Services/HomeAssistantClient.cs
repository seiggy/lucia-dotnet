using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;
using Microsoft.Extensions.Options;

namespace lucia.HomeAssistant.Services;

/// <summary>
/// Typed HTTP client for the Home Assistant REST API.
/// See https://developers.home-assistant.io/docs/api/rest/
/// </summary>
public sealed class HomeAssistantClient : IHomeAssistantClient
{
    private readonly HttpClient _httpClient;

    public HomeAssistantClient(HttpClient httpClient, IOptions<HomeAssistantOptions> options)
    {
        _httpClient = httpClient;
        var opts = options.Value;

        _httpClient.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {opts.AccessToken}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
    }

    // ── Status & Config ─────────────────────────────────────────────

    /// <summary>Returns a message if the API is up and running.</summary>
    public async Task<ApiStatusResponse> GetApiRootAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<ApiStatusResponse>("/api/", cancellationToken);
    }

    /// <summary>Returns the current Home Assistant configuration.</summary>
    public async Task<ConfigResponse> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<ConfigResponse>("/api/config", cancellationToken);
    }

    /// <summary>Returns a list of currently loaded components.</summary>
    public async Task<string[]> GetComponentsAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<string>("/api/components", cancellationToken);
    }

    /// <summary>Trigger a check of configuration.yaml.</summary>
    public async Task<CheckConfigResponse> CheckConfigAsync(CancellationToken cancellationToken = default)
    {
        return await PostAsync<CheckConfigResponse>("/api/config/core/check_config", cancellationToken: cancellationToken);
    }

    // ── Events ──────────────────────────────────────────────────────

    /// <summary>Returns an array of event objects with listener counts.</summary>
    public async Task<EventInfo[]> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<EventInfo>("/api/events", cancellationToken);
    }

    /// <summary>Fires an event with the specified event_type.</summary>
    public async Task<FireEventResponse> FireEventAsync(string eventType, object? data = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync<FireEventResponse>($"/api/events/{Uri.EscapeDataString(eventType)}", data, cancellationToken);
    }

    // ── Services ────────────────────────────────────────────────────

    /// <summary>Returns an array of service domain objects.</summary>
    public async Task<ServiceDomainInfo[]> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<ServiceDomainInfo>("/api/services", cancellationToken);
    }

    /// <summary>Calls a service within a specific domain. Returns raw JSON response.</summary>
    public async Task<string> CallServiceRawAsync(
        string domain,
        string service,
        ServiceCallRequest? request = null,
        bool returnResponse = false,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/services/{Uri.EscapeDataString(domain)}/{Uri.EscapeDataString(service)}";
        if (returnResponse)
            path += "?return_response";

        var json = request is not null
            ? JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default)
            : "{}";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(path, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // ── History & Logbook ───────────────────────────────────────────

    /// <summary>Returns state changes in the past for requested entities.</summary>
    public async Task<HomeAssistantState[][]> GetHistoryAsync(
        string timestamp,
        string? filterEntityId = null,
        string? endTime = null,
        bool? minimalResponse = null,
        bool? noAttributes = null,
        bool? significantChangesOnly = null,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildQueryString(
            ("filter_entity_id", filterEntityId),
            ("end_time", endTime),
            ("minimal_response", minimalResponse),
            ("no_attributes", noAttributes),
            ("significant_changes_only", significantChangesOnly));

        var path = $"/api/history/period/{Uri.EscapeDataString(timestamp)}{qs}";
        var result = await _httpClient.GetFromJsonAsync<HomeAssistantState[][]>(path, HomeAssistantJsonOptions.Default, cancellationToken);
        return result ?? [];
    }

    /// <summary>Returns an array of logbook entries.</summary>
    public async Task<LogbookEntry[]> GetLogbookAsync(
        string timestamp,
        string? entity = null,
        string? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildQueryString(("entity", entity), ("end_time", endTime));
        var path = $"/api/logbook/{Uri.EscapeDataString(timestamp)}{qs}";
        return await GetArrayAsync<LogbookEntry>(path, cancellationToken);
    }

    // ── States ──────────────────────────────────────────────────────

    /// <summary>Returns an array of state objects for all entities.</summary>
    public async Task<HomeAssistantState[]> GetStatesAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<HomeAssistantState>("/api/states", cancellationToken);
    }

    /// <summary>Returns a state object for the specified entity, or null if not found.</summary>
    public async Task<HomeAssistantState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<HomeAssistantState>(
                $"/api/states/{Uri.EscapeDataString(entityId)}", HomeAssistantJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Updates or creates a state for the specified entity.</summary>
    public async Task<HomeAssistantState> SetStateAsync(string entityId, object? payload = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync<HomeAssistantState>($"/api/states/{Uri.EscapeDataString(entityId)}", payload, cancellationToken);
    }


    // ── Camera ──────────────────────────────────────────────────────

    /// <summary>Returns the camera image data.</summary>
    public async Task<byte[]> GetCameraProxyAsync(string cameraEntityId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/camera_proxy/{Uri.EscapeDataString(cameraEntityId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    // ── Calendars ───────────────────────────────────────────────────

    /// <summary>Returns the list of calendar entities.</summary>
    public async Task<CalendarEntity[]> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<CalendarEntity>("/api/calendars", cancellationToken);
    }

    /// <summary>Returns the list of calendar events for the specified calendar.</summary>
    public async Task<CalendarEvent[]> GetCalendarEventsAsync(
        string calendarEntityId,
        string? start = null,
        string? end = null,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildQueryString(("start", start), ("end", end));
        var path = $"/api/calendars/{Uri.EscapeDataString(calendarEntityId)}{qs}";
        return await GetArrayAsync<CalendarEvent>(path, cancellationToken);
    }

    // ── Templates ───────────────────────────────────────────────────

    /// <summary>Render a Home Assistant Jinja2 template.</summary>
    public async Task<string> RenderTemplateAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/template", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // ── Intents ─────────────────────────────────────────────────────

    /// <summary>Handle an intent.</summary>
    public async Task<string> HandleIntentAsync(IntentRequest request, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/intent/handle", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // ── IHomeAssistantClient explicit implementations ───────────────

    async Task<IEnumerable<HomeAssistantState>> IHomeAssistantClient.GetAllEntityStatesAsync(CancellationToken cancellationToken)
    {
        return await GetStatesAsync(cancellationToken);
    }

    async Task<HomeAssistantState?> IHomeAssistantClient.GetEntityStateAsync(string entityId, CancellationToken cancellationToken)
    {
        return await GetStateAsync(entityId, cancellationToken);
    }

    async Task<HomeAssistantState> IHomeAssistantClient.SetEntityStateAsync(
        string entityId, string state, Dictionary<string, object>? attributes, CancellationToken cancellationToken)
    {
        var payload = new { state, attributes = attributes ?? new Dictionary<string, object>() };
        return await SetStateAsync(entityId, payload, cancellationToken);
    }

    async Task<T> IHomeAssistantClient.CallServiceAsync<T>(
        string domain, string service, string? parameters, ServiceCallRequest? request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(service);

        var returnResponse = parameters?.Contains("return_response", StringComparison.OrdinalIgnoreCase) == true;
        var result = await CallServiceRawAsync(domain, service, request, returnResponse, cancellationToken);
        return JsonSerializer.Deserialize<T>(result, HomeAssistantJsonOptions.Default) ?? default!;
    }

    async Task<object[]> IHomeAssistantClient.CallServiceAsync(
        string domain, string service, string? parameters, ServiceCallRequest? request, CancellationToken cancellationToken)
    {
        var returnResponse = parameters?.Contains("return_response", StringComparison.OrdinalIgnoreCase) == true;
        var result = await CallServiceRawAsync(domain, service, request, returnResponse, cancellationToken);
        return JsonSerializer.Deserialize<object[]>(result, HomeAssistantJsonOptions.Default) ?? [];
    }

    async Task<T> IHomeAssistantClient.RunTemplateAsync<T>(string jinjaTemplate, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jinjaTemplate);

        var rendered = await RenderTemplateAsync(new TemplateRenderRequest { Template = jinjaTemplate }, cancellationToken);

        if (typeof(T) == typeof(string))
            return (T)(object)rendered;

        return JsonSerializer.Deserialize<T>(rendered, HomeAssistantJsonOptions.Default)
            ?? throw new InvalidOperationException($"Template response could not be deserialized to type '{typeof(T).Name}'.");
    }

    // ── Private helpers ─────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<T>(path, HomeAssistantJsonOptions.Default, cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private async Task<T[]> GetArrayAsync<T>(string path, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<T[]>(path, HomeAssistantJsonOptions.Default, cancellationToken)
            ?? [];
    }

    private async Task<T> PostAsync<T>(string path, object? payload = null, CancellationToken cancellationToken = default)
    {
        var json = payload is not null
            ? JsonSerializer.Serialize(payload, HomeAssistantJsonOptions.Default)
            : "{}";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(path, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(HomeAssistantJsonOptions.Default, cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private static string BuildQueryString(params (string key, object? value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
                parts.Add($"{key}={Uri.EscapeDataString(value.ToString() ?? string.Empty)}");
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }
}
