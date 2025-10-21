using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;

namespace lucia.Tests;

public class HomeAssistantErrorHandlingTests
{
    private readonly GeneratedHomeAssistantClient _clientWithInvalidToken;
    private readonly GeneratedHomeAssistantClient _clientWithInvalidUrl;

    public HomeAssistantErrorHandlingTests()
    {
        // Client with invalid token
        var servicesInvalidToken = new ServiceCollection();
        servicesInvalidToken.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = "invalid-token";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        servicesInvalidToken.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProviderInvalidToken = servicesInvalidToken.BuildServiceProvider();
        _clientWithInvalidToken = serviceProviderInvalidToken.GetRequiredService<GeneratedHomeAssistantClient>();

        // Client with invalid URL
        var servicesInvalidUrl = new ServiceCollection();
        servicesInvalidUrl.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://nonexistent.local:8123";
            options.AccessToken = "any-token";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        servicesInvalidUrl.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProviderInvalidUrl = servicesInvalidUrl.BuildServiceProvider();
        _clientWithInvalidUrl = serviceProviderInvalidUrl.GetRequiredService<GeneratedHomeAssistantClient>();
    }

    [Fact]
    public async Task GetStatesAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken.GetStatesAsync());

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task GetStateAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken.GetStateAsync("sensor.time"));

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task GetConfigAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken.GetConfigAsync());

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task CallServiceAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken.CallServiceAsync("homeassistant", "restart"));

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task GetStatesAsync_WithInvalidUrl_ShouldThrowHttpRequestException()
    {
        // Act & Assert
        // When connecting to a nonexistent domain, the HttpClient times out after 5 seconds
        // This can throw either TaskCanceledException or HttpRequestException depending on timing
        var exception = await Record.ExceptionAsync(
            () => _clientWithInvalidUrl.GetStatesAsync());

        // Verify it's a timeout-related exception
        Assert.NotNull(exception);
        Assert.True(
            exception is HttpRequestException || exception is TaskCanceledException,
            $"Expected HttpRequestException or TaskCanceledException, but got {exception.GetType().Name}");
    }

    [Fact]
    public async Task GetStateAsync_WithInvalidUrl_ShouldThrowHttpRequestException()
    {
        // Act & Assert
        // When connecting to a nonexistent domain, the HttpClient times out after 5 seconds
        // This can throw either TaskCanceledException or HttpRequestException depending on timing
        var exception = await Record.ExceptionAsync(
            () => _clientWithInvalidUrl.GetStateAsync("sensor.time"));

        // Verify it's a timeout-related exception
        Assert.NotNull(exception);
        Assert.True(
            exception is HttpRequestException || exception is TaskCanceledException,
            $"Expected HttpRequestException or TaskCanceledException, but got {exception.GetType().Name}");
    }

    [Fact]
    public async Task CallServiceAsync_WithInvalidDomain_ShouldThrowHttpRequestException()
    {
        // This test uses a valid client but invalid service domain
        var services = new ServiceCollection();
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = "<YOUR_HA_TOKEN>";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        services.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<GeneratedHomeAssistantClient>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CallServiceAsync("nonexistent_domain", "nonexistent_service"));

        Assert.Contains("400", exception.Message);
    }

    [Fact]
    public async Task GetStateAsync_WithNonExistentEntity_ShouldReturnNull()
    {
        // This test uses a valid client but requests a non-existent entity
        var services = new ServiceCollection();
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = "<YOUR_HA_TOKEN>";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        services.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<GeneratedHomeAssistantClient>();

        // Act
        var result = await client.GetStateAsync("nonexistent.entity_that_does_not_exist");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FireEventAsync_WithInvalidEventType_ShouldStillSucceed()
    {
        // Custom events can have any name, so this should succeed
        var services = new ServiceCollection();
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = "<YOUR_HA_TOKEN>";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        services.AddHttpClient<GeneratedHomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<GeneratedHomeAssistantClient>();

        var eventData = new Dictionary<string, object>
        {
            ["test"] = "value"
        };

        // Act
        var result = await client.FireEventAsync("test_custom_event_name", eventData);

        // Assert
        Assert.NotNull(result);
    }
}