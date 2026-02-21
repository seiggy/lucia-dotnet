using lucia.HomeAssistant.Services;

namespace lucia.Tests;

public class HomeAssistantApiTests
{
    private readonly HomeAssistantClient? _client;

    public HomeAssistantApiTests()
    {
        var (client, _) = HomeAssistantTestConfig.CreateClient();
        _client = client;
    }

    private void SkipIfNotConfigured() =>
        Skip.If(!HomeAssistantTestConfig.IsConfigured, "HA_ENDPOINT and HA_TOKEN not configured. Set via user secrets or environment variables.");

    [SkippableFact]
    public async Task GetApiRootAsync_ShouldReturnApiStatus()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetApiRootAsync();
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task GetConfigAsync_ShouldReturnConfiguration()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetConfigAsync();
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task GetEventsAsync_ShouldReturnAvailableEvents()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetEventsAsync();
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task GetServicesAsync_ShouldReturnAvailableServices()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetServicesAsync();
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task GetStatesAsync_ShouldReturnAllEntityStates()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetStatesAsync();

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        var firstState = result.First();
        Assert.NotNull(firstState.EntityId);
        Assert.NotNull(firstState.State);
    }

    [SkippableFact]
    public async Task GetStateAsync_WithValidEntityId_ShouldReturnEntityState()
    {
        SkipIfNotConfigured();
        var entityId = "sensor.time";
        var result = await _client!.GetStateAsync(entityId);

        Assert.NotNull(result);
        Assert.Equal(entityId, result.EntityId);
        Assert.NotNull(result.State);
    }

    [SkippableFact]
    public async Task GetStateAsync_WithInvalidEntityId_ShouldReturnNull()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetStateAsync("invalid.nonexistent_entity");
        Assert.Null(result);
    }

    [SkippableFact]
    public async Task GetCalendarsAsync_ShouldReturnCalendars()
    {
        SkipIfNotConfigured();
        var result = await _client!.GetCalendarsAsync();
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task CallServiceAsync_WithValidService_ShouldExecuteSuccessfully()
    {
        SkipIfNotConfigured();
        var request = new lucia.HomeAssistant.Models.ServiceCallRequest
        {
            ["notification_id"] = "test_integration_1234",
            ["title"] = "Integration Test",
            ["message"] = "This is a test notification from the integration test."
        };

        IHomeAssistantClient client = _client!;
        var result = await client.CallServiceAsync("persistent_notification", "create", null, request);

        Assert.NotNull(result);
        Assert.IsType<object[]>(result);
    }

    [SkippableFact]
    public async Task FireEventAsync_WithTestEvent_ShouldFireEventSuccessfully()
    {
        SkipIfNotConfigured();
        var eventData = new Dictionary<string, object>
        {
            ["test_data"] = "integration_test_value",
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        var result = await _client!.FireEventAsync("test_integration_event", eventData);
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task RenderTemplateAsync_WithValidTemplate_ShouldReturnRenderedResult()
    {
        SkipIfNotConfigured();
        var templateRequest = new lucia.HomeAssistant.Models.TemplateRenderRequest
        {
            Template = "{{ now().strftime('%Y-%m-%d %H:%M:%S') }}"
        };

        var result = await _client!.RenderTemplateAsync(templateRequest);

        Assert.NotNull(result);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", result);
    }

    [SkippableFact]
    public async Task GetHistoryAsync_WithValidParameters_ShouldReturnHistoricalData()
    {
        SkipIfNotConfigured();
        var timestamp = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entityId = "sun.sun";

        var result = await _client!.GetHistoryAsync(
            timestamp,
            entityId,
            endTime: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );

        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task GetLogbookAsync_WithValidParameters_ShouldReturnLogbookEntries()
    {
        SkipIfNotConfigured();
        var timestamp = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var result = await _client!.GetLogbookAsync(
            timestamp,
            endTime: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );

        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task CheckConfigAsync_ShouldReturnConfigValidationResult()
    {
        SkipIfNotConfigured();
        var result = await _client!.CheckConfigAsync();
        Assert.NotNull(result);
    }
}
