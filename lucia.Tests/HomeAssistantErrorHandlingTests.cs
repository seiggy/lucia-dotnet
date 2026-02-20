using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;

namespace lucia.Tests;

public class HomeAssistantErrorHandlingTests
{
    private readonly HomeAssistantClient? _clientWithInvalidToken;
    private readonly HomeAssistantClient? _clientWithInvalidUrl;

    public HomeAssistantErrorHandlingTests()
    {
        // Client with invalid token — uses the configured endpoint but a bad token
        if (HomeAssistantTestConfig.IsConfigured)
        {
            var servicesInvalidToken = new ServiceCollection();
            servicesInvalidToken.Configure<HomeAssistantOptions>(options =>
            {
                options.BaseUrl = HomeAssistantTestConfig.Endpoint!;
                options.AccessToken = "invalid-token";
                options.TimeoutSeconds = 5;
                options.ValidateSSL = false;
            });
            servicesInvalidToken.AddHttpClient<HomeAssistantClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                });

            var serviceProviderInvalidToken = servicesInvalidToken.BuildServiceProvider();
            _clientWithInvalidToken = serviceProviderInvalidToken.GetRequiredService<HomeAssistantClient>();
        }

        // Client with invalid URL
        var servicesInvalidUrl = new ServiceCollection();
        servicesInvalidUrl.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://nonexistent.local:8123";
            options.AccessToken = "any-token";
            options.TimeoutSeconds = 5;
            options.ValidateSSL = false;
        });
        servicesInvalidUrl.AddHttpClient<HomeAssistantClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProviderInvalidUrl = servicesInvalidUrl.BuildServiceProvider();
        _clientWithInvalidUrl = serviceProviderInvalidUrl.GetRequiredService<HomeAssistantClient>();
    }

    private void SkipIfNotConfigured() =>
        Skip.If(!HomeAssistantTestConfig.IsConfigured, "HA_ENDPOINT and HA_TOKEN not configured.");

    [SkippableFact]
    public async Task GetStatesAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        SkipIfNotConfigured();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken!.GetStatesAsync());
        Assert.Contains("401", exception.Message);
    }

    [SkippableFact]
    public async Task GetStateAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        SkipIfNotConfigured();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken!.GetStateAsync("sensor.time"));
        Assert.Contains("401", exception.Message);
    }

    [SkippableFact]
    public async Task GetConfigAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        SkipIfNotConfigured();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken!.GetConfigAsync());
        Assert.Contains("401", exception.Message);
    }

    [SkippableFact]
    public async Task CallServiceAsync_WithInvalidToken_ShouldThrowUnauthorized()
    {
        SkipIfNotConfigured();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _clientWithInvalidToken!.CallServiceRawAsync("homeassistant", "restart"));
        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task GetStatesAsync_WithInvalidUrl_ShouldThrowHttpRequestException()
    {
        var exception = await Record.ExceptionAsync(
            () => _clientWithInvalidUrl!.GetStatesAsync());

        Assert.NotNull(exception);
        Assert.True(
            exception is HttpRequestException || exception is TaskCanceledException,
            $"Expected HttpRequestException or TaskCanceledException, but got {exception.GetType().Name}");
    }

    [Fact]
    public async Task GetStateAsync_WithInvalidUrl_ShouldThrowHttpRequestException()
    {
        var exception = await Record.ExceptionAsync(
            () => _clientWithInvalidUrl!.GetStateAsync("sensor.time"));

        Assert.NotNull(exception);
        Assert.True(
            exception is HttpRequestException || exception is TaskCanceledException,
            $"Expected HttpRequestException or TaskCanceledException, but got {exception.GetType().Name}");
    }

    [SkippableFact]
    public async Task CallServiceAsync_WithInvalidDomain_ShouldThrowHttpRequestException()
    {
        SkipIfNotConfigured();
        var (client, _) = HomeAssistantTestConfig.CreateClient(timeoutSeconds: 5);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client!.CallServiceRawAsync("nonexistent_domain", "nonexistent_service"));
        Assert.Contains("400", exception.Message);
    }

    [SkippableFact]
    public async Task GetStateAsync_WithNonExistentEntity_ShouldReturnNull()
    {
        SkipIfNotConfigured();
        var (client, _) = HomeAssistantTestConfig.CreateClient(timeoutSeconds: 5);

        var result = await client!.GetStateAsync("nonexistent.entity_that_does_not_exist");
        Assert.Null(result);
    }

    [SkippableFact]
    public async Task FireEventAsync_WithInvalidEventType_ShouldStillSucceed()
    {
        SkipIfNotConfigured();
        var (client, _) = HomeAssistantTestConfig.CreateClient(timeoutSeconds: 5);

        var eventData = new Dictionary<string, object>
        {
            ["test"] = "value"
        };

        var result = await client!.FireEventAsync("test_custom_event_name", eventData);
        Assert.NotNull(result);
    }
}