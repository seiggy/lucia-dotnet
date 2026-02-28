using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.HomeAssistant.Services;

/// <summary>
/// Typed HTTP client for the Home Assistant REST API.
/// See https://developers.home-assistant.io/docs/api/rest/
/// </summary>
public sealed class HomeAssistantClient : IHomeAssistantClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HomeAssistantClient> _logger;
    private readonly IOptionsMonitor<HomeAssistantOptions> _optionsMonitor;

    /// <summary>Current config snapshot — always reads latest from the options monitor.</summary>
    private HomeAssistantOptions Options => _optionsMonitor.CurrentValue;

    public HomeAssistantClient(HttpClient httpClient, ILogger<HomeAssistantClient> logger, IOptionsMonitor<HomeAssistantOptions> optionsMonitor)
    {
        _httpClient = httpClient;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>
    /// Ensures the HttpClient has the latest BaseAddress and Authorization header
    /// from the current options. Called before every HTTP request so wizard-configured
    /// credentials are picked up without restarting the app.
    /// </summary>
    private void EnsureHttpClientConfigured()
    {
        var options = Options;
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return;

        var expected = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        // BaseAddress can only be set before the first request, so wrap in try/catch
        // for the case where the URL changed after the client was already used.
        if (_httpClient.BaseAddress != expected)
        {
            try
            {
                _httpClient.BaseAddress = expected;
            }
            catch (InvalidOperationException)
            {
                // HttpClient already started — BaseAddress is immutable now.
                // This is expected when config changes after first use.
            }
        }

        // Auth header can always be refreshed
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {options.AccessToken}");
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

        _logger.PostRequest(path);
        EnsureHttpClientConfigured();
        var json = request is not null
            ? JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default)
            : "{}";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(path, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", path);
            throw;
        }
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
        EnsureHttpClientConfigured();
        _logger.GetRequest(path);
        try
        {
            var result = await _httpClient.GetFromJsonAsync<HomeAssistantState[][]>(path, HomeAssistantJsonOptions.Default, cancellationToken);
            return result ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "GET", path);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.DeserializationFailed(ex, "GET", path);
            throw;
        }
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
        var path = $"/api/states/{Uri.EscapeDataString(entityId)}";
        EnsureHttpClientConfigured();
        _logger.GetRequest(path);
        try
        {
            return await _httpClient.GetFromJsonAsync<HomeAssistantState>(
                path, HomeAssistantJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.EntityNotFound(entityId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "GET", path);
            throw;
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
        var path = $"/api/camera_proxy/{Uri.EscapeDataString(cameraEntityId)}";
        EnsureHttpClientConfigured();
        _logger.GetRequest(path);
        try
        {
            var response = await _httpClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "GET", path);
            throw;
        }
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

    // ── Shopping List ───────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ShoppingListItem[]> GetShoppingListItemsAsync(CancellationToken cancellationToken = default)
    {
        return await GetArrayAsync<ShoppingListItem>("/api/shopping_list", cancellationToken);
    }

    // ── Todo Lists ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string[]> GetTodoListEntityIdsAsync(CancellationToken cancellationToken = default)
    {
        var states = await GetStatesAsync(cancellationToken);
        return states
            .Where(s => s.EntityId.StartsWith("todo.", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.EntityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<TodoItem[]> GetTodoItemsAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var request = new ServiceCallRequest { { "entity_id", entityId } };
        var json = await CallServiceRawAsync("todo", "get_items", request, returnResponse: true, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty(entityId, out var entityEl) && entityEl.TryGetProperty("items", out var itemsEl))
            return JsonSerializer.Deserialize<TodoItem[]>(itemsEl.GetRawText(), HomeAssistantJsonOptions.Default) ?? [];
        return [];
    }

    // ── Templates ───────────────────────────────────────────────────

    /// <summary>Render a Home Assistant Jinja2 template.</summary>
    public async Task<string> RenderTemplateAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default)
    {
        EnsureHttpClientConfigured();
        _logger.PostRequest("/api/template");
        var json = JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync("/api/template", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", "/api/template");
            throw;
        }
    }

    // ── Intents ─────────────────────────────────────────────────────

    /// <summary>Handle an intent.</summary>
    public async Task<string> HandleIntentAsync(IntentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureHttpClientConfigured();
        _logger.PostRequest("/api/intent/handle");
        var json = JsonSerializer.Serialize(request, HomeAssistantJsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync("/api/intent/handle", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", "/api/intent/handle");
            throw;
        }
    }

    // ── Config Registries ────────────────────────────────────────────
    //
    // Floor and area registries use the HA WebSocket API because the REST API
    // does not expose these endpoints and the Jinja template engine does not
    // provide access to aliases. A short-lived WebSocket connection is opened,
    // authenticated, and the registry command is sent/received.
    //
    // Entity registry continues to use Jinja templates because area_entities()
    // includes device-inherited area assignments, which the raw entity registry
    // does not.

    /// <summary>Returns all floor entries from the config registry via WebSocket.</summary>
    public async Task<FloorRegistryEntry[]> GetFloorRegistryAsync(CancellationToken cancellationToken = default)
    {
        return await SendWebSocketCommandAsync<FloorRegistryEntry[]>(
            "config/floor_registry/list", cancellationToken) ?? [];
    }

    /// <summary>Returns all area entries from the config registry via WebSocket.</summary>
    public async Task<AreaRegistryEntry[]> GetAreaRegistryAsync(CancellationToken cancellationToken = default)
    {
        return await SendWebSocketCommandAsync<AreaRegistryEntry[]>(
            "config/area_registry/list", cancellationToken) ?? [];
    }

    /// <summary>Returns all entity entries from the config registry via WebSocket.</summary>
    public async Task<EntityRegistryEntry[]> GetEntityRegistryAsync(CancellationToken cancellationToken = default)
    {
        return await SendWebSocketCommandAsync<EntityRegistryEntry[]>(
            "config/entity_registry/list", cancellationToken) ?? [];
    }

    // ── Media Source ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<MediaBrowseResult?> BrowseMediaAsync(string? mediaContentId = null, CancellationToken cancellationToken = default)
    {
        var payload = string.IsNullOrWhiteSpace(mediaContentId)
            ? new { media_content_id = (string?)null, media_content_type = (string?)null }
            : (object)new { media_content_id = mediaContentId, media_content_type = "music" };

        return await SendWebSocketCommandAsync<MediaBrowseResult>(
            "media_source/browse_media", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaUploadResponse> UploadMediaAsync(
        string targetDirectory,
        string fileName,
        Stream fileContent,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        _logger.PostRequest("/api/media_source/local_source/upload");
        EnsureHttpClientConfigured();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(targetDirectory), "media_content_id");
        form.Add(new StreamContent(fileContent) { Headers = { { "Content-Type", contentType } } }, "file", fileName);

        try
        {
            var response = await _httpClient.PostAsync("/api/media_source/local_source/upload", form, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<MediaUploadResponse>(
                HomeAssistantJsonOptions.Default, cancellationToken)
                ?? throw new InvalidOperationException("Upload succeeded but response was empty");
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", "/api/media_source/local_source/upload");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteMediaAsync(string mediaContentId, CancellationToken cancellationToken = default)
    {
        var payload = new { media_content_id = mediaContentId };
        await SendWebSocketCommandAsync<object>(
            "media_source/local_source/remove", payload, cancellationToken);
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
        return JsonSerializer.Deserialize<T>(result, HomeAssistantJsonOptions.Default)
            ?? throw new InvalidOperationException($"Service response could not be deserialized to type '{typeof(T).Name}'.");
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
        EnsureHttpClientConfigured();
        _logger.GetRequest(path);
        try
        {
            return await _httpClient.GetFromJsonAsync<T>(path, HomeAssistantJsonOptions.Default, cancellationToken)
                ?? throw new InvalidOperationException("Failed to deserialize response");
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "GET", path);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.DeserializationFailed(ex, "GET", path);
            throw;
        }
    }

    private async Task<T[]> GetArrayAsync<T>(string path, CancellationToken cancellationToken)
    {
        EnsureHttpClientConfigured();
        _logger.GetRequest(path);
        try
        {
            return await _httpClient.GetFromJsonAsync<T[]>(path, HomeAssistantJsonOptions.Default, cancellationToken)
                ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "GET", path);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.DeserializationFailed(ex, "GET", path);
            throw;
        }
    }

    private async Task<T> PostAsync<T>(string path, object? payload = null, CancellationToken cancellationToken = default)
    {
        EnsureHttpClientConfigured();
        _logger.PostRequest(path);
        var json = payload is not null
            ? JsonSerializer.Serialize(payload, HomeAssistantJsonOptions.Default)
            : "{}";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(path, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(HomeAssistantJsonOptions.Default, cancellationToken)
                ?? throw new InvalidOperationException("Failed to deserialize response");
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", path);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.DeserializationFailed(ex, "POST", path);
            throw;
        }
    }

    private async Task<T[]> PostArrayAsync<T>(string path, object? payload = null, CancellationToken cancellationToken = default)
    {
        EnsureHttpClientConfigured();
        _logger.PostRequest(path);
        var json = payload is not null
            ? JsonSerializer.Serialize(payload, HomeAssistantJsonOptions.Default)
            : "{}";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(path, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T[]>(HomeAssistantJsonOptions.Default, cancellationToken)
                ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.HttpRequestFailed(ex, "POST", path);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.DeserializationFailed(ex, "POST", path);
            throw;
        }
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

    // ── WebSocket helpers for config registry commands ───────────────

    /// <summary>
    /// Opens a short-lived WebSocket connection to HA, authenticates, sends a
    /// single command, deserializes the result, and disconnects.
    /// Uses .NET 10 per-message <see cref="WebSocketStream"/> for clean I/O.
    /// </summary>
    private async Task<T?> SendWebSocketCommandAsync<T>(string commandType, CancellationToken ct)
    {
        return await SendWebSocketCommandAsync<T>(commandType, payload: null, ct);
    }

    private async Task<T?> SendWebSocketCommandAsync<T>(string commandType, object? payload, CancellationToken ct)
    {
        var baseUrl = Options.BaseUrl.TrimEnd('/');
        var wsScheme = baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var uri = new Uri(baseUrl);
        var wsUri = new Uri($"{wsScheme}://{uri.Host}:{uri.Port}/api/websocket");

        _logger.WebSocketConnecting(wsUri.ToString());

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(wsUri, ct);

        try
        {
            // Step 1: Receive auth_required message
            var authRequired = await WsReadJsonAsync(ws, ct);
            var msgType = GetJsonStringProperty(authRequired, "type");
            if (msgType != "auth_required")
                throw new InvalidOperationException($"Expected 'auth_required', got '{msgType}'");

            // Step 2: Send auth
            await WsWriteJsonAsync(ws, new { type = "auth", access_token = Options.AccessToken }, ct);

            // Step 3: Receive auth result
            var authResult = await WsReadJsonAsync(ws, ct);
            msgType = GetJsonStringProperty(authResult, "type");
            if (msgType != "auth_ok")
            {
                var authMsg = GetJsonStringProperty(authResult, "message");
                throw new InvalidOperationException($"WebSocket auth failed: {authMsg}");
            }

            // Step 4: Send command (merge payload properties into command message)
            _logger.WebSocketCommand(commandType);
            var command = BuildWsCommand(1, commandType, payload);
            await WsWriteJsonAsync(ws, command, ct);

            // Step 5: Receive command result
            var result = await WsReadJsonAsync(ws, ct);
            var success = result.RootElement.TryGetProperty("success", out var successProp)
                          && successProp.GetBoolean();

            if (!success)
            {
                var errorMsg = result.RootElement.TryGetProperty("error", out var errorProp)
                    ? errorProp.ToString()
                    : "Unknown WebSocket error";
                _logger.WebSocketCommandFailed(commandType, errorMsg);
                throw new InvalidOperationException($"WebSocket command '{commandType}' failed: {errorMsg}");
            }

            if (result.RootElement.TryGetProperty("result", out var resultProp))
            {
                return JsonSerializer.Deserialize<T>(resultProp.GetRawText(), HomeAssistantJsonOptions.Default);
            }

            return default;
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                catch
                {
                    // Best-effort close; don't mask the real exception
                }
            }
        }
    }

    /// <summary>
    /// Reads a single WebSocket message as a <see cref="JsonDocument"/> using
    /// .NET 10 <see cref="WebSocketStream.CreateReadableMessageStream"/>.
    /// </summary>
    private static async Task<JsonDocument> WsReadJsonAsync(ClientWebSocket ws, CancellationToken ct)
    {
        await using var stream = WebSocketStream.CreateReadableMessageStream(ws);
        return await JsonSerializer.DeserializeAsync<JsonDocument>(stream, HomeAssistantJsonOptions.Default, ct)
               ?? throw new InvalidOperationException("Received empty WebSocket message");
    }

    /// <summary>
    /// Writes a single JSON payload as one WebSocket text message using
    /// .NET 10 <see cref="WebSocketStream.CreateWritableMessageStream"/>.
    /// </summary>
    private static async Task WsWriteJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        await using var stream = WebSocketStream.CreateWritableMessageStream(ws, WebSocketMessageType.Text);
        await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(payload, HomeAssistantJsonOptions.Default), ct);
    }

    private static string? GetJsonStringProperty(JsonDocument doc, string propertyName)
    {
        return doc.RootElement.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    /// <summary>
    /// Builds a WebSocket command dictionary with id, type, and merged payload properties.
    /// </summary>
    private static Dictionary<string, object?> BuildWsCommand(int id, string type, object? payload)
    {
        var command = new Dictionary<string, object?> { ["id"] = id, ["type"] = type };

        if (payload is null) return command;

        // Serialize payload to JSON and merge properties
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, HomeAssistantJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            command[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => JsonSerializer.Deserialize<object>(prop.Value.GetRawText(), HomeAssistantJsonOptions.Default)
            };
        }

        return command;
    }
}
