using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;

namespace lucia.Tests;

public class HomeAssistantApiTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GeneratedHomeAssistantClient _client;

    public HomeAssistantApiTests()
    {
        var services = new ServiceCollection();

        // Configure Home Assistant options
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            // test instance key. Need to disable this test in CI as test HA instance is not available there.
            options.AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiIyZmQyNzkyNTE4M2Y0ZjQyYjE5N2E2NTVjNzM0ZTkzOCIsImlhdCI6MTc1MjA5NTI5MywiZXhwIjoyMDY3NDU1MjkzfQ.A-ZsmZx0dZJosOno_C4ct3fdh0YYo9kou4H7pN9DIKc";
            options.TimeoutSeconds = 30;
            options.ValidateSSL = false; // For self-signed certificates
        });

        // Configure HttpClient
        services.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        _serviceProvider = services.BuildServiceProvider();
        _client = _serviceProvider.GetRequiredService<GeneratedHomeAssistantClient>();
    }

    [Fact]
    public async Task GetApiRootAsync_ShouldReturnApiStatus()
    {
        // Act
        var result = await _client.GetApiRootAsync();

        // Assert
        Assert.NotNull(result);
        // API root typically returns a message about the API
    }

    [Fact]
    public async Task GetConfigAsync_ShouldReturnConfiguration()
    {
        // Act
        var result = await _client.GetConfigAsync();

        // Assert
        Assert.NotNull(result);
        // Configuration should contain basic Home Assistant info
    }

    [Fact]
    public async Task GetEventsAsync_ShouldReturnAvailableEvents()
    {
        // Act
        var result = await _client.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should return array of available events
    }

    [Fact]
    public async Task GetServicesAsync_ShouldReturnAvailableServices()
    {
        // Act
        var result = await _client.GetServicesAsync();

        // Assert
        Assert.NotNull(result);
        // Should return object containing services by domain
    }

    [Fact]
    public async Task GetStatesAsync_ShouldReturnAllEntityStates()
    {
        // Act
        var result = await _client.GetStatesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // Verify we got actual state objects
        var firstState = result.First();
        Assert.NotNull(firstState.EntityId);
        Assert.NotNull(firstState.State);
    }

    [Fact]
    public async Task GetStateAsync_WithValidEntityId_ShouldReturnEntityState()
    {
        // Arrange - Use a sensor entity that should always exist
        var entityId = "sensor.time";

        // Act
        var result = await _client.GetStateAsync(entityId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entityId, result.EntityId);
        Assert.NotNull(result.State);
    }

    [Fact]
    public async Task GetStateAsync_WithInvalidEntityId_ShouldReturnNull()
    {
        // Act
        var result = await _client.GetStateAsync("invalid.nonexistent_entity");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetErrorLogAsync_ShouldReturnErrorLog()
    {
        // Act
        var result = await _client.GetErrorLogAsync();

        // Assert
        Assert.NotNull(result);
        // Error log should be a string
    }

    [Fact]
    public async Task GetCalendarsAsync_ShouldReturnCalendars()
    {
        // Act
        var result = await _client.GetCalendarsAsync();

        // Assert
        Assert.NotNull(result);
        // Should return array of calendar entities (may be empty)
    }

    [Fact]
    public async Task CallServiceAsync_WithValidService_ShouldExecuteSuccessfully()
    {
        // Arrange - Test with a harmless service call using correct payload format
        var request = new lucia.HomeAssistant.Models.ServiceCallRequest
        {
            ["notification_id"] = "test_integration_1234",
            ["title"] = "Integration Test",
            ["message"] = "This is a test notification from the integration test."
        };

        // Act - Call the persistent_notification.create service
        var result = await _client.CallServiceAsync("persistent_notification", "create", request);

        // Assert
        Assert.NotNull(result);
        // Most services return an empty array when they don't return data
        Assert.IsType<object[]>(result);
    }

    [Fact]
    public async Task FireEventAsync_WithTestEvent_ShouldFireEventSuccessfully()
    {
        // Arrange
        var eventData = new Dictionary<string, object>
        {
            ["test_data"] = "integration_test_value",
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        // Act
        var result = await _client.FireEventAsync("test_integration_event", eventData);

        // Assert
        Assert.NotNull(result);
        // Event firing should return success response
    }

    [Fact]
    public async Task RenderTemplateAsync_WithValidTemplate_ShouldReturnRenderedResult()
    {
        // Arrange
        var templateRequest = new lucia.HomeAssistant.Models.TemplateRenderRequest
        {
            Template = "{{ now().strftime('%Y-%m-%d %H:%M:%S') }}"
        };

        // Act
        var result = await _client.RenderTemplateAsync(templateRequest);

        // Assert
        Assert.NotNull(result);
        // Template should render current date/time in format like "2025-07-10 08:16:38"
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", result);
    }

    [Fact]
    public async Task GetHistoryAsync_WithValidParameters_ShouldReturnHistoricalData()
    {
        // Arrange
        var timestamp = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entityId = "sun.sun"; // Sun entity is always available

        // Act
        var result = await _client.GetHistoryAsync(
            timestamp,
            entityId,
            end_time: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );

        // Assert
        Assert.NotNull(result);
        // History should return array of historical states
    }

    [Fact]
    public async Task GetLogbookAsync_WithValidParameters_ShouldReturnLogbookEntries()
    {
        // Arrange
        var timestamp = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Act
        var result = await _client.GetLogbookAsync(
            timestamp,
            end_time: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );

        // Assert
        Assert.NotNull(result);
        // Logbook should return array of log entries
    }

    [Fact]
    public async Task CheckConfigAsync_ShouldReturnConfigValidationResult()
    {
        // Act
        var result = await _client.CheckConfigAsync();

        // Assert
        Assert.NotNull(result);
        // Config check should return validation status
    }

}
