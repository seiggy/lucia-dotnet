using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace lucia.HomeAssistant.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistant(this IServiceCollection services, Action<HomeAssistantOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IValidateOptions<HomeAssistantOptions>, HomeAssistantOptionsValidator>();
        
        services.AddHttpClient<IHomeAssistantClient, HomeAssistantClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.AccessToken}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            var handler = new HttpClientHandler();
            
            if (!options.ValidateSSL)
            {
                handler.ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    // Accept all certificates when SSL validation is disabled
                    return true;
                };
            }
            
            return handler;
        });

        return services;
    }
}