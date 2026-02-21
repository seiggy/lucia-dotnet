using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests;

/// <summary>
/// Provides shared Home Assistant client configuration for integration tests.
/// Reads HA_ENDPOINT and HA_TOKEN from environment variables or user secrets.
/// </summary>
public static class HomeAssistantTestConfig
{
    private static readonly Lazy<IConfiguration> s_configuration = new(() =>
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(HomeAssistantTestConfig).Assembly, optional: true)
            .Build());

    public static IConfiguration Configuration => s_configuration.Value;

    public static string? Endpoint => Configuration["HA_ENDPOINT"];
    public static string? Token => Configuration["HA_TOKEN"];

    /// <summary>
    /// Returns true if both HA_ENDPOINT and HA_TOKEN are configured.
    /// </summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Token);

    /// <summary>
    /// Creates a configured HomeAssistantClient from user secrets / env vars.
    /// Returns null if HA_ENDPOINT or HA_TOKEN are not set.
    /// </summary>
    public static (HomeAssistantClient? Client, IServiceProvider? ServiceProvider) CreateClient(
        int timeoutSeconds = 30,
        bool validateSsl = false)
    {
        if (!IsConfigured)
            return (null, null);

        var services = new ServiceCollection();
        services.Configure<HomeAssistantOptions>(options =>
        {
            options.BaseUrl = Endpoint!;
            options.AccessToken = Token!;
            options.TimeoutSeconds = timeoutSeconds;
            options.ValidateSSL = validateSsl;
        });

        services.AddHttpClient<HomeAssistantClient>((sp, client) =>
            {
                client.BaseAddress = new Uri(Endpoint!.TrimEnd('/'));
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Token}");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<HomeAssistantClient>();
        return (client, serviceProvider);
    }
}
