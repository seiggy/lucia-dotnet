using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateServiceTests
{
    [Fact]
    public async Task InstallAsync_QueuesExactTagOutsideRequestLifetime()
    {
        var stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        var requestedUri =
            new TaskCompletionSource<Uri>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var stagingFailed =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new ApplianceManagerClient(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sock"));
        var staging = new ApplianceUpdateStagingStore(
            stagingPath,
            NullLogger<ApplianceUpdateStagingStore>.Instance);
        using var service = new ApplianceUpdateService(
            new HttpClient(
                new StaticHttpMessageHandler(request =>
                {
                    requestedUri.SetResult(request.RequestUri!);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}"),
                    };
                })),
            manager,
            staging,
            NullLogger<ApplianceUpdateService>.Instance);
        using var watcher = new FileSystemWatcher(stagingPath, "operation.json")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        void ObserveStagingStatus()
        {
            if (staging.GetStatus().Status == "failed")
            {
                stagingFailed.TrySetResult();
            }
        }
        watcher.Changed += (_, _) => ObserveStagingStatus();
        watcher.Created += (_, _) => ObserveStagingStatus();
        watcher.Renamed += (_, _) => ObserveStagingStatus();
        try
        {
            var operation = await service.InstallAsync(
                "lucia",
                "v1.5.0",
                CancellationToken.None);

            Assert.Equal("queued", operation.Status);
            Assert.Equal(
                new Uri(
                    "https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.5.0"),
                await requestedUri.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await stagingFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("failed", staging.GetStatus().Status);
        }
        finally
        {
            Directory.Delete(stagingPath, recursive: true);
        }
    }

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

    [Fact]
    public void EnsureStagingCapacity_RejectsInsufficientFreeSpace()
    {
        Assert.Throws<IOException>(
            () => ApplianceUpdateService.EnsureStagingCapacity(
                Path.GetTempPath(),
                long.MaxValue / 2));
    }

    [Fact]
    public void ValidateParts_RejectsChannelSizeMismatch()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "bytes": 1,
              "parts": [{
                "name": "part.zst",
                "bytes": 2,
                "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "url": "https://github.com/seiggy/lucia-dotnet/releases/download/v1.5.0/part.zst"
              }]
            }
            """);

        Assert.Throws<InvalidDataException>(
            () => ApplianceUpdateService.ValidateParts(
                document.RootElement,
                "v1.5.0"));
    }

    [Theory]
    [InlineData(
        "jetson-orin-nano-super-p3767-0005",
        "36.5.2",
        true)]
    [InlineData("unsupported-board", "36.5.2", false)]
    [InlineData(
        "jetson-orin-nano-super-p3767-0005",
        "99.0.0",
        false)]
    public async Task CheckAsync_ReportsUpdatesIndependentlyOfCompatibility(
        string board,
        string jetsonLinux,
        bool expectedCompatible)
    {
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-manager-{Guid.NewGuid():N}.sock");
        var stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
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
                          {"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"v1.3.0","attestationBundleUrl":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl","version":"1.3.0","releaseApi":"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.3.0","releaseNotesUrl":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","compatibility":{"architecture":"arm64","board":"BOARD","jetsonLinux":"JETSON","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1,"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"channels":{"lucia":{"version":"1.3.0","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","requires":{"jetsonLinux":"36.5.2","layoutVersion":1,"dataSchemaVersion":1,"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34","reboot":false},"parts":[{"name":"lucia.tar.zst","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia.tar.zst"}]},"os":{"version":"1.4.0","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","requires":{"minimumLuciaVersion":"1.2.3","layoutVersion":1,"jetsonLinux":"36.5.2","redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34","reboot":true},"parts":[{"name":"os.tar.zst","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/os.tar.zst"}]}}}
                          """
                            .Replace("BOARD", board, StringComparison.Ordinal)
                            .Replace(
                                "JETSON",
                                jetsonLinux,
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
                manager,
                new ApplianceUpdateStagingStore(
                    stagingPath,
                    NullLogger<ApplianceUpdateStagingStore>.Instance),
                NullLogger<ApplianceUpdateService>.Instance);

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
            Assert.Equal("v1.3.0", result.ReleaseTag);
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
            Directory.Delete(stagingPath, recursive: true);
        }
    }
}
