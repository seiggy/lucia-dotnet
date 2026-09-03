using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace lucia.ApplianceManager;

public sealed partial class ApplianceUpdateCoordinator
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
    private bool _isUpdaterRunning;
    private bool _ignorePersistedStatus;
    private UpdateOperationStatus _status =
        new("none", "none", "idle", null, null);

    public ApplianceUpdateCoordinator()
    {
        _operationPath = Path.Combine(_statePath, "operation.json");
        Directory.CreateDirectory(_statePath);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                _statePath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute);
            foreach (var fileName in new[]
                     {
                         "operation.json",
                         "lucia.env",
                         "lucia.previous.env",
                         "os.env",
                     })
            {
                var path = Path.Combine(_statePath, fileName);
                var file = new FileInfo(path);
                if (file.Exists || file.LinkTarget is not null)
                {
                    if (file.LinkTarget is not null)
                    {
                        throw new InvalidDataException(
                            "Trusted appliance update state cannot be a symbolic link.");
                    }
                    File.SetUnixFileMode(
                        path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }
        RecoverInterruptedLuciaUpdate();
        RecoverInterruptedOsWrite();
        if (!File.Exists(_operationPath))
        {
            return;
        }
        try
        {
            var durableStatus = JsonSerializer.Deserialize<UpdateOperationStatus>(
                    File.ReadAllText(_operationPath))
                ?? _status;
            var status = durableStatus;
            if ((status.Status is "queued" or "running")
                && !IsOsAwaitingValidation(status))
            {
                status = status with
                {
                    Status = "failed",
                    Message =
                        "The manager restarted during the update; startup recovery restored a safe state.",
                };
                PersistStatusUnsafe(status, durableStatus);
            }
            _status = status;
        }
        catch (JsonException)
        {
            var status = new UpdateOperationStatus(
                "none",
                "none",
                "failed",
                null,
                "The persisted update operation is invalid.");
            PersistStatusUnsafe(status, _status);
            _status = status;
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

    public UpdateOperationStatus? GetStatus(string operationId)
    {
        var status = GetStatus();
        return status.OperationId == operationId ? status : null;
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                RefreshStatusUnsafe();
                return _status.Status is "queued" or "running"
                    || IsLuciaTransitionInProgress()
                    || IsOsTransitionInProgress()
                    || _isUpdaterRunning;
            }
        }
    }

    public bool CanStartOsRollback
    {
        get
        {
            lock (_gate)
            {
                RefreshStatusUnsafe();
                return !_isUpdaterRunning && IsOsRollbackAvailable();
            }
        }
    }

    public UpdateStartResult TryStart(
        string action,
        string channel,
        string? tag,
        string? operationId = null)
    {
        if (action is not ("apply" or "rollback")
            || channel is not ("lucia" or "os")
            || action == "apply"
                && (tag is null
                    || !System.Text.RegularExpressions.Regex.IsMatch(
                        tag,
                        @"^v[0-9]+\.[0-9]+\.[0-9]+$"))
            || operationId is not null
                && !Guid.TryParseExact(operationId, "D", out _))
        {
            throw new ArgumentException("Invalid update operation.");
        }

        lock (_gate)
        {
            RefreshStatusUnsafe();
            if (action == "rollback"
                && channel == "os"
                && !IsOsRollbackAvailable())
            {
                return UpdateStartResult.RollbackUnavailable;
            }
            var isAllowedOsRollback = !_isUpdaterRunning
                && action == "rollback"
                && channel == "os"
                && IsOsRollbackAvailable();
            if (((_status.Status is "queued" or "running")
                    || IsLuciaTransitionInProgress()
                    || IsOsTransitionInProgress())
                && !isAllowedOsRollback)
            {
                return UpdateStartResult.Busy;
            }

            operationId ??= Guid.NewGuid().ToString("D");
            var status = new UpdateOperationStatus(
                action,
                channel,
                "queued",
                tag,
                null,
                OperationId: operationId);
            PersistStatusUnsafe(status, _status);
            _status = status;
            _ignorePersistedStatus = false;
            _isUpdaterRunning = true;
            _ = Task.Run(
                () => RunAsync(action, channel, tag, operationId));
            return UpdateStartResult.Accepted;
        }
    }

    private async Task RunAsync(
        string action,
        string channel,
        string? tag,
        string operationId)
    {
        try
        {
            await RunProcessAsync(action, channel, tag, operationId)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _isUpdaterRunning = false;
            }
        }
    }

    private async Task RunProcessAsync(
        string action,
        string channel,
        string? tag,
        string operationId)
    {
        try
        {
            SetStatus(new(
                action,
                channel,
                "running",
                tag,
                null,
                OperationId: operationId));
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
            startInfo.Environment["LUCIA_UPDATE_OPERATION_ID"] = operationId;
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
                        action == "rollback"
                            ? "OS rollback is awaiting boot validation."
                            : "OS update is awaiting boot validation.",
                        OperationId: operationId)
                    : new(
                        action,
                        channel,
                        "succeeded",
                        tag,
                        NullIfEmpty(output),
                        OperationId: operationId)
                : new(
                    action,
                    channel,
                    "failed",
                    tag,
                    NullIfEmpty(error),
                    OperationId: operationId);
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
                or IOException
                or UnauthorizedAccessException
                or AggregateException)
        {
            var failed = new UpdateOperationStatus(
                action,
                channel,
                "failed",
                tag,
                exception.Message,
                OperationId: operationId);
            try
            {
                SetStatus(failed);
            }
            catch (Exception persistenceException) when (
                persistenceException is IOException
                    or UnauthorizedAccessException
                    or Win32Exception
                    or AggregateException)
            {
                lock (_gate)
                {
                    _status = failed;
                    _ignorePersistedStatus = true;
                }
            }
        }
    }

    private void SetStatus(UpdateOperationStatus status)
    {
        lock (_gate)
        {
            PersistStatusUnsafe(status, _status);
            _status = status;
            _ignorePersistedStatus = false;
        }
    }

    private void PersistStatusUnsafe(
        UpdateOperationStatus status,
        UpdateOperationStatus previous)
    {
        var replaced = false;
        try
        {
            WriteStatusUnsafe(status, ref replaced);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or Win32Exception)
        {
            if (replaced)
            {
                try
                {
                    var restored = false;
                    WriteStatusUnsafe(previous, ref restored);
                }
                catch (Exception restoreException) when (
                    restoreException is IOException
                        or UnauthorizedAccessException
                        or Win32Exception)
                {
                    throw new AggregateException(
                        "The manager transition and its durable rollback both failed.",
                        exception,
                        restoreException);
                }
            }
            throw;
        }
    }

    private void WriteStatusUnsafe(
        UpdateOperationStatus status,
        ref bool replaced)
    {
        var temporary = _operationPath + ".tmp";
        File.Delete(temporary);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using (var stream = new FileStream(temporary, options))
        {
            JsonSerializer.Serialize(stream, status);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _operationPath, overwrite: true);
        replaced = true;
        if (OperatingSystem.IsLinux())
        {
            SyncDirectory(_statePath);
        }
    }

    private void RefreshStatusUnsafe()
    {
        if (_ignorePersistedStatus || !File.Exists(_operationPath))
        {
            return;
        }
        _status = JsonSerializer.Deserialize<UpdateOperationStatus>(
                File.ReadAllText(_operationPath))
            ?? _status;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void SyncDirectory(string path)
    {
        const int OpenDirectory = 0x10000;
        var descriptor = Open(path, OpenDirectory);
        if (descriptor < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        try
        {
            if (Fsync(descriptor) != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            _ = Close(descriptor);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int descriptor);

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
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "systemd rejected the Lucia service restart.");
        }
    }

    private void RecoverInterruptedLuciaUpdate()
    {
        var state = Path.Combine(_statePath, "lucia.env");
        if (!File.Exists(state)
            || File.ReadLines(state).Any(
                line => line is "phase=committed" or "phase=manager-pending"))
        {
            return;
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = _updaterPath,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("recover");
        startInfo.ArgumentList.Add("lucia");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start Lucia update recovery.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Lucia update recovery failed: {process.StandardError.ReadToEnd().Trim()}");
        }
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

    private bool IsOsAwaitingValidation(UpdateOperationStatus status) =>
        status is
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

    private bool IsLuciaTransitionInProgress()
    {
        var path = Path.Combine(_statePath, "lucia.env");
        if (!File.Exists(path))
        {
            return false;
        }
        return !File.ReadLines(path).Any(line => line == "phase=committed");
    }
}
