using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateDownloadResumeTests : IDisposable
{
    private static readonly Uri PartUri =
        new("https://github.com/seiggy/lucia-dotnet/releases/download/v1.5.0/part1");
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly string Digest = Convert.ToHexStringLower(SHA256.HashData(Payload));

    private readonly string _root = Directory.CreateTempSubdirectory("lucia-resume-").FullName;
    private readonly List<RangeHeaderValue?> _ranges = [];

    private string Destination => Path.Combine(_root, "part1");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task DownloadAsync_ResumesWithRangeAfterConnectionDrops()
    {
        using var service = CreateService(request => _ranges.Count == 1
            ? Full(new DroppingStream(Payload, 6))
            : Partial(request, Payload));

        await DownloadAsync(service);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Null(_ranges[0]);
        Assert.Equal(6, Assert.Single(_ranges[1]!.Ranges).From);
    }

    [Fact]
    public async Task DownloadAsync_ResumesPartialLeftByAnEarlierAttempt()
    {
        await File.WriteAllBytesAsync(Destination + ".tmp", Payload[..6]);
        using var service = CreateService(request => Partial(request, Payload));

        await DownloadAsync(service);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Equal(6, Assert.Single(Assert.Single(_ranges)!.Ranges).From);
    }

    [Fact]
    public async Task DownloadAsync_RestartsWhenServerIgnoresRange()
    {
        await File.WriteAllBytesAsync(Destination + ".tmp", Payload[..6]);
        using var service = CreateService(_ => Full(new MemoryStream(Payload)));

        await DownloadAsync(service);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task DownloadAsync_DiscardsCorruptPartialAndStartsOver()
    {
        await File.WriteAllBytesAsync(Destination + ".tmp", [9, 9, 9, 9, 9, 9]);
        using var service = CreateService(request => request.Headers.Range is null
            ? Full(new MemoryStream(Payload))
            : Partial(request, Payload));

        await DownloadAsync(service);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.NotNull(_ranges[0]);
        Assert.Null(_ranges[1]);
    }

    [Fact]
    public async Task DownloadAsync_ReverifiesAPartFinishedByAnEarlierAttempt()
    {
        await File.WriteAllBytesAsync(Destination, Payload);
        using var service = CreateService(_ => throw new InvalidOperationException("No request expected."));

        await DownloadAsync(service);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Empty(_ranges);
    }

    private Task DownloadAsync(ApplianceUpdateService service) =>
        service.DownloadAsync(
            PartUri,
            Destination,
            Payload.Length,
            Digest,
            maximumBytes: 1024,
            CancellationToken.None);

    private ApplianceUpdateService CreateService(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(
            new HttpClient(new StaticHttpMessageHandler(request =>
            {
                _ranges.Add(request.Headers.Range);
                return respond(request);
            })),
            new ApplianceManagerClient(Path.Combine(_root, "manager.sock")),
            new ApplianceUpdateStagingStore(Path.Combine(_root, "staging"), NullLogger<ApplianceUpdateStagingStore>.Instance),
            NullLogger<ApplianceUpdateService>.Instance)
        {
            DownloadRetryDelay = TimeSpan.Zero,
        };

    private static HttpResponseMessage Full(Stream body) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(body) };

    private static HttpResponseMessage Partial(HttpRequestMessage request, byte[] payload)
    {
        var from = (int)request.Headers.Range!.Ranges.Single().From!.Value;
        var content = new ByteArrayContent(payload[from..]);
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, payload.Length - 1, payload.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }
}
