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

    async Task<object[]> IHomeAssistantClient.CallServiceAsync(string domain, string service, ServiceCallRequest? request, CancellationToken cancellationToken)
    {
        return await CallServiceAsync(domain, service, request, cancellationToken);
    }
}