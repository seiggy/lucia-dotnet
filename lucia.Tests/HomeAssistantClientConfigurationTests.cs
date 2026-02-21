using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Extensions;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public class HomeAssistantClientConfigurationTests
{
    [Fact]
    public void AddHomeAssistant_ShouldRegisterServicesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHomeAssistant(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = "test-token";
            options.TimeoutSeconds = 45;
            options.ValidateSSL = false;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var client = serviceProvider.GetService<IHomeAssistantClient>();
        Assert.NotNull(client);
        Assert.IsType<HomeAssistantClient>(client);

        var options = serviceProvider.GetRequiredService<IOptions<HomeAssistantOptions>>();
        Assert.Equal("http://homeassistant.local:8123", options.Value.BaseUrl);
        Assert.Equal("test-token", options.Value.AccessToken);
        Assert.Equal(45, options.Value.TimeoutSeconds);
        Assert.False(options.Value.ValidateSSL);
    }

    [Fact]
    public void HomeAssistantOptions_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new HomeAssistantOptions();

        // Assert
        Assert.Equal(string.Empty, options.BaseUrl);
        Assert.Equal(string.Empty, options.AccessToken);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.True(options.ValidateSSL);
    }

    [Fact]
    public void HomeAssistantOptions_SectionName_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("HomeAssistant", HomeAssistantOptions.SectionName);
    }
}