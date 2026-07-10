using lucia.HomeAssistant.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace lucia.HomeAssistant.Services;

/// <summary>
/// Adds a per-request <c>Authorization</c> header so the token from
/// <see cref="HomeAssistantOptions.AccessToken"/> is always current and applied
/// atomically. This replaces the non-atomic Remove+Add pattern on
/// <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> that caused
/// intermittent 401s under concurrent requests.
/// </summary>
public sealed class HomeAssistantAuthorizationHandler(IOptionsMonitor<HomeAssistantOptions> optionsMonitor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = optionsMonitor.CurrentValue.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
