using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace lucia.InstallerHost;

internal sealed partial class InstallerControlClient(
    string controlPath,
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
        ExecuteJsonAsync("configure", request, cancellationToken);

    private async Task<JsonNode> ExecuteJsonAsync(
        string command,
        InstallerConfigurationRequest? request,
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
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the installer control command.");
        var standardOutputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);
        if (request is not null)
        {
            await JsonSerializer.SerializeAsync(
                    process.StandardInput.BaseStream,
                    request,
                    InstallerJsonContext.Default.InstallerConfigurationRequest,
                    cancellationToken)
                .ConfigureAwait(false);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            LogControlFailure(command, process.ExitCode, standardError.Trim());
            throw new InvalidOperationException(
                $"Installer control command '{command}' failed.");
        }

        return JsonNode.Parse(standardOutput)
            ?? throw new InvalidOperationException(
                $"Installer control command '{command}' returned no JSON.");
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
