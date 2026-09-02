using System.Net;
using System.Net.Sockets;
using System.Text;
using lucia.AgentHost.Appliance;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateServiceTests
{
    [Theory]
    [InlineData("http://github.com/seiggy/lucia-dotnet/releases/download/v1/manifest.json")]
    [InlineData("https://example.com/seiggy/lucia-dotnet/releases/download/v1/lucia-appliance-manifest.json")]
    [InlineData("https://github.com/other/repo/releases/download/v1/lucia-appliance-manifest.json")]
    [InlineData("https://github.com/seiggy/lucia-dotnet/releases/download/v1/other.json")]
    public void ParseManifestUri_RejectsUntrustedUrl(string value)
    {
        Assert.Throws<InvalidDataException>(
            () => ApplianceUpdateService.ParseManifestUri(value));
    }

    [Theory]
    [InlineData("jetson-orin-nano-super-p3767-0005", true)]
    [InlineData("unsupported-board", false)]
    public async Task CheckAsync_ReportsUpdatesIndependentlyOfCompatibility(
        string board,
        bool expectedCompatible)
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
                    {"hostname":"lucia","architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","luciaVersion":"1.2.3","storageBytes":2000000000000,"rebootRequired":false,"network":{"ssid":"Home WiFi","signal":87},"os":{"name":"Ubuntu","versionId":"22.04","imageVersion":"1.1.0","jetsonLinuxVersion":"36.5.2"},"services":[]}
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
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(
                new StaticHttpMessageHandler(request =>
                {
                    requestedUris.Add(request.RequestUri!);
                    var body = request.RequestUri == new Uri(
                        "https://api.github.com/repos/seiggy/lucia-dotnet/releases/latest")
                        ? """
                          {"tag_name":"v1.3.0","html_url":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","assets":[{"name":"lucia-appliance-manifest.json","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-manifest.json"},{"name":"lucia-appliance-attestations.jsonl","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl"}]}
                          """
                        : """
                          {"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"v1.3.0","attestationBundleUrl":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl","version":"1.3.0","compatibility":{"architecture":"arm64","board":"BOARD","jetsonLinux":"36.5.2","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1},"channels":{"lucia":{"version":"1.3.0","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","requires":{"jetsonLinux":"36.5.2","layoutVersion":1,"dataSchemaVersion":1},"parts":[{"name":"lucia.tar.zst","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia.tar.zst"}]},"os":{"version":"1.4.0","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","requires":{"minimumLuciaVersion":"1.2.3","layoutVersion":1},"parts":[{"name":"os.tar.zst","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/os.tar.zst"}]}}}
                          """.Replace(
                              "BOARD",
                              board,
                              StringComparison.Ordinal);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            body,
                            Encoding.UTF8,
                            "application/json"),
                    };
                }));
            var service = new ApplianceUpdateService(
                httpClient,
                manager);

            var result = await service.CheckAsync(CancellationToken.None);

            Assert.Equal(expectedCompatible, result.Compatible);
            Assert.Equal(expectedCompatible, result.LuciaCompatible);
            Assert.Equal(expectedCompatible, result.OsCompatible);
            Assert.True(result.LuciaNewerDiscovered);
            Assert.True(result.OsNewerDiscovered);
            Assert.Equal(expectedCompatible, result.LuciaUpdateAvailable);
            Assert.Equal(expectedCompatible, result.OsUpdateAvailable);
            Assert.Equal("1.3.0", result.LatestLuciaVersion);
            Assert.Equal("1.4.0", result.LatestOsVersion);
            Assert.Contains(
                expectedCompatible ? "ready" : "not compatible",
                result.Message);
            Assert.Equal(
                [
                    new Uri("https://api.github.com/repos/seiggy/lucia-dotnet/releases/latest"),
                    new Uri("https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-manifest.json"),
                ],
                requestedUris);
            await serverTask;
        }
        finally
        {
            File.Delete(socketPath);
        }
    }
}
