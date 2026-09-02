using System.Diagnostics;
using System.Text.Json;

namespace lucia.ApplianceManager;

public sealed class ApplianceUpdateCoordinator
{
    private readonly object _gate = new();
    private readonly string _updaterPath =
        Environment.GetEnvironmentVariable("LUCIA_UPDATE_PATH")
        ?? "/usr/libexec/lucia/lucia-update";
    private readonly string _systemctlPath =
        Environment.GetEnvironmentVariable("LUCIA_SYSTEMCTL_PATH")
        ?? "/usr/bin/systemctl";
    private readonly string _statePath =
        Path.Combine(
            Environment.GetEnvironmentVariable("LUCIA_UPDATE_ROOT")
                ?? "/var/lib/lucia/updates",
            "state");
    private readonly string _operationPath;
    private UpdateOperationStatus _status =
        new("none", "none", "idle", null, null);

    public ApplianceUpdateCoordinator()
    {
        _operationPath = Path.Combine(_statePath, "operation.json");
        Directory.CreateDirectory(_statePath);
        RecoverInterruptedOsWrite();
        if (!File.Exists(_operationPath))
        {
            return;
        }
        try
        {
            _status = JsonSerializer.Deserialize<UpdateOperationStatus>(
                    File.ReadAllText(_operationPath))
                ?? _status;
            if ((_status.Status is "queued" or "running")
                && !IsOsAwaitingValidation())
            {
                _status = _status with
                {
                    Status = "failed",
                    Message =
                        "The manager restarted during the update; startup recovery restored a safe state.",
                };
                PersistStatusUnsafe();
            }
        }
        catch (JsonException)
        {
            _status = new(
                "none",
                "none",
                "failed",
                null,
                "The persisted update operation is invalid.");
            PersistStatusUnsafe();
        }
    }

    public UpdateOperationStatus GetStatus()
    {
        lock (_gate)
        {
            RefreshStatusUnsafe();
            return _status with
            {
                LuciaRollbackAvailable = IsLuciaRollbackAvailable(),
                OsRollbackAvailable = IsOsRollbackAvailable(),
            };
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                RefreshStatusUnsafe();
                return (_status.Status is "queued" or "running")
                    && !IsOsAwaitingValidation();
            }
        }
    }

    public bool TryStart(string action, string channel, string? tag)
    {
        if (action is not ("apply" or "rollback")
            || channel is not ("lucia" or "os")
            || action == "apply"
                && (tag is null
                    || !System.Text.RegularExpressions.Regex.IsMatch(
                        tag,
                        @"^v[0-9]+\.[0-9]+\.[0-9]+$")))
        {
            throw new ArgumentException("Invalid update operation.");
        }

        lock (_gate)
        {
            RefreshStatusUnsafe();
            var awaitingOsValidation = IsOsAwaitingValidation();
            if (action == "rollback"
                && channel == "os"
                && !IsOsRollbackAvailable())
            {
                return false;
            }
            var isAllowedOsRollback = awaitingOsValidation
                && action == "rollback"
                && channel == "os"
                && IsOsRollbackAvailable();
            if (((_status.Status is "queued" or "running")
                    || IsOsTransitionInProgress())
                && !isAllowedOsRollback)
            {
                return false;
            }

            _status = new(action, channel, "queued", tag, null);
            PersistStatusUnsafe();
            _ = Task.Run(() => RunAsync(action, channel, tag));
            return true;
        }
    }

    private async Task RunAsync(string action, string channel, string? tag)
    {
        SetStatus(new(action, channel, "running", tag, null));
        var startInfo = new ProcessStartInfo
        {
            FileName = _updaterPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(action);
        startInfo.ArgumentList.Add(channel);
        if (tag is not null)
        {
            startInfo.ArgumentList.Add(tag);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Failed to start the appliance updater.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();
            UpdateOperationStatus result = process.ExitCode == 0
                ? channel == "os"
                    ? new(
                        action,
                        channel,
                        "running",
                        tag,
                        "OS update is awaiting boot validation.")
                    : new(action, channel, "succeeded", tag, NullIfEmpty(output))
                : new(action, channel, "failed", tag, NullIfEmpty(error));
            SetStatus(result);
            if (result.Status == "succeeded"
                && channel == "lucia")
            {
                ScheduleLuciaServicesRestart();
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException)
        {
            SetStatus(new(action, channel, "failed", tag, exception.Message));
        }
    }

    private void SetStatus(UpdateOperationStatus status)
    {
        lock (_gate)
        {
            _status = status;
            PersistStatusUnsafe();
        }
    }

    private void PersistStatusUnsafe()
    {
        var temporary = _operationPath + ".tmp";
        using (var stream = new FileStream(
                   temporary,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        {
            JsonSerializer.Serialize(stream, _status);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _operationPath, overwrite: true);
    }

    private void RefreshStatusUnsafe()
    {
        if (!File.Exists(_operationPath))
        {
            return;
        }
        _status = JsonSerializer.Deserialize<UpdateOperationStatus>(
                File.ReadAllText(_operationPath))
            ?? _status;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private void ScheduleLuciaServicesRestart()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _systemctlPath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--no-block");
        startInfo.ArgumentList.Add("restart");
        startInfo.ArgumentList.Add("lucia-appliance-manager.service");
        startInfo.ArgumentList.Add("lucia-agenthost.service");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to schedule the Lucia service restart.");
    }

    private bool IsOsRollbackAvailable()
    {
        return ReadOsStatus() is "pending" or "validated";
    }

    private bool IsOsTransitionInProgress()
    {
        var status = ReadOsStatus();
        return status is
            "writing" or
            "pending" or
            "rollback-pending";
    }

    private bool IsOsAwaitingValidation() =>
        _status is
            { Action: "apply" or "rollback", Channel: "os", Status: "running" }
        && ReadOsStatus() is "pending" or "rollback-pending";

    private void RecoverInterruptedOsWrite()
    {
        var path = Path.Combine(_statePath, "os.env");
        if (!File.Exists(path)
            || ReadOsStatus() != "writing")
        {
            return;
        }
        var temporary = path + ".tmp";
        using (var stream = new FileStream(
                   temporary,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            foreach (var line in File.ReadLines(path))
            {
                writer.WriteLine(line == "status=writing"
                    ? "status=failed"
                    : line);
            }
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private string? ReadOsStatus()
    {
        var path = Path.Combine(_statePath, "os.env");
        if (!File.Exists(path))
        {
            return null;
        }
        var status = File.ReadLines(path)
            .FirstOrDefault(line => line.StartsWith("status=", StringComparison.Ordinal));
        return status is null ? null : status["status=".Length..];
    }

    private bool IsLuciaRollbackAvailable()
    {
        var path = Path.Combine(_statePath, "lucia.env");
        if (!File.Exists(path))
        {
            return false;
        }
        var values = File.ReadLines(path)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);
        return values.GetValueOrDefault("phase") == "committed"
            && values.TryGetValue("backup", out var backup)
            && File.Exists(backup);
    }
}
