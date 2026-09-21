using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateProgressTests
{
    [Fact]
    public async Task MultipartDownload_AggregatesBytesAndKeepsIdentityAtHandoff()
    {
        var root = Directory.CreateTempSubdirectory("lucia-progress-").FullName;
        var socket = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socket));
        listener.Listen();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            const string Tag = "v1.5.0";
            const string BaseUrl = "https://github.com/seiggy/lucia-dotnet/releases/download/v1.5.0/";
            var runtime = new { jetsonLinux = "36.5.2", redis = "8", cuda = "12", cudnn = "9", onnxRuntime = "1", sherpaOnnx = "1" };
            var runtimePath = Path.Combine(root, "runtime.json");
            await File.WriteAllTextAsync(runtimePath, JsonSerializer.Serialize(runtime), timeout.Token);
            var store = new ApplianceUpdateStagingStore(root, NullLogger<ApplianceUpdateStagingStore>.Instance);
            byte[] payload = [1, 2, 3, 4];
            var digest = Convert.ToHexStringLower(SHA256.HashData(payload));
            var manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, repository = "seiggy/lucia-dotnet", tag = Tag,
                attestationBundleUrl = BaseUrl + "lucia-appliance-attestations.jsonl",
                releaseApi = "https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/" + Tag,
                releaseNotesUrl = "https://github.com/seiggy/lucia-dotnet/releases/tag/" + Tag,
                compatibility = new { architecture = "arm64", board = "test", minimumDiskBytes = 1, layoutVersion = 1, dataSchemaVersion = 1 },
                channels = new { lucia = new
                {
                    version = "1.5.0", bytes = 8,
                    requires = new { layoutVersion = 1, dataSchemaVersion = 1, source = runtime, target = runtime, reboot = false },
                    parts = new[] { "part1", "part2" }.Select(name => new { name, bytes = 4, sha256 = digest, url = BaseUrl + name }),
                } },
            });
            ApplianceUpdateOperationStatus? halfway = null;
            var server = Task.Run(async () =>
            {
                for (var index = 0; index < 2; index++)
                {
                    using var accepted = await listener.AcceptAsync(timeout.Token);
                    await using var stream = new NetworkStream(accepted);
                    var buffer = new byte[8192];
                    _ = await stream.ReadAsync(buffer, timeout.Token);
                    var response = index == 0
                        ? """{"hostname":"test","architecture":"arm64","board":"test","luciaVersion":"1.0.0","storageBytes":100000000000,"rebootRequired":false,"network":{"ssid":"","signal":null},"os":{"name":"Ubuntu","versionId":"22.04","imageVersion":"1.0.0","jetsonLinuxVersion":"36.5.2","rootfsAbEnabled":true},"services":[]}"""
                        : JsonSerializer.Serialize(store.GetStatus() with { Action = "apply", Phase = "verifying" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(response)}\r\nConnection: close\r\n\r\n{response}"), timeout.Token);
                }
            }, timeout.Token);
            using var manager = new ApplianceManagerClient(socket);
            using var service = new ApplianceUpdateService(
                new HttpClient(new StaticHttpMessageHandler(request =>
                {
                    var name = request.RequestUri!.Segments[^1];
                    HttpContent content;
                    if (name == Tag)
                    {
                        content = new StringContent(JsonSerializer.Serialize(new
                        {
                            tag_name = Tag,
                            html_url = "https://github.com/seiggy/lucia-dotnet/releases/tag/" + Tag,
                            assets = new[] { "lucia-appliance-manifest.json", "lucia-appliance-attestations.jsonl" }
                                .Select(asset => new { name = asset, browser_download_url = BaseUrl + asset }),
                        }));
                    }
                    else if (name == "lucia-appliance-manifest.json")
                    {
                        content = new StringContent(manifest);
                    }
                    else
                    {
                        if (name == "part2")
                        {
                            halfway = store.GetStatus();
                        }
                        content = new ByteArrayContent(payload);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                })), manager, store, NullLogger<ApplianceUpdateService>.Instance, runtimePath);

            var acceptedOperation = await service.InstallAsync("lucia", Tag, timeout.Token);
            await service.StagingTask!.WaitAsync(timeout.Token);
            Assert.NotNull(halfway);
            Assert.Equal("downloading", halfway.Phase);
            Assert.Equal(4, halfway.CompletedBytes);
            Assert.Equal(8, halfway.TotalBytes);
            Assert.Equal(acceptedOperation.OperationId, halfway.OperationId);
            Assert.Equal(acceptedOperation.OperationId, store.GetStatus().OperationId);
            Assert.Equal("apply", store.GetStatus().Action);
            Assert.Equal("running", store.GetStatus().Status);
            await server.WaitAsync(timeout.Token);
        }
        finally
        {
            listener.Dispose();
            File.Delete(socket);
            Directory.Delete(root, recursive: true);
        }
    }
}
