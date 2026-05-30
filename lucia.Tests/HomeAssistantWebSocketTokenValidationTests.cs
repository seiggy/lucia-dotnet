using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;

namespace lucia.Tests;

/// <summary>
/// Verifies that SendWebSocketCommandAsync rejects empty/missing tokens
/// with a clear local error before any network connection is attempted.
/// </summary>
public class HomeAssistantWebSocketTokenValidationTests
{
    private static HomeAssistantClient BuildClientWithToken(string accessToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = "http://homeassistant.local:8123";
            options.AccessToken = accessToken;
        });
        services.AddHttpClient<HomeAssistantClient>();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<HomeAssistantClient>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAreaRegistryAsync_WithEmptyToken_ThrowsInvalidOperationException(string token)
    {
        var client = BuildClientWithToken(token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAreaRegistryAsync(CancellationToken.None));

        Assert.Contains("AccessToken", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetFloorRegistryAsync_WithEmptyToken_ThrowsInvalidOperationException(string token)
    {
        var client = BuildClientWithToken(token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetFloorRegistryAsync(CancellationToken.None));

        Assert.Contains("AccessToken", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetEntityRegistryAsync_WithEmptyToken_ThrowsInvalidOperationException(string token)
    {
        var client = BuildClientWithToken(token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetEntityRegistryAsync(CancellationToken.None));

        Assert.Contains("AccessToken", ex.Message);
    }
}
