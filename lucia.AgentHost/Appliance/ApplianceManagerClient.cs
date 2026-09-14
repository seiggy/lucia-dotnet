using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

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
        using var response = await _httpClient
            .GetAsync("/v1/status", cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceStatusResponse>(cancellationToken)
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
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RebootHostAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync("/v1/host/reboot", content: null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ApplianceTelemetryStatus> GetTelemetryAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync("/v1/telemetry", cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceTelemetryStatus>(cancellationToken)
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
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceTelemetryStatus>(
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty telemetry response.");
    }

    public async Task<ApplianceUpdateOperationStatus> StartUpdateAsync(
        string channel,
        string tag,
        string operationId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsJsonAsync(
                $"/v1/updates/{Uri.EscapeDataString(channel)}/apply",
                new ApplianceUpdateRequest(tag, operationId),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceUpdateOperationStatus>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty update response.");
    }

    public async Task<ApplianceUpdateOperationStatus> StartRollbackAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsJsonAsync(
                $"/v1/updates/{Uri.EscapeDataString(channel)}/rollback",
                new ApplianceUpdateRequest(string.Empty),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceUpdateOperationStatus>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty rollback response.");
    }

    public async Task<ApplianceUpdateOperationStatus> GetUpdateOperationAsync(
        string? operationId,
        CancellationToken cancellationToken)
    {
        var path = operationId is null
            ? "/v1/updates/operation"
            : $"/v1/updates/operations/{Uri.EscapeDataString(operationId)}";
        using var response = await _httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<ApplianceUpdateOperationStatus>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Appliance manager returned an empty update operation.");
    }

    public Task<ApplianceUpdateOperationStatus> GetUpdateOperationAsync(
        CancellationToken cancellationToken) =>
        GetUpdateOperationAsync(null, cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var message = response.ReasonPhrase
            ?? $"Appliance manager returned {(int)response.StatusCode}.";
        if (response.Content.Headers.ContentType?.MediaType
                is "application/json" or "application/problem+json"
            && !string.IsNullOrWhiteSpace(content))
        {
            message = ReadProblemMessage(content) ?? message;
        }

        throw new HttpRequestException(
            message,
            inner: null,
            response.StatusCode);
    }

    private static string? ReadProblemMessage(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
