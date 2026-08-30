using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using lucia.ApplianceManager;

var builder = WebApplication.CreateSlimBuilder(args);

var socketPath = Environment.GetEnvironmentVariable("LUCIA_APPLIANCE_SOCKET")
    ?? "/run/lucia/appliance-manager.sock";

builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socketPath));

var app = builder.Build();
var operationLock = new SemaphoreSlim(1, 1);

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        || !context.Request.Path.StartsWithSegments("/v1"))
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    if (!await operationLock.WaitAsync(
            TimeSpan.Zero,
            context.RequestAborted)
        .ConfigureAwait(false))
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(
                new { Error = "Another appliance operation is in progress." },
                context.RequestAborted)
            .ConfigureAwait(false);
        return;
    }

    try
    {
        await next(context).ConfigureAwait(false);
    }
    finally
    {
        operationLock.Release();
    }
});

app.MapGet("/v1/status", GetStatusAsync);
app.MapPost("/v1/services/{service}/restart", RestartServiceAsync);
app.MapPost("/v1/host/reboot", RebootHostAsync);
app.MapGet("/v1/telemetry", GetTelemetryConfiguration);
app.MapPut("/v1/telemetry", UpdateTelemetryConfigurationAsync);

await app.RunAsync().ConfigureAwait(false);

static async Task<IResult> GetStatusAsync(CancellationToken cancellationToken)
{
    var services = new[]
    {
        (Id: "agenthost", Unit: "lucia-agenthost.service"),
        (Id: "redis", Unit: "lucia-redis.service"),
        (Id: "collector", Unit: "lucia-otelcol.service"),
        (Id: "redis-exporter", Unit: "lucia-redis-exporter.service"),
    };
    var arguments = new List<string>
    {
        "show",
        "--property=Id,ActiveState,UnitFileState",
    };
    arguments.AddRange(services.Select(service => service.Unit));

    var systemctlResult = await RunSystemctlAsync(arguments, cancellationToken)
        .ConfigureAwait(false);
    if (systemctlResult.ExitCode != 0)
    {
        return TypedResults.Problem(
            detail: systemctlResult.StandardError.Trim(),
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "systemd status is unavailable");
    }

    var unitStates = ParseUnitStates(systemctlResult.StandardOutput);
    var network = await ReadWifiStatusAsync(cancellationToken)
        .ConfigureAwait(false);
    var storageBytes = await ReadStorageBytesAsync(cancellationToken)
        .ConfigureAwait(false);
    var osRelease = ReadKeyValueFile(
        Environment.GetEnvironmentVariable("LUCIA_OS_RELEASE_PATH")
            ?? "/etc/os-release");
    var hostnamePath = Environment.GetEnvironmentVariable("LUCIA_HOSTNAME_PATH")
        ?? "/etc/hostname";
    var currentReleasePath =
        Environment.GetEnvironmentVariable("LUCIA_CURRENT_RELEASE_PATH")
            ?? "/opt/lucia/current";
    var rebootRequiredPath =
        Environment.GetEnvironmentVariable("LUCIA_REBOOT_REQUIRED_PATH")
            ?? "/var/run/reboot-required";
    var osVersionPath =
        Environment.GetEnvironmentVariable("LUCIA_OS_VERSION_PATH")
            ?? "/etc/lucia/os-version";
    var jetsonReleasePath =
        Environment.GetEnvironmentVariable("LUCIA_JETSON_RELEASE_PATH")
            ?? "/etc/nv_tegra_release";
    var currentRelease = new DirectoryInfo(currentReleasePath);
    var releaseTarget = currentRelease.LinkTarget ?? currentRelease.FullName;
    var luciaVersion = Path.GetFileName(
        releaseTarget.TrimEnd(Path.DirectorySeparatorChar));

    return Results.Ok(new
    {
        Hostname = File.ReadAllText(hostnamePath).Trim(),
        Architecture = RuntimeInformation.ProcessArchitecture
            .ToString()
            .ToLowerInvariant(),
        Board = Environment.GetEnvironmentVariable("LUCIA_APPLIANCE_BOARD")
            ?? "jetson-orin-nano-super-p3767-0005",
        LuciaVersion = luciaVersion,
        StorageBytes = storageBytes,
        RebootRequired = File.Exists(rebootRequiredPath),
        Network = new
        {
            network.Ssid,
            network.Signal,
        },
        Os = new
        {
            Name = osRelease.GetValueOrDefault("NAME", "Linux"),
            VersionId = osRelease.GetValueOrDefault("VERSION_ID", "unknown"),
            ImageVersion = File.Exists(osVersionPath)
                ? File.ReadAllText(osVersionPath).Trim()
                : "unknown",
            JetsonLinuxVersion = ReadJetsonLinuxVersion(jetsonReleasePath),
        },
        Services = services.Select(service =>
        {
            var state = unitStates.GetValueOrDefault(service.Unit);
            return new
            {
                service.Id,
                ActiveState = state.ActiveState ?? "unknown",
                UnitFileState = state.UnitFileState ?? "unknown",
            };
        }),
    });
}

static async Task<IResult> RestartServiceAsync(
    string service,
    CancellationToken cancellationToken)
{
    var unit = service switch
    {
        "collector" => "lucia-otelcol.service",
        "redis" => "lucia-redis.service",
        "redis-exporter" => "lucia-redis-exporter.service",
        _ => null,
    };

    if (unit is null)
    {
        return TypedResults.NotFound();
    }

    var result = await RunSystemctlAsync(
            ["restart", unit],
            cancellationToken)
        .ConfigureAwait(false);
    if (result.ExitCode != 0)
    {
        return TypedResults.Problem(
            detail: result.StandardError.Trim(),
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "systemd rejected the restart request");
    }

    return Results.Accepted(
        value: new
        {
            Service = service,
            Status = "restart-requested",
        });
}

static async Task<IResult> RebootHostAsync(CancellationToken cancellationToken)
{
    var result = await RunSystemctlAsync(
            ["--no-block", "reboot"],
            cancellationToken)
        .ConfigureAwait(false);
    if (result.ExitCode != 0)
    {
        return TypedResults.Problem(
            detail: result.StandardError.Trim(),
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "systemd rejected the reboot request");
    }

    return Results.Accepted(value: new { Status = "reboot-requested" });
}

static IResult GetTelemetryConfiguration()
{
    var path = Environment.GetEnvironmentVariable("LUCIA_TELEMETRY_ENV_PATH")
        ?? "/var/lib/lucia/config/telemetry.env";
    return Results.Ok(ReadTelemetryConfiguration(path));
}

static async Task<IResult> UpdateTelemetryConfigurationAsync(
    TelemetryConfigurationRequest request,
    CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint)
        || endpoint.Scheme is not ("http" or "https")
        || string.IsNullOrWhiteSpace(endpoint.Host))
    {
        return Results.BadRequest(new
        {
            Error = "Endpoint must be an absolute HTTP or HTTPS URL.",
        });
    }

    var hasUsername = !string.IsNullOrWhiteSpace(request.Username);
    var hasPassword = !string.IsNullOrWhiteSpace(request.Password);
    if (hasUsername != hasPassword)
    {
        return Results.BadRequest(new
        {
            Error = "Basic authentication requires both username and password.",
        });
    }

    var path = Environment.GetEnvironmentVariable("LUCIA_TELEMETRY_ENV_PATH")
        ?? "/var/lib/lucia/config/telemetry.env";
    var normalizedEndpoint = request.Endpoint.TrimEnd('/');
    var existing = File.Exists(path)
        ? ReadKeyValueFile(path)
        : new Dictionary<string, string>(StringComparer.Ordinal);
    var previousLines = File.Exists(path)
        ? File.ReadAllLines(path)
        : null;
    var previousEnabled = bool.TryParse(
        existing.GetValueOrDefault("LUCIA_TELEMETRY_ENABLED"),
        out var wasEnabled)
        && wasEnabled;
    var authorization = request.ClearAuthorization
        ? string.Empty
        : hasUsername
            ? "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{request.Username}:{request.Password}"))
            : existing.GetValueOrDefault(
                "OTEL_EXPORTER_OTLP_AUTHORIZATION",
                string.Empty);
    var values = new[]
    {
        $"LUCIA_TELEMETRY_ENABLED={request.Enabled.ToString().ToLowerInvariant()}",
        $"OTEL_EXPORTER_OTLP_ENDPOINT={normalizedEndpoint}",
        $"OTEL_EXPORTER_OTLP_INSECURE={(endpoint.Scheme == "http").ToString().ToLowerInvariant()}",
        $"OTEL_EXPORTER_OTLP_INSECURE_SKIP_VERIFY={request.InsecureSkipVerify.ToString().ToLowerInvariant()}",
        $"OTEL_EXPORTER_OTLP_AUTHORIZATION={authorization}",
    };
    WriteEnvironmentFile(path, values);

    var arguments = request.Enabled
        ? new[]
        {
            "enable",
            "--now",
            "lucia-redis-exporter.service",
            "lucia-otelcol.service",
        }
        : new[]
        {
            "disable",
            "--now",
            "lucia-otelcol.service",
            "lucia-redis-exporter.service",
        };
    (int ExitCode, string StandardOutput, string StandardError) result;
    try
    {
        result = await RunSystemctlAsync(arguments, cancellationToken)
            .ConfigureAwait(false);
    }
    catch (Exception exception) when (
        exception is OperationCanceledException
            or Win32Exception
            or InvalidOperationException)
    {
        RestoreTelemetryFile(path, previousLines);
        _ = await RunSystemctlAsync(
                GetTelemetrySystemdArguments(previousEnabled),
                CancellationToken.None)
            .ConfigureAwait(false);
        throw;
    }
    if (result.ExitCode != 0)
    {
        RestoreTelemetryFile(path, previousLines);
        var rollbackResult = await RunSystemctlAsync(
                GetTelemetrySystemdArguments(previousEnabled),
                CancellationToken.None)
            .ConfigureAwait(false);
        var detail = rollbackResult.ExitCode == 0
            ? result.StandardError.Trim()
            : $"{result.StandardError.Trim()} Rollback failed: {rollbackResult.StandardError.Trim()}";
        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "systemd could not apply telemetry state");
    }

    static string[] GetTelemetrySystemdArguments(bool enabled) =>
        enabled
            ?
            [
                "enable",
                "--now",
                "lucia-redis-exporter.service",
                "lucia-otelcol.service",
            ]
            :
            [
                "disable",
                "--now",
                "lucia-otelcol.service",
                "lucia-redis-exporter.service",
            ];

    static void RestoreTelemetryFile(string path, string[]? previousLines)
    {
        if (previousLines is null)
        {
            File.Delete(path);
        }
        else
        {
            WriteEnvironmentFile(path, previousLines);
        }
    }

    return Results.Ok(ReadTelemetryConfiguration(path));
}

static async Task<(int ExitCode, string StandardOutput, string StandardError)>
    RunSystemctlAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
{
    var systemctlPath = Environment.GetEnvironmentVariable("LUCIA_SYSTEMCTL_PATH")
        ?? "/usr/bin/systemctl";
    var startInfo = new ProcessStartInfo
    {
        FileName = systemctlPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start systemctl.");
    var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    var standardOutput = await standardOutputTask.ConfigureAwait(false);
    var standardError = await standardErrorTask.ConfigureAwait(false);
    return (process.ExitCode, standardOutput, standardError);
}

static Dictionary<string, (string? ActiveState, string? UnitFileState)>
    ParseUnitStates(string output)
{
    var result =
        new Dictionary<string, (string? ActiveState, string? UnitFileState)>(
            StringComparer.Ordinal);
    foreach (var block in output.Split(
                 "\n\n",
                 StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
    {
        var values = block.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);
        if (values.TryGetValue("Id", out var id))
        {
            result[id] = (
                values.GetValueOrDefault("ActiveState"),
                values.GetValueOrDefault("UnitFileState"));
        }
    }

    return result;
}

static Dictionary<string, string> ReadKeyValueFile(string path)
{
    return File.ReadLines(path)
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
        .Select(line => line.Split('=', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(
            parts => parts[0],
            parts => parts[1].Trim().Trim('"'),
            StringComparer.Ordinal);
}

static object ReadTelemetryConfiguration(string path)
{
    var values = File.Exists(path)
        ? ReadKeyValueFile(path)
        : new Dictionary<string, string>(StringComparer.Ordinal);
    var endpoint = values.GetValueOrDefault(
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        string.Empty);
    return new
    {
        Configured = !string.IsNullOrWhiteSpace(endpoint),
        Enabled = bool.TryParse(
            values.GetValueOrDefault("LUCIA_TELEMETRY_ENABLED"),
            out var enabled)
            && enabled,
        Endpoint = endpoint,
        InsecureSkipVerify = bool.TryParse(
            values.GetValueOrDefault(
                "OTEL_EXPORTER_OTLP_INSECURE_SKIP_VERIFY"),
            out var insecureSkipVerify)
            && insecureSkipVerify,
        HasAuthorization = !string.IsNullOrWhiteSpace(
            values.GetValueOrDefault("OTEL_EXPORTER_OTLP_AUTHORIZATION")),
    };
}

static void WriteEnvironmentFile(string path, IEnumerable<string> values)
{
    var directory = Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException(
            "Telemetry environment path has no directory.");
    Directory.CreateDirectory(directory);
    var temporaryPath = Path.Combine(
        directory,
        $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    try
    {
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            foreach (var value in values)
            {
                writer.WriteLine(value);
            }
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temporaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }
    finally
    {
        File.Delete(temporaryPath);
    }
}

static string ReadJetsonLinuxVersion(string path)
{
    if (!File.Exists(path))
    {
        return "unknown";
    }

    var value = File.ReadAllText(path).Trim();
    var match = Regex.Match(
        value,
        @"# R(?<major>\d+).+REVISION:\s*(?<revision>\d+(?:\.\d+)?)");
    return match.Success
        ? $"{match.Groups["major"].Value}.{match.Groups["revision"].Value}"
        : value;
}

static async Task<(string Ssid, int? Signal)> ReadWifiStatusAsync(
    CancellationToken cancellationToken)
{
    var nmcliPath = Environment.GetEnvironmentVariable("LUCIA_NMCLI_PATH")
        ?? "/usr/bin/nmcli";
    var startInfo = new ProcessStartInfo
    {
        FileName = nmcliPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (var argument in new[]
    {
        "--terse",
        "--escape",
        "yes",
        "--fields",
        "IN-USE,SSID,SIGNAL",
        "device",
        "wifi",
        "list",
        "--rescan",
        "no",
    })
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start nmcli.");
    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    var output = await outputTask.ConfigureAwait(false);
    _ = await errorTask.ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
        return ("Ethernet", null);
    }

    var match = Regex.Match(
        output,
        @"^(?:yes|\*):(?<ssid>.*):(?<signal>\d+)$",
        RegexOptions.Multiline);
    return match.Success
        ? (
            match.Groups["ssid"].Value
                .Replace(@"\:", ":", StringComparison.Ordinal)
                .Replace(@"\\", @"\", StringComparison.Ordinal),
            int.Parse(
                match.Groups["signal"].Value,
                System.Globalization.CultureInfo.InvariantCulture))
        : ("Ethernet", null);
}

static async Task<long> ReadStorageBytesAsync(
    CancellationToken cancellationToken)
{
    var blockdevPath = Environment.GetEnvironmentVariable("LUCIA_BLOCKDEV_PATH")
        ?? "/usr/sbin/blockdev";
    var device = Environment.GetEnvironmentVariable("LUCIA_APPLIANCE_DEVICE")
        ?? "/dev/nvme0n1";
    var startInfo = new ProcessStartInfo
    {
        FileName = blockdevPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("--getsize64");
    startInfo.ArgumentList.Add(device);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start blockdev.");
    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    var output = await outputTask.ConfigureAwait(false);
    _ = await errorTask.ConfigureAwait(false);
    return process.ExitCode == 0
        && long.TryParse(
            output.Trim(),
            System.Globalization.CultureInfo.InvariantCulture,
            out var bytes)
        ? bytes
        : 0;
}
