using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace lucia.HomeAssistant.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistant(this IServiceCollection services, Action<HomeAssistantOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IValidateOptions<HomeAssistantOptions>, HomeAssistantOptionsValidator>();

        // Register per-request authorization handler so the token is always current
        // and set atomically on each HttpRequestMessage.
        services.AddTransient<HomeAssistantAuthorizationHandler>();

        services.AddHttpClient<IHomeAssistantClient, HomeAssistantClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            // Authorization is set per-request by HomeAssistantAuthorizationHandler.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
        })
        .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            var handler = new HttpClientHandler();

            if (!options.ValidateSSL)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            return handler;
        })
        .AddHttpMessageHandler<HomeAssistantAuthorizationHandler>();

        return services;
    }
}
