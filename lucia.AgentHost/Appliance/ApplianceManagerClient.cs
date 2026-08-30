using System.Net.Http.Json;
using System.Net.Sockets;

namespace lucia.AgentHost.Appliance;

public sealed class ApplianceManagerClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public ApplianceManagerClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);
                var ownsSocket = true;
                try
                {
                    await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(socketPath),
                            cancellationToken)
                        .ConfigureAwait(false);
                    ownsSocket = false;
                    return new NetworkStream(socket, ownsSocket: true);
                }
                finally
                {
                    if (ownsSocket)
                    {
                        socket.Dispose();
                    }
                }
            },
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
        };
    }

    public async Task<ApplianceStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        return await _httpClient
            .GetFromJsonAsync<ApplianceStatusResponse>(
                "/v1/status",
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty status response.");
    }

    public async Task RestartServiceAsync(
        string service,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync(
                $"/v1/services/{Uri.EscapeDataString(service)}/restart",
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RebootHostAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync("/v1/host/reboot", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ApplianceTelemetryStatus> GetTelemetryAsync(
        CancellationToken cancellationToken)
    {
        return await _httpClient
            .GetFromJsonAsync<ApplianceTelemetryStatus>(
                "/v1/telemetry",
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty telemetry response.");
    }

    public async Task<ApplianceTelemetryStatus> UpdateTelemetryAsync(
        ApplianceTelemetryConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PutAsJsonAsync("/v1/telemetry", request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<ApplianceTelemetryStatus>(
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty telemetry response.");
    }

    public void Dispose() => _httpClient.Dispose();
}
