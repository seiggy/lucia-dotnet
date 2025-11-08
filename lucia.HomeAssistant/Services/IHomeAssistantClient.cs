using lucia.HomeAssistant.Models;

namespace lucia.HomeAssistant.Services;

public interface IHomeAssistantClient
{
    Task<IEnumerable<HomeAssistantState>> GetStatesAsync(CancellationToken cancellationToken = default);
    Task<HomeAssistantState?> GetStateAsync(string entityId, CancellationToken cancellationToken = default);
    Task<HomeAssistantState> SetStateAsync(string entityId, string state, Dictionary<string, object>? attributes = null, CancellationToken cancellationToken = default);
    Task<object[]> CallServiceAsync(string domain, string service, string? parameters = null, ServiceCallRequest? request = null, CancellationToken cancellationToken = default);
    Task<T> CallServiceAsync<T>(string domain, string service, string? parameters = null, ServiceCallRequest? request = null, CancellationToken cancellationToken = default);
    Task<T> RunTemplateAsync<T>(string jinjaTemplate, CancellationToken cancellationToken = default);
}