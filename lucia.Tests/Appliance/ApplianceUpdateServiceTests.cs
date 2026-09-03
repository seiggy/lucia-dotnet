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
            var stagingTask = service.StagingTask;
            Assert.NotNull(stagingTask);
            await stagingTask.WaitAsync(TimeSpan.FromSeconds(5));
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
                long.MaxValue / 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10_485_761L)]
    public async Task ReadManifestAsync_RejectsUnboundedResponses(
        long? contentLength)
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = contentLength;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ApplianceUpdateService.ReadManifestAsync(
                content,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3-preview", "1.2.3", true)]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.2.2", "1.2.3", false)]
    [InlineData("invalid", "1.2.3", false)]
    [InlineData("1.2.3", "invalid", false)]
    public void MeetsMinimumVersion_RequiresTwoValidVersions(
        string current,
        string minimum,
        bool expected)
    {
        Assert.Equal(
            expected,
            ApplianceUpdateService.MeetsMinimumVersion(current, minimum));
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
        "1.2.3",
        "1.1.0",
        true,
        true,
        true,
        true)]
    [InlineData(
        "unsupported-board",
        "36.5.2",
        "1.2.3",
        "1.1.0",
        false,
        false,
        false,
        false)]
    [InlineData(
        "jetson-orin-nano-super-p3767-0005",
        "99.0.0",
        "1.2.3",
        "1.1.0",
        true,
        false,
        true,
        false)]
    [InlineData(
        "jetson-orin-nano-super-p3767-0005",
        "36.5.2",
        "1.3.0",
        "1.4.0",
        true,
        true,
        false,
        false)]
    [InlineData(
        "jetson-orin-nano-super-p3767-0005",
        "99.0.0",
        "1.3.0",
        "1.1.0",
        true,
        false,
        false,
        false)]
    public async Task CheckAsync_ReportsUpdatesIndependentlyOfCompatibility(
        string board,
        string jetsonLinux,
        string currentLuciaVersion,
        string currentOsVersion,
        bool expectedLuciaCompatible,
        bool expectedOsCompatible,
        bool expectedLuciaUpdate,
        bool expectedOsUpdate)
    {
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-manager-{Guid.NewGuid():N}.sock");
        var stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        var runtimeInfoPath = Path.Combine(stagingPath, "runtime.json");
        await File.WriteAllTextAsync(
            runtimeInfoPath,
            """
            {"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"}
            """);
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

                var body =
                    """
                    {"hostname":"lucia","architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","luciaVersion":"CURRENT_LUCIA","storageBytes":2000000000000,"rebootRequired":false,"network":{"ssid":"Home WiFi","signal":87},"os":{"name":"Ubuntu","versionId":"22.04","imageVersion":"CURRENT_OS","jetsonLinuxVersion":"36.5.2"},"services":[]}
                    """
                    .Replace(
                        "CURRENT_LUCIA",
                        currentLuciaVersion,
                        StringComparison.Ordinal)
                    .Replace(
                        "CURRENT_OS",
                        currentOsVersion,
                        StringComparison.Ordinal);
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + "Connection: close\r\n\r\n"
                    + body);
                await stream.WriteAsync(response);
            });
            using var manager = new ApplianceManagerClient(socketPath);
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(
                new StaticHttpMessageHandler(request =>
                {
                    requestedUris.Add(request.RequestUri!);
                    var body = request.RequestUri == new Uri(
                        "https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=1")
                        ? """
                          [{"tag_name":"v1.3.0","html_url":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","draft":false,"prerelease":false,"assets":[{"name":"lucia-appliance-manifest.json","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-manifest.json"},{"name":"lucia-appliance-attestations.jsonl","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl"}]}]
                          """
                        : """
                          {"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"v1.3.0","attestationBundleUrl":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl","version":"1.3.0","releaseApi":"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.3.0","releaseNotesUrl":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","compatibility":{"architecture":"arm64","board":"BOARD","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1},"channels":{"lucia":{"version":"1.3.0","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","requires":{"layoutVersion":1,"dataSchemaVersion":1,"source":{"jetsonLinux":"36.5.2","redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"target":{"jetsonLinux":"36.6.0","redis":"8.3.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":false},"parts":[{"name":"lucia.tar.zst","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia.tar.zst"}]},"os":{"version":"1.4.0","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","requires":{"minimumLuciaVersion":"1.2.3","layoutVersion":1,"source":{"jetsonLinux":"JETSON","redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"target":{"jetsonLinux":"36.6.0","redis":"8.3.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":true},"parts":[{"name":"os.tar.zst","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/os.tar.zst"}]}}}
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
                NullLogger<ApplianceUpdateService>.Instance,
                runtimeInfoPath);

            var result = await service.CheckAsync(CancellationToken.None);

            Assert.Equal(
                expectedLuciaCompatible || expectedOsCompatible,
                result.Compatible);
            Assert.Equal(expectedLuciaCompatible, result.LuciaCompatible);
            Assert.Equal(expectedOsCompatible, result.OsCompatible);
            Assert.Equal(
                currentLuciaVersion == "1.2.3",
                result.LuciaNewerDiscovered);
            Assert.Equal(
                currentOsVersion == "1.1.0",
                result.OsNewerDiscovered);
            Assert.Equal(
                expectedLuciaUpdate,
                result.LuciaUpdateAvailable);
            Assert.Equal(expectedOsUpdate, result.OsUpdateAvailable);
            Assert.Equal("1.3.0", result.LatestLuciaVersion);
            Assert.Equal("1.4.0", result.LatestOsVersion);
            Assert.Equal("v1.3.0", result.ReleaseTag);
            if (expectedLuciaUpdate || expectedOsUpdate)
            {
                Assert.Contains("ready", result.Message);
            }
            else if (!expectedLuciaCompatible && !expectedOsCompatible)
            {
                Assert.Contains("No compatible newer", result.Message);
            }
            else if (result.LuciaNewerDiscovered || result.OsNewerDiscovered)
            {
                Assert.Contains("No compatible newer", result.Message);
            }
            else
            {
                Assert.Null(result.Message);
            }
            Assert.Equal(
                [
                    new Uri("https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=1"),
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

    [Fact]
    public async Task CheckAsync_PrefersNewestCompatibleStableRelease()
    {
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-manager-{Guid.NewGuid():N}.sock");
        var stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        var runtimeInfoPath = Path.Combine(stagingPath, "runtime.json");
        await File.WriteAllTextAsync(
            runtimeInfoPath,
            """
            {"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"}
            """);
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
                    if (request.RequestUri == new Uri(
                        "https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=1"))
                    {
                        const string LatestRelease =
                            """
                            {"tag_name":"v1.3.0","html_url":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","draft":false,"prerelease":false,"assets":[{"name":"lucia-appliance-manifest.json","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-manifest.json"},{"name":"lucia-appliance-attestations.jsonl","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl"}]}
                            """;
                        var releases = $"[{LatestRelease},{string.Join(
                            ',',
                            Enumerable.Range(0, 99).Select(
                                index => $$"""{"tag_name":"invalid-{{index}}"}"""))}]";
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                releases,
                                Encoding.UTF8,
                                "application/json"),
                        };
                    }
                    if (request.RequestUri == new Uri(
                        "https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=2"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                """
                                [{"tag_name":"v1.2.6","html_url":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.2.6","draft":false,"prerelease":false,"assets":[{"name":"lucia-appliance-manifest.json","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/lucia-appliance-manifest.json"},{"name":"lucia-appliance-attestations.jsonl","browser_download_url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/lucia-appliance-attestations.jsonl"}]}]
                                """,
                                Encoding.UTF8,
                                "application/json"),
                        };
                    }

                    var manifest = request.RequestUri!.AbsolutePath.Contains("v1.3.0")
                        ? """
                          {"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"v1.3.0","attestationBundleUrl":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-attestations.jsonl","version":"1.3.0","releaseApi":"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.3.0","releaseNotesUrl":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.3.0","compatibility":{"architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1},"channels":{"lucia":{"version":"1.3.0","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","requires":{"layoutVersion":1,"dataSchemaVersion":1,"source":{"jetsonLinux":"36.5.2","redis":"9.0.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"target":{"jetsonLinux":"36.6.0","redis":"9.0.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":false},"parts":[{"name":"lucia.tar.zst","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia.tar.zst"}]},"os":{"version":"1.4.0","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","requires":{"minimumLuciaVersion":"1.2.3","layoutVersion":1,"source":{"jetsonLinux":"36.5.2","redis":"9.0.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"target":{"jetsonLinux":"36.6.0","redis":"9.0.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":true},"parts":[{"name":"os.tar.zst","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/os.tar.zst"}]}}}
                          """
                        : """
                          {"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"v1.2.6","attestationBundleUrl":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/lucia-appliance-attestations.jsonl","version":"1.2.6","releaseApi":"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/v1.2.6","releaseNotesUrl":"https://github.com/seiggy/lucia-dotnet/releases/tag/v1.2.6","compatibility":{"architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1},"channels":{"lucia":{"version":"1.2.6","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","requires":{"layoutVersion":1,"dataSchemaVersion":1,"source":{"jetsonLinux":"36.5.2","redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"target":{"jetsonLinux":"36.6.0","redis":"8.3.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":false},"parts":[{"name":"lucia.tar.zst","bytes":5,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/lucia.tar.zst"}]},"os":{"version":"1.3.0","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","requires":{"minimumLuciaVersion":"1.2.3","layoutVersion":1,"source":{"jetsonLinux":"36.5.2","redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"target":{"jetsonLinux":"36.6.0","redis":"8.3.0","cuda":"13.0","cudnn":"10.0","onnxRuntime":"2.0.0","sherpaOnnx":"2.0.0"},"reboot":true},"parts":[{"name":"os.tar.zst","bytes":5,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/os.tar.zst"}]}}}
                          """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            manifest,
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
                NullLogger<ApplianceUpdateService>.Instance,
                runtimeInfoPath);

            var result = await service.CheckAsync(CancellationToken.None);

            Assert.True(result.Compatible);
            Assert.True(result.LuciaCompatible);
            Assert.True(result.LuciaNewerDiscovered);
            Assert.True(result.LuciaUpdateAvailable);
            Assert.Equal("1.2.6", result.LatestLuciaVersion);
            Assert.Equal("v1.2.6", result.ReleaseTag);
            Assert.Equal(
                [
                    new Uri("https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=1"),
                    new Uri("https://github.com/seiggy/lucia-dotnet/releases/download/v1.3.0/lucia-appliance-manifest.json"),
                    new Uri("https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page=100&page=2"),
                    new Uri("https://github.com/seiggy/lucia-dotnet/releases/download/v1.2.6/lucia-appliance-manifest.json"),
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
