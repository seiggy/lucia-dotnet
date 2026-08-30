using System.Net.Sockets;
using System.Text;
using lucia.AgentHost.Appliance;

namespace lucia.Tests.Appliance;

public sealed class ApplianceManagerClientTests
{
    [Fact]
    public async Task GetStatusAsync_ReadsStatusFromUnixSocket()
    {
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-manager-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen();

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptAsync();
                await using var stream = new NetworkStream(
                    accepted,
                    ownsSocket: false);
                var requestBuffer = new byte[4096];
                _ = await stream.ReadAsync(requestBuffer);

                const string Body =
                    """
                    {"hostname":"lucia","architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","luciaVersion":"1.2.3","storageBytes":2000000000000,"rebootRequired":false,"network":{"ssid":"Home WiFi","signal":87},"os":{"name":"Ubuntu","versionId":"22.04","imageVersion":"1.1.0","jetsonLinuxVersion":"36.5.2"},"services":[{"id":"agenthost","activeState":"active","unitFileState":"enabled"}]}
                    """;
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(Body)}\r\n"
                    + "Connection: close\r\n\r\n"
                    + Body);
                await stream.WriteAsync(response);
            });

            using var client = new ApplianceManagerClient(socketPath);
            var status = await client.GetStatusAsync(CancellationToken.None);

            Assert.Equal("lucia", status.Hostname);
            Assert.Equal("1.2.3", status.LuciaVersion);
            Assert.Equal("active", Assert.Single(status.Services).ActiveState);
            await serverTask;
        }
        finally
        {
            File.Delete(socketPath);
        }
    }
}
