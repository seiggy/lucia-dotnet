using System.Net;
using System.Net.Sockets;
using System.Text;
using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReportsCompatibleLuciaAndOsUpdates()
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
                    {"hostname":"lucia","architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","luciaVersion":"1.2.3","rebootRequired":false,"network":{"ssid":"Home WiFi","signal":87},"os":{"name":"Ubuntu","versionId":"22.04","imageVersion":"1.1.0","jetsonLinuxVersion":"36.5.2"},"services":[]}
                    """;
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(Body)}\r\n"
                    + "Connection: close\r\n\r\n"
                    + Body);
                await stream.WriteAsync(response);
            });
            using var manager = new ApplianceManagerClient(socketPath);
            using var httpClient = new HttpClient(
                new StaticHttpMessageHandler(request =>
                {
                    var body = request.RequestUri?.AbsolutePath == "/latest"
                        ? """
                          {"html_url":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","assets":[{"name":"lucia-appliance-manifest.json","browser_download_url":"https://downloads.example/manifest.json"}]}
                          """
                        : """
                          {"schemaVersion":1,"version":"1.3.0","compatibility":{"architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","jetsonLinux":"36.5.2"},"channels":{"lucia":{},"os":{}}}
                          """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            body,
                            Encoding.UTF8,
                            "application/json"),
                    };
                }));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Appliance:ReleaseApi"] = "https://api.example/latest",
                })
                .Build();
            var service = new ApplianceUpdateService(
                httpClient,
                manager,
                configuration);

            var result = await service.CheckAsync(CancellationToken.None);

            Assert.True(result.Compatible);
            Assert.False(result.LuciaUpdateAvailable);
            Assert.False(result.OsUpdateAvailable);
            Assert.Equal("1.3.0", result.LatestVersion);
            Assert.Contains("attestation", result.Message);
            await serverTask;
        }
        finally
        {
            File.Delete(socketPath);
        }
    }
}
