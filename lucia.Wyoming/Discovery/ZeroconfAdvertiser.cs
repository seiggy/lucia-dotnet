using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lucia.Wyoming.Wyoming;

namespace lucia.Wyoming.Discovery;

/// <summary>
/// Advertises the Wyoming server via mDNS/Zeroconf for auto-discovery by Home Assistant.
/// Announces the _wyoming._tcp service type.
/// </summary>
public sealed class ZeroconfAdvertiser : IHostedService, IDisposable
{
    private readonly WyomingOptions _options;
    private readonly ILogger<ZeroconfAdvertiser> _logger;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _serviceProfile;

    public ZeroconfAdvertiser(
        IOptions<WyomingOptions> options,
        ILogger<ZeroconfAdvertiser> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _serviceProfile = new ServiceProfile(
                instanceName: _options.ServiceName,
                serviceName: "_wyoming._tcp",
                port: (ushort)_options.Port);

            _serviceProfile.AddProperty("version", "1.0");
            _serviceProfile.AddProperty("type", "voice");

            _serviceDiscovery = new ServiceDiscovery();
            _serviceDiscovery.Advertise(_serviceProfile);

            _logger.LogInformation(
                "Wyoming service advertised via mDNS: {ServiceName} on port {Port}",
                _options.ServiceName,
                _options.Port);
        }
        catch (Exception ex)
        {
            // mDNS may fail in containers or restricted network environments.
            _logger.LogWarning(
                ex,
                "Failed to advertise Wyoming service via mDNS. Service will still accept direct connections on port {Port}",
                _options.Port);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_serviceDiscovery is not null && _serviceProfile is not null)
        {
            _serviceDiscovery.Unadvertise(_serviceProfile);
        }

        _logger.LogInformation("Wyoming mDNS advertisement stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _serviceDiscovery?.Dispose();
    }
}
