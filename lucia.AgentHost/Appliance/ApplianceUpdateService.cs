using System.Security.Cryptography;
using System.Text.Json;

namespace lucia.AgentHost.Appliance;

public sealed partial class ApplianceUpdateService(
    HttpClient httpClient,
    ApplianceManagerClient manager,
    ApplianceUpdateStagingStore staging,
    ILogger<ApplianceUpdateService> logger,
    string? runtimeInfoPath = null) : IDisposable
{
    private const int ReleaseListLimit = 10;
    private static readonly Uri s_releaseApi = new(
        $"https://api.github.com/repos/seiggy/lucia-dotnet/releases?per_page={ReleaseListLimit}");
    private readonly string _runtimeInfoPath = runtimeInfoPath
        ?? Environment.GetEnvironmentVariable("LUCIA_RUNTIME_INFO_PATH")
        ?? "/etc/lucia/appliance-runtime.json";
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    internal Task? StagingTask { get; private set; }

    public async Task<ApplianceUpdateStatus> CheckAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var checkToken = timeout.Token;
        var current = await manager.GetStatusAsync(checkToken)
            .ConfigureAwait(false);
        using var releaseResponse = await httpClient
            .GetAsync(s_releaseApi, checkToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        await using var releaseStream = await releaseResponse.Content
            .ReadAsStreamAsync(checkToken)
            .ConfigureAwait(false);
        using var releaseDocument = await JsonDocument
            .ParseAsync(releaseStream, cancellationToken: checkToken)
            .ConfigureAwait(false);

        ApplianceUpdateStatus? latestStableStatus = null;
        foreach (var releaseRoot in releaseDocument.RootElement.EnumerateArray())
        {
            if (releaseRoot.TryGetProperty("draft", out var draft)
                && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }
            if (releaseRoot.TryGetProperty("prerelease", out var prerelease)
                && prerelease.ValueKind == JsonValueKind.True)
            {
                continue;
            }
            var releaseTag = releaseRoot.TryGetProperty(
                "tag_name",
                out var tagElement)
                ? tagElement.GetString()
                : null;
            if (!IsStableReleaseTag(releaseTag))
            {
                continue;
            }

            ApplianceUpdateStatus? status;
            try
            {
                status = await TryEvaluateReleaseAsync(
                    current,
                    releaseRoot,
                    releaseTag,
                    checkToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or InvalidOperationException
                    or KeyNotFoundException
                    or JsonException
                    or FormatException
                    or OverflowException)
            {
                continue;
            }
            if (status is null)
            {
                continue;
            }
            latestStableStatus ??= status;
            if (status.LuciaUpdateAvailable || status.OsUpdateAvailable)
            {
                return status;
            }
            if (status.Compatible
                && !status.LuciaNewerDiscovered
                && !status.OsNewerDiscovered)
            {
                return status;
            }
        }

        if (latestStableStatus is not null)
        {
            return latestStableStatus with
            {
                LuciaUpdateAvailable = false,
                OsUpdateAvailable = false,
                Message = "No compatible newer appliance release was found for this device.",
            };
        }

        return new ApplianceUpdateStatus(
            current.LuciaVersion,
            current.Os.ImageVersion,
            LatestLuciaVersion: null,
            LatestOsVersion: null,
            ManifestAvailable: false,
            Compatible: false,
            LuciaCompatible: false,
            OsCompatible: false,
            LuciaNewerDiscovered: false,
            OsNewerDiscovered: false,
            LuciaUpdateAvailable: false,
            OsUpdateAvailable: false,
            ReleaseTag: null,
            ReleaseUrl: null,
            Message: "No stable appliance releases were discovered for compatibility checks.");
    }

    private async Task<ApplianceUpdateStatus?> TryEvaluateReleaseAsync(
        ApplianceStatusResponse current,
        JsonElement releaseRoot,
        string? releaseTag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseTag))
        {
            return null;
        }

        var releaseUrl = releaseRoot.TryGetProperty(
            "html_url",
            out var releaseUrlElement)
            ? releaseUrlElement.GetString()
            : null;
        var manifestUrl = releaseRoot.GetProperty("assets")
            .EnumerateArray()
            .Where(asset =>
                asset.TryGetProperty("name", out var nameElement)
                && "lucia-appliance-manifest.json" == nameElement.GetString())
            .Select(asset =>
                asset.TryGetProperty(
                    "browser_download_url",
                    out var urlElement)
                    ? urlElement.GetString()
                    : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var attestationBundleUrl = releaseRoot.GetProperty("assets")
            .EnumerateArray()
            .Where(asset =>
                asset.TryGetProperty("name", out var nameElement)
                && "lucia-appliance-attestations.jsonl" == nameElement.GetString())
            .Select(asset =>
                asset.TryGetProperty(
                    "browser_download_url",
                    out var urlElement)
                    ? urlElement.GetString()
                    : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(manifestUrl)
            || string.IsNullOrWhiteSpace(attestationBundleUrl))
        {
            return null;
        }

        Uri manifestUri;
        try
        {
            manifestUri = ParseManifestUri(manifestUrl);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        using var manifestResponse = await httpClient
            .GetAsync(manifestUri, cancellationToken)
            .ConfigureAwait(false);
        if (!manifestResponse.IsSuccessStatusCode)
        {
            return null;
        }

        await using var manifestStream = await manifestResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var manifestDocument = await JsonDocument
            .ParseAsync(manifestStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var manifest = manifestDocument.RootElement;
        var declaredBundleUrl = manifest
            .TryGetProperty("attestationBundleUrl", out var bundleElement)
            ? bundleElement.GetString()
            : null;
        if (manifest.TryGetProperty("repository", out var repositoryElement)
            && repositoryElement.GetString() == "seiggy/lucia-dotnet"
            && manifest.TryGetProperty("tag", out var tagElement)
            && tagElement.GetString() == releaseTag
            && !string.IsNullOrWhiteSpace(attestationBundleUrl)
            && string.Equals(
                declaredBundleUrl,
                attestationBundleUrl,
                StringComparison.Ordinal)
            && IsTrustedReleaseUri(attestationBundleUrl)
            && manifest.TryGetProperty("releaseNotesUrl", out var releaseNotesElement)
            && manifest.TryGetProperty("releaseApi", out var releaseApiElement)
            && string.Equals(
                releaseNotesElement.GetString(),
                releaseUrl,
                StringComparison.Ordinal)
            && string.Equals(
                releaseApiElement.GetString(),
                $"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/{releaseTag}",
                StringComparison.Ordinal))
        {
            var compatibility = manifest.GetProperty("compatibility");
            var architecture = compatibility.GetProperty("architecture").GetString();
            var board = compatibility.GetProperty("board").GetString();
            var minimumDiskBytes = compatibility
                .GetProperty("minimumDiskBytes")
                .GetInt64();
            var hardwareCompatible =
                string.Equals(
                    architecture,
                    current.Architecture,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    board,
                    current.Board,
                    StringComparison.Ordinal)
                && current.StorageBytes >= minimumDiskBytes
                && compatibility.GetProperty("layoutVersion").GetInt32() == 1
                && compatibility.GetProperty("dataSchemaVersion").GetInt32() == 1;
            var channels = manifest.GetProperty("channels");
            var luciaRequirements = channels
                .GetProperty("lucia")
                .GetProperty("requires");
            var luciaSource = luciaRequirements.GetProperty("source");
            var luciaTarget = luciaRequirements.GetProperty("target");
            var osRequirements = channels
                .GetProperty("os")
                .GetProperty("requires");
            var osSource = osRequirements.GetProperty("source");
            var osTarget = osRequirements.GetProperty("target");
            var luciaCompatible = hardwareCompatible
                && string.Equals(
                    luciaSource.GetProperty("jetsonLinux").GetString(),
                    current.Os.JetsonLinuxVersion,
                    StringComparison.Ordinal)
                && luciaRequirements.GetProperty("layoutVersion").GetInt32() == 1
                && luciaRequirements.GetProperty("dataSchemaVersion").GetInt32() == 1
                && !luciaRequirements.GetProperty("reboot").GetBoolean()
                && HasExpectedRuntime(luciaSource)
                && HasRuntimeMetadata(luciaTarget);
            var osCompatible = hardwareCompatible
                && osRequirements.GetProperty("layoutVersion").GetInt32() == 1
                && osSource.GetProperty("jetsonLinux").GetString()
                    == current.Os.JetsonLinuxVersion
                && HasExpectedRuntime(osSource)
                && HasRuntimeMetadata(osTarget)
                && osRequirements.GetProperty("reboot").GetBoolean()
                && MeetsMinimumVersion(
                    current.LuciaVersion,
                    osRequirements.GetProperty("minimumLuciaVersion").GetString());
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
                Compatible: luciaCompatible || osCompatible,
                LuciaCompatible: luciaCompatible,
                OsCompatible: osCompatible,
                LuciaNewerDiscovered: luciaNewerDiscovered,
                OsNewerDiscovered: osNewerDiscovered,
                LuciaUpdateAvailable: luciaCompatible && luciaNewerDiscovered,
                OsUpdateAvailable: osCompatible && osNewerDiscovered,
                releaseTag,
                releaseUrl,
                !luciaCompatible && !osCompatible
                    ? "The latest appliance release is not compatible with this device."
                    : hasNewerRelease
                        ? "A signed update is ready to verify and install."
                        : null);
        }

        return null;
    }

    private static bool IsStableReleaseTag(string? value) =>
        value is not null
        && System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^v[0-9]+\.[0-9]+\.[0-9]+$");

    public async Task<ApplianceUpdateOperationStatus> InstallAsync(
        string channel,
        string tag,
        CancellationToken cancellationToken)
    {
        if (channel is not ("lucia" or "os"))
        {
            throw new ArgumentException("Unknown appliance update channel.", nameof(channel));
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                tag,
                @"^v[0-9]+\.[0-9]+\.[0-9]+$"))
        {
            throw new ArgumentException("Invalid appliance release tag.", nameof(tag));
        }
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconcileHandedOffOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            var accepted = staging.TryStart(channel, tag)
                ?? throw new InvalidOperationException(
                    "Another appliance update is already being staged.");
            StagingTask = Task.Run(() => RunStagingAsync(channel, tag));
            return accepted;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task<ApplianceUpdateOperationStatus> StageAsync(
        string channel,
        string tag,
        CancellationToken cancellationToken)
    {
        var releaseApi = new Uri(
            $"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/{tag}");
        using var releaseResponse = await httpClient
            .GetAsync(releaseApi, cancellationToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        using var releaseDocument = JsonDocument.Parse(
            await releaseResponse.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false));
        var releaseRoot = releaseDocument.RootElement;
        if (releaseRoot.GetProperty("tag_name").GetString() != tag)
        {
            throw new InvalidDataException(
                "GitHub returned a different appliance release tag.");
        }

        var manifestUrl = FindAssetUrl(
            releaseRoot,
            "lucia-appliance-manifest.json");
        var bundleUrl = FindAssetUrl(
            releaseRoot,
            "lucia-appliance-attestations.jsonl");
        var manifestUri = ParseManifestUri(manifestUrl);
        var bundleUri = ParseReleaseAssetUri(
            bundleUrl,
            tag,
            "lucia-appliance-attestations.jsonl");
        foreach (var partial in Directory.EnumerateDirectories(
                     staging.Root,
                     $".{tag}.*.partial"))
        {
            Directory.Delete(partial, recursive: true);
        }
        var stage = Path.Combine(
            staging.Root,
            $".{tag}.{Guid.NewGuid():N}.partial");
        Directory.CreateDirectory(stage);
        try
        {
            var manifestPath = Path.Combine(
                stage,
                "lucia-appliance-manifest.json");
            await DownloadAsync(
                    manifestUri,
                    manifestPath,
                    expectedBytes: null,
                    expectedSha256: null,
                    maximumBytes: 10 * 1024 * 1024,
                    cancellationToken)
                .ConfigureAwait(false);
            var bundlePath = Path.Combine(
                stage,
                "lucia-appliance-attestations.jsonl");
            await DownloadAsync(
                    bundleUri,
                    bundlePath,
                    expectedBytes: null,
                    expectedSha256: null,
                    maximumBytes: 10 * 1024 * 1024,
                    cancellationToken)
                .ConfigureAwait(false);

            using var manifestDocument = JsonDocument.Parse(
                await File.ReadAllBytesAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false));
            var manifest = manifestDocument.RootElement;
            if (manifest.GetProperty("repository").GetString()
                    != "seiggy/lucia-dotnet"
                || manifest.GetProperty("tag").GetString() != tag
                || manifest.GetProperty("attestationBundleUrl").GetString()
                    != bundleUrl)
            {
                throw new InvalidDataException(
                    "The appliance manifest does not match the GitHub release.");
            }
            var current = await manager.GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            var compatibility = manifest.GetProperty("compatibility");
            if (!string.Equals(
                    compatibility.GetProperty("architecture").GetString(),
                    current.Architecture,
                    StringComparison.OrdinalIgnoreCase)
                || compatibility.GetProperty("board").GetString() != current.Board
                || current.StorageBytes
                    < compatibility.GetProperty("minimumDiskBytes").GetInt64())
            {
                throw new InvalidDataException(
                    "The appliance update is not compatible with this device.");
            }
            if (compatibility.GetProperty("layoutVersion").GetInt32() != 1
                || compatibility.GetProperty("dataSchemaVersion").GetInt32() != 1
                || manifest.GetProperty("releaseNotesUrl").GetString()
                    != releaseRoot.GetProperty("html_url").GetString()
                || manifest.GetProperty("releaseApi").GetString()
                    != releaseApi.AbsoluteUri)
            {
                throw new InvalidDataException(
                    "The appliance release metadata is incomplete or unsupported.");
            }
            var selectedChannel = manifest
                .GetProperty("channels")
                .GetProperty(channel);
            var requirements = selectedChannel.GetProperty("requires");
            var luciaSource = channel == "lucia"
                ? requirements.GetProperty("source")
                : default;
            var luciaTarget = channel == "lucia"
                ? requirements.GetProperty("target")
                : default;
            var osSource = channel == "os"
                ? requirements.GetProperty("source")
                : default;
            var osTarget = channel == "os"
                ? requirements.GetProperty("target")
                : default;
            if (requirements.GetProperty("layoutVersion").GetInt32() != 1
                || channel == "lucia"
                    && (requirements.GetProperty("dataSchemaVersion").GetInt32() != 1
                        || luciaSource.GetProperty("jetsonLinux").GetString()
                            != current.Os.JetsonLinuxVersion
                        || requirements.GetProperty("reboot").GetBoolean()
                        || !HasExpectedRuntime(luciaSource)
                        || !HasRuntimeMetadata(luciaTarget))
                || channel == "os"
                    && (osSource.GetProperty("jetsonLinux").GetString()
                            != current.Os.JetsonLinuxVersion
                        || !HasExpectedRuntime(osSource)
                        || !HasRuntimeMetadata(osTarget)
                        || !requirements.GetProperty("reboot").GetBoolean()
                        || !MeetsMinimumVersion(
                            current.LuciaVersion,
                            requirements.GetProperty("minimumLuciaVersion").GetString())))
            {
                throw new InvalidDataException(
                    "The appliance update channel requirements are not satisfied.");
            }
            var candidateVersion = selectedChannel.GetProperty("version").GetString();
            var currentVersion = channel == "lucia"
                ? current.LuciaVersion
                : current.Os.ImageVersion;
            if (!IsNewer(candidateVersion, currentVersion))
            {
                throw new InvalidDataException(
                    "The selected appliance channel has no newer release.");
            }
            var (parts, channelBytes) = ValidateParts(selectedChannel, tag);
            EnsureStagingCapacity(staging.Root, channelBytes);
            foreach (var part in parts)
            {
                var name = part.GetProperty("name").GetString()!;
                var bytes = part.GetProperty("bytes").GetInt64();
                var sha256 = part.GetProperty("sha256").GetString()!;
                var uri = ParseReleaseAssetUri(
                    part.GetProperty("url").GetString()!,
                    tag,
                    name);
                await DownloadAsync(
                        uri,
                        Path.Combine(stage, name),
                        bytes,
                        sha256,
                        maximumBytes: 1_900_000_000,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var finalStage = Path.Combine(staging.Root, tag);
            if (Directory.Exists(finalStage))
            {
                Directory.Delete(finalStage, recursive: true);
            }
            Directory.Move(stage, finalStage);
            staging.SetHandingOff(channel, tag);
            try
            {
                var operation = await manager
                    .StartUpdateAsync(
                        channel,
                        tag,
                        staging.GetStatus().OperationId!,
                        cancellationToken)
                    .ConfigureAwait(false);
                staging.SetHandedOff(channel, tag);
                return operation;
            }
            catch
            {
                staging.CompleteHandoffAttempt();
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    internal static (JsonElement[] Parts, long ChannelBytes) ValidateParts(
        JsonElement selectedChannel,
        string tag)
    {
        var channelBytes = selectedChannel.GetProperty("bytes").GetInt64();
        var parts = selectedChannel.GetProperty("parts")
            .EnumerateArray()
            .ToArray();
        if (parts is { Length: < 1 or > 64 })
        {
            throw new InvalidDataException(
                "The appliance update has an invalid number of parts.");
        }
        long partBytesTotal = 0;
        foreach (var part in parts)
        {
            var name = part.GetProperty("name").GetString()
                ?? throw new InvalidDataException("An update part has no name.");
            if (Path.GetFileName(name) != name
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    name,
                    @"^[A-Za-z0-9][A-Za-z0-9._-]*$"))
            {
                throw new InvalidDataException("An update part name is invalid.");
            }
            var bytes = part.GetProperty("bytes").GetInt64();
            if (bytes is < 1 or > 1_900_000_000)
            {
                throw new InvalidDataException(
                    "An update part has an invalid size.");
            }
            partBytesTotal = checked(partBytesTotal + bytes);
            var sha256 = part.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("An update part has no digest.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    sha256,
                    @"^[0-9a-f]{64}$"))
            {
                throw new InvalidDataException(
                    "An update part has an invalid digest.");
            }
            var url = part.GetProperty("url").GetString()
                ?? throw new InvalidDataException("An update part has no URL.");
            _ = ParseReleaseAssetUri(url, tag, name);
        }
        if (partBytesTotal != channelBytes)
        {
            throw new InvalidDataException(
                "The appliance update part sizes do not match the channel size.");
        }
        return (parts, channelBytes);
    }

    public async Task<ApplianceUpdateOperationStatus> GetOperationAsync(
        CancellationToken cancellationToken,
        string? operationId = null)
    {
        var stagingStatus = staging.GetStatus();
        if (operationId is not null
            && stagingStatus.OperationId != operationId)
        {
            return await manager
                .GetUpdateOperationAsync(operationId, cancellationToken)
                .ConfigureAwait(false);
        }
        if (stagingStatus.Status is "queued"
            || stagingStatus is { Action: "stage", Status: "running" })
        {
            return stagingStatus;
        }
        var managerStatus = await manager
            .GetUpdateOperationAsync(
                stagingStatus.Status == "failed" ? null : operationId,
                cancellationToken)
            .ConfigureAwait(false);
        var currentStagingStatus = staging.GetStatus();
        if (currentStagingStatus.Action != stagingStatus.Action
            || currentStagingStatus.Status != stagingStatus.Status
            || currentStagingStatus.OperationId != stagingStatus.OperationId)
        {
            return await GetOperationAsync(cancellationToken, operationId)
                .ConfigureAwait(false);
        }
        if (stagingStatus is { Action: "handoff", Status: "running" })
        {
            if (staging.IsHandoffRequestActive)
            {
                return stagingStatus;
            }
            if (managerStatus.Channel == stagingStatus.Channel
                && managerStatus.Tag == stagingStatus.Tag
                && managerStatus.OperationId == stagingStatus.OperationId)
            {
                staging.SetHandedOff(
                    stagingStatus.Channel,
                    stagingStatus.Tag!);
                return managerStatus;
            }
            staging.SetFailed(
                stagingStatus.Channel,
                stagingStatus.Tag!,
                "The appliance manager did not accept the staged update.");
            return staging.GetStatus();
        }
        if (stagingStatus is { Action: "apply", Status: "running" }
            && managerStatus.Status is not ("queued" or "running"))
        {
            staging.Clear();
        }
        return stagingStatus.Status == "failed"
            ? stagingStatus with
            {
                LuciaRollbackAvailable = managerStatus.LuciaRollbackAvailable,
                OsRollbackAvailable = managerStatus.OsRollbackAvailable,
            }
            : managerStatus;
    }

    public async Task<ApplianceUpdateOperationStatus> RollbackAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stagingStatus = staging.GetStatus();
            var isPendingOsValidation =
                stagingStatus is
                {
                    Action: "apply",
                    Channel: "os",
                    Status: "running",
                }
                && channel == "os";
            if (stagingStatus.Status is "queued" or "running"
                && !isPendingOsValidation)
            {
                throw new InvalidOperationException(
                    "An appliance update is still being staged.");
            }
            if (stagingStatus.Status != "running")
            {
                staging.Clear();
            }
            return await manager
                .StartRollbackAsync(channel, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task RunStagingAsync(string channel, string tag)
    {
        try
        {
            staging.SetRunning(channel, tag);
            await StageAsync(channel, tag, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogStagingFailure(exception, channel, tag);
            if (staging.GetStatus().Action == "stage")
            {
                try
                {
                    staging.SetFailed(channel, tag, exception.Message);
                }
                catch (Exception persistenceException) when (
                    persistenceException is IOException
                        or UnauthorizedAccessException
                        or System.ComponentModel.Win32Exception
                        or AggregateException)
                {
                    staging.SetFailedInMemory(
                        channel,
                        tag,
                        exception.Message);
                    LogStagingPersistenceFailure(
                        persistenceException,
                        channel,
                        tag);
                }
            }
        }
    }

    private async Task ReconcileHandedOffOperationAsync(
        CancellationToken cancellationToken)
    {
        var stagingStatus = staging.GetStatus();
        if (stagingStatus is not
            { Action: "apply" or "handoff", Status: "running" })
        {
            return;
        }
        if (stagingStatus.Action == "handoff"
            && staging.IsHandoffRequestActive)
        {
            return;
        }
        var managerStatus = await manager
            .GetUpdateOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (managerStatus.Channel != stagingStatus.Channel
            || managerStatus.Tag != stagingStatus.Tag
            || managerStatus.OperationId != stagingStatus.OperationId)
        {
            staging.SetFailed(
                stagingStatus.Channel,
                stagingStatus.Tag!,
                "The appliance manager did not accept the staged update.");
        }
        else if (managerStatus.Status is not ("queued" or "running"))
        {
            staging.Clear();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to stage {Channel} update from {Tag}.")]
    private partial void LogStagingFailure(
        Exception exception,
        string channel,
        string tag);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to persist terminal staging state for {Channel} update {Tag}.")]
    private partial void LogStagingPersistenceFailure(
        Exception exception,
        string channel,
        string tag);


    private static bool IsNewer(string? candidate, string current)
    {
        return Version.TryParse(candidate, out var candidateVersion)
            && Version.TryParse(
                current.Split('-', 2)[0],
                out var currentVersion)
            && candidateVersion > currentVersion;
    }

    internal static bool MeetsMinimumVersion(
        string current,
        string? minimum)
    {
        return Version.TryParse(
                current.Split('-', 2)[0],
                out var currentVersion)
            && Version.TryParse(minimum, out var minimumVersion)
            && currentVersion >= minimumVersion;
    }

    private bool HasExpectedRuntime(JsonElement compatibility)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(_runtimeInfoPath));
        var installed = document.RootElement;
        return compatibility.GetProperty("redis").GetString()
                == installed.GetProperty("redis").GetString()
            && compatibility.GetProperty("cuda").GetString()
                == installed.GetProperty("cuda").GetString()
            && compatibility.GetProperty("cudnn").GetString()
                == installed.GetProperty("cudnn").GetString()
            && compatibility.GetProperty("onnxRuntime").GetString()
                == installed.GetProperty("onnxRuntime").GetString()
            && compatibility.GetProperty("sherpaOnnx").GetString()
                == installed.GetProperty("sherpaOnnx").GetString();
    }

    private static bool HasRuntimeMetadata(JsonElement runtime) =>
        new[]
        {
            "jetsonLinux",
            "redis",
            "cuda",
            "cudnn",
            "onnxRuntime",
            "sherpaOnnx",
        }.All(
            name => runtime.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()));

    internal static void EnsureStagingCapacity(
        string stagingRoot,
        long payloadBytes)
    {
        if (payloadBytes < 1)
        {
            throw new InvalidDataException(
                "The appliance update payload size is invalid.");
        }
        var fullPath = Path.GetFullPath(stagingRoot);
        var drive = DriveInfo.GetDrives()
            .Where(candidate => candidate.IsReady)
            .OrderByDescending(
                candidate => candidate.RootDirectory.FullName.Length)
            .FirstOrDefault(
                candidate => fullPath.StartsWith(
                    candidate.RootDirectory.FullName,
                    StringComparison.Ordinal));
        if (drive is null)
        {
            throw new IOException(
                "The appliance staging filesystem is unavailable.");
        }
        var requiredBytes = checked(
            payloadBytes * 2 + (1L << 30));
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"The appliance update needs {requiredBytes} free bytes; "
                + $"{drive.AvailableFreeSpace} are available.");
        }
    }

    public void Dispose()
    {
        _transitionGate.Dispose();
        httpClient.Dispose();
    }

    internal static Uri ParseManifestUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.Ordinal)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith(
                "/seiggy/lucia-dotnet/releases/download/",
                StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith(
                "/lucia-appliance-manifest.json",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The latest GitHub release returned an untrusted appliance manifest URL.");
        }

        return uri;
    }

    private static string FindAssetUrl(JsonElement releaseRoot, string name) =>
        releaseRoot.GetProperty("assets")
            .EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == name)
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault()
        ?? throw new InvalidDataException($"GitHub release asset is missing: {name}");

    private static Uri ParseReleaseAssetUri(
        string value,
        string tag,
        string name)
    {
        if (!IsTrustedReleaseUri(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.AbsolutePath
                != $"/seiggy/lucia-dotnet/releases/download/{tag}/{name}")
        {
            throw new InvalidDataException(
                "The appliance release returned an untrusted asset URL.");
        }
        return uri;
    }

    private static bool IsTrustedReleaseUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.Ordinal)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath.StartsWith(
            "/seiggy/lucia-dotnet/releases/download/",
            StringComparison.Ordinal);

    private async Task DownloadAsync(
        Uri uri,
        string destination,
        long? expectedBytes,
        string? expectedSha256,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        var downloadToken = timeout.Token;
        using var response = await httpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, downloadToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Update asset exceeds its size limit.");
        }

        var temporary = destination + ".tmp";
        try
        {
            await using var source = await response.Content
                .ReadAsStreamAsync(downloadToken)
                .ConfigureAwait(false);
            await using var target = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            int read;
            while ((read = await source
                       .ReadAsync(buffer, downloadToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new InvalidDataException(
                        "Update asset exceeds its size limit.");
                }
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(
                        buffer.AsMemory(0, read),
                        downloadToken)
                    .ConfigureAwait(false);
            }
            await target.FlushAsync(downloadToken).ConfigureAwait(false);
            if (expectedBytes is not null && total != expectedBytes)
            {
                throw new InvalidDataException("Update asset size mismatch.");
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant();
            if (expectedSha256 is not null
                && !string.Equals(
                    actualHash,
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Update asset digest mismatch.");
            }
            await target.DisposeAsync().ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
