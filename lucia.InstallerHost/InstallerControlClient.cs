using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace lucia.InstallerHost;

internal sealed partial class InstallerControlClient(
    string controlPath,
    string? controlCommand,
    ILogger<InstallerControlClient> logger)
{
    public Task<JsonNode> GetDisksAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync("disks", null, cancellationToken);

    public Task<JsonNode> GetNetworksAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync("networks", null, cancellationToken);

    public Task<JsonNode> GetStatusAsync(CancellationToken cancellationToken) =>
        ExecuteJsonAsync("status", null, cancellationToken);

    public Task<JsonNode> StartInstallationAsync(
        InstallerConfigurationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteJsonAsync(
            "configure",
            JsonSerializer.Serialize(
                request,
                InstallerJsonContext.Default.InstallerConfigurationRequest),
            cancellationToken);

    public Task<JsonNode> RetryNetworkAsync(
        WifiConfigurationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteJsonAsync(
            "retry-network",
            JsonSerializer.Serialize(
                request,
                InstallerJsonContext.Default.WifiConfigurationRequest),
            cancellationToken);

    private async Task<JsonNode> ExecuteJsonAsync(
        string command,
        string? request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = controlPath,
            RedirectStandardError = true,
            RedirectStandardInput = request is not null,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (controlCommand is not null)
        {
            startInfo.ArgumentList.Add("--non-interactive");
            startInfo.ArgumentList.Add(controlCommand);
        }
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the installer control command.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            if (request is not null)
            {
                await process.StandardInput
                    .WriteAsync(request.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
            var standardOutput = await standardOutputTask
                .ConfigureAwait(false);
            var standardError = await standardErrorTask
                .ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                LogControlFailure(
                    command,
                    process.ExitCode,
                    standardError.Trim());
                throw new InstallerControlException(
                    ParseControlError(standardError)
                    ?? $"Installer control command '{command}' failed.");
            }

            return JsonNode.Parse(standardOutput)
                ?? throw new InvalidOperationException(
                    $"Installer control command '{command}' returned no JSON.");
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static string? ParseControlError(string standardError)
    {
        try
        {
            return JsonNode.Parse(standardError)?["error"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Installer control command {Command} exited with code {ExitCode}: {StandardError}")]
    private partial void LogControlFailure(
        string command,
        int exitCode,
        string standardError);
}
