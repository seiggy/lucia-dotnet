using System.Text.Json;
using lucia.HomeAssistant.Attributes;
using lucia.HomeAssistant.Models;

namespace lucia.HomeAssistant.Services;

[HomeAssistantApi(ConfigSectionName = "HomeAssistant")]
public partial class GeneratedHomeAssistantClient : IHomeAssistantClient
{
    // The source generator will create the constructor and all API methods
    
    // Implement the interface methods by delegating to generated methods
    async Task<IEnumerable<HomeAssistantState>> IHomeAssistantClient.GetStatesAsync(CancellationToken cancellationToken)
    {
        return await GetStatesAsync(cancellationToken);
    }

    async Task<HomeAssistantState?> IHomeAssistantClient.GetStateAsync(string entityId, CancellationToken cancellationToken)
    {
        return await GetStateAsync(entityId, cancellationToken);
    }

    async Task<HomeAssistantState> IHomeAssistantClient.SetStateAsync(string entityId, string state, Dictionary<string, object>? attributes, CancellationToken cancellationToken)
    {
        var payload = new
        {
            state,
            attributes = attributes ?? new Dictionary<string, object>()
        };

        return await SetStateAsync(entityId, payload, cancellationToken);
    }

    async Task<T> IHomeAssistantClient.CallServiceAsync<T>(string domain, string service, string? parameters,
        ServiceCallRequest? request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain, nameof(domain));
        ArgumentNullException.ThrowIfNull(service, nameof(service));
        
        if (string.IsNullOrEmpty(parameters))
            parameters = string.Empty;
        var result = await CallServiceAsync(domain, service, request, parameters, cancellationToken);
        return JsonSerializer.Deserialize<T>(result) ?? default;
    }

    async Task<object[]> IHomeAssistantClient.CallServiceAsync(string domain, string service, string? parameters, ServiceCallRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(parameters))
            parameters = string.Empty;
        var result = await CallServiceAsync(domain, service, request, parameters, cancellationToken);
        return JsonSerializer.Deserialize<object[]>(result) ?? Array.Empty<object>();
    }

    async Task<T> IHomeAssistantClient.RunTemplateAsync<T>(string jinjaTemplate, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jinjaTemplate, nameof(jinjaTemplate));

        var request = new TemplateRenderRequest
        {
            Template = jinjaTemplate
        };

        var renderedTemplate = await RenderTemplateAsync(request, cancellationToken);

        // If the caller wants a string, return the raw response
        if (typeof(T) == typeof(string))
        {
            return (T)(object)renderedTemplate;
        }

        // Otherwise, deserialize the JSON response into the requested type
        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(renderedTemplate, _jsonOptions);
            if (deserialized is null)
            {
                throw new InvalidOperationException($"Template response could not be deserialized to type '{typeof(T).Name}'.");
            }

            return deserialized;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize template response to type '{typeof(T).Name}'.", ex);
        }
    }
}