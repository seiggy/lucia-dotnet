using System.Text.Json;

namespace lucia.AgentHost.Appliance;

public sealed class ApplianceUpdateService(
    HttpClient httpClient,
    ApplianceManagerClient manager,
    IConfiguration configuration)
{
    public async Task<ApplianceUpdateStatus> CheckAsync(
        CancellationToken cancellationToken)
    {
        var current = await manager.GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        var releaseApi = configuration["Appliance:ReleaseApi"]
            ?? "https://api.github.com/repos/seiggy/lucia-dotnet/releases/latest";
        using var releaseResponse = await httpClient
            .GetAsync(releaseApi, cancellationToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        await using var releaseStream = await releaseResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var releaseDocument = await JsonDocument
            .ParseAsync(releaseStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var releaseRoot = releaseDocument.RootElement;
        var releaseUrl = releaseRoot.TryGetProperty(
            "html_url",
            out var releaseUrlElement)
            ? releaseUrlElement.GetString()
            : null;
        var manifestUrl = releaseRoot.GetProperty("assets")
            .EnumerateArray()
            .Where(asset =>
                asset.GetProperty("name").GetString()
                == "lucia-appliance-manifest.json")
            .Select(asset =>
                asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return new ApplianceUpdateStatus(
                current.LuciaVersion,
                current.Os.ImageVersion,
                LatestLuciaVersion: null,
                LatestOsVersion: null,
                ManifestAvailable: false,
                Compatible: false,
                LuciaNewerDiscovered: false,
                OsNewerDiscovered: false,
                LuciaUpdateAvailable: false,
                OsUpdateAvailable: false,
                releaseUrl,
                "The latest GitHub release has no appliance manifest.");
        }

        using var manifestResponse = await httpClient
            .GetAsync(manifestUrl, cancellationToken)
            .ConfigureAwait(false);
        manifestResponse.EnsureSuccessStatusCode();
        await using var manifestStream = await manifestResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var manifestDocument = await JsonDocument
            .ParseAsync(manifestStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var manifest = manifestDocument.RootElement;
        var compatibility = manifest.GetProperty("compatibility");
        var architecture = compatibility.GetProperty("architecture").GetString();
        var board = compatibility.GetProperty("board").GetString();
        var jetsonLinux = compatibility.GetProperty("jetsonLinux").GetString();
        var minimumDiskBytes = compatibility
            .GetProperty("minimumDiskBytes")
            .GetInt64();
        var compatible =
            string.Equals(
                architecture,
                current.Architecture,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                board,
                current.Board,
                StringComparison.Ordinal)
            && string.Equals(
                jetsonLinux,
                current.Os.JetsonLinuxVersion,
                StringComparison.Ordinal)
            && current.StorageBytes >= minimumDiskBytes;
        var channels = manifest.GetProperty("channels");
        var latestLuciaVersion = channels
            .GetProperty("lucia")
            .GetProperty("version")
            .GetString();
        var latestOsVersion = channels
            .GetProperty("os")
            .GetProperty("version")
            .GetString();
        var luciaNewerDiscovered =
            IsNewer(latestLuciaVersion, current.LuciaVersion);
        var osNewerDiscovered =
            IsNewer(latestOsVersion, current.Os.ImageVersion);
        var hasNewerRelease = luciaNewerDiscovered || osNewerDiscovered;

        return new ApplianceUpdateStatus(
            current.LuciaVersion,
            current.Os.ImageVersion,
            latestLuciaVersion,
            latestOsVersion,
            ManifestAvailable: true,
            Compatible: compatible,
            LuciaNewerDiscovered: luciaNewerDiscovered,
            OsNewerDiscovered: osNewerDiscovered,
            LuciaUpdateAvailable: false,
            OsUpdateAvailable: false,
            releaseUrl,
            !compatible
                ? "The latest appliance release is not compatible with this device."
                : hasNewerRelease
                    ? "A compatible release was found, but installation remains locked until GitHub attestation verification is implemented."
                    : null);
    }

    private static bool IsNewer(string? candidate, string current)
    {
        return Version.TryParse(candidate, out var candidateVersion)
            && Version.TryParse(
                current.Split('-', 2)[0],
                out var currentVersion)
            && candidateVersion > currentVersion;
    }
}
