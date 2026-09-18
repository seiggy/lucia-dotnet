using System.Text.Json;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace lucia.AgentHost.Appliance;

public sealed partial class ApplianceUpdateStagingStore
{
    private readonly object _gate = new();
    private readonly string _operationPath;
    private readonly ILogger<ApplianceUpdateStagingStore> _logger;
    private bool _isHandoffRequestActive;
    private ApplianceUpdateOperationStatus _status =
        new("none", "none", "idle", null, null);

    public ApplianceUpdateStagingStore(
        ILogger<ApplianceUpdateStagingStore> logger)
        : this(
            Environment.GetEnvironmentVariable("LUCIA_UPDATE_STAGING_PATH")
                ?? "/var/lib/lucia/updates/staging",
            logger)
    {
    }

    private void DeleteUnreferencedFinalizedStages()
    {
        var referencedTag = _status.Status is "queued" or "running"
            ? _status.Tag
            : null;
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(directory);
            if (name != referencedTag
                && System.Text.RegularExpressions.Regex.IsMatch(
                    name,
                    @"^v[0-9]+\.[0-9]+\.[0-9]+$"))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private void DeleteFinalizedStage(string? tag)
    {
        if (tag is null)
        {
            return;
        }
        var directory = Path.Combine(Root, tag);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal ApplianceUpdateStagingStore(
        string root,
        ILogger<ApplianceUpdateStagingStore> logger)
    {
        _logger = logger;
        Root = root;
        _operationPath = Path.Combine(Root, "operation.json");
        Directory.CreateDirectory(Root);
        Load();
        DeleteOrphanedAttempts();
        DeleteUnreferencedFinalizedStages();
    }

    public string Root { get; }

    public bool IsHandoffRequestActive
    {
        get
        {
            lock (_gate)
            {
                return _isHandoffRequestActive;
            }
        }
    }

    public ApplianceUpdateOperationStatus? TryStart(string channel, string tag)
    {
        lock (_gate)
        {
            if (_status.Status is "queued" or "running")
            {
                return null;
            }
            DeleteUnreferencedFinalizedStages();
            var status = new ApplianceUpdateOperationStatus(
                "stage",
                channel,
                "queued",
                tag,
                null,
                OperationId: Guid.NewGuid().ToString("D"));
            PersistUnsafe(status, _status);
            _status = status;
            return _status;
        }
    }

    public ApplianceUpdateOperationStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public void SetRunning(string channel, string tag) =>
        Set(new(
            "stage",
            channel,
            "running",
            tag,
            null,
            OperationId: _status.OperationId,
            Phase: "verifying"));

    public void SetProgress(string phase, long? completedBytes = null, long? totalBytes = null)
    {
        if (totalBytes is <= 0 || completedBytes is < 0
            || completedBytes.HasValue != totalBytes.HasValue)
        {
            throw new ArgumentOutOfRangeException(nameof(completedBytes));
        }
        lock (_gate)
        {
            if (_status.Phase == phase && totalBytes.HasValue)
            {
                if (_status.TotalBytes.HasValue && _status.TotalBytes != totalBytes)
                {
                    throw new InvalidOperationException("The download phase total changed.");
                }
                completedBytes = Math.Max(completedBytes!.Value, _status.CompletedBytes ?? 0);
            }
            var status = _status with
            {
                Phase = phase,
                CompletedBytes = totalBytes.HasValue ? Math.Min(completedBytes!.Value, totalBytes.Value) : null,
                TotalBytes = totalBytes,
            };
            PersistUnsafe(status, _status);
            _status = status;
        }
    }

    public void SetHandingOff(string channel, string tag)
    {
        lock (_gate)
        {
            var status = new ApplianceUpdateOperationStatus(
                "handoff",
                channel,
                "running",
                tag,
                null,
                OperationId: _status.OperationId,
                Phase: "handing-off");
            PersistUnsafe(status, _status);
            _status = status;
            _isHandoffRequestActive = true;
        }
    }

    public void CompleteHandoffAttempt()
    {
        lock (_gate)
        {
            _isHandoffRequestActive = false;
        }
    }

    public void SetHandedOff(string channel, string tag) =>
        Set(new(
            "apply",
            channel,
            "running",
            tag,
            null,
            OperationId: _status.OperationId,
            Phase: "verifying"));

    public void SetFailed(string channel, string tag, string message) =>
        Set(new(
            "stage",
            channel,
            "failed",
            tag,
            message,
            OperationId: _status.OperationId));

    internal void SetFailedInMemory(
        string channel,
        string tag,
        string message)
    {
        lock (_gate)
        {
            _isHandoffRequestActive = false;
            _status = _status with
            {
                Action = "stage",
                Channel = channel,
                Status = "failed",
                Tag = tag,
                Message = message,
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            var previousTag = _status.Tag;
            var status = new ApplianceUpdateOperationStatus(
                "none",
                "none",
                "idle",
                null,
                null);
            PersistUnsafe(status, _status);
            _status = status;
            _isHandoffRequestActive = false;
            DeleteFinalizedStage(previousTag);
        }
    }

    private void Load()
    {
        if (!File.Exists(_operationPath))
        {
            return;
        }
        try
        {
            var durableStatus =
                JsonSerializer.Deserialize<ApplianceUpdateOperationStatus>(
                    File.ReadAllText(_operationPath))
                ?? _status;
            var status = durableStatus;
            if (status.Status == "queued"
                || status is { Action: "stage", Status: "running" })
            {
                status = status with
                {
                    Status = "failed",
                    Message = "AgentHost restarted while staging the update.",
                };
                PersistUnsafe(status, durableStatus);
            }
            _status = status;
        }
        catch (JsonException exception)
        {
            LogInvalidState(exception, _operationPath);
            var status = new ApplianceUpdateOperationStatus(
                "stage",
                "none",
                "failed",
                null,
                "The persisted staging operation is invalid.");
            PersistUnsafe(status, _status);
            _status = status;
        }
    }

    private void Set(ApplianceUpdateOperationStatus status)
    {
        lock (_gate)
        {
            if (status.Status == "failed" && status.OperationId == _status.OperationId)
            {
                status = status with
                {
                    Phase = _status.Phase,
                    CompletedBytes = _status.CompletedBytes,
                    TotalBytes = _status.TotalBytes,
                };
            }
            PersistUnsafe(status, _status);
            _isHandoffRequestActive = false;
            _status = status;
        }
    }

    private void PersistUnsafe(
        ApplianceUpdateOperationStatus status,
        ApplianceUpdateOperationStatus previous)
    {
        var replaced = false;
        try
        {
            WriteDurableUnsafe(status, ref replaced);
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
                    WriteDurableUnsafe(previous, ref restored);
                }
                catch (Exception restoreException) when (
                    restoreException is IOException
                        or UnauthorizedAccessException
                        or Win32Exception)
                {
                    throw new AggregateException(
                        "The staging transition and its durable rollback both failed.",
                        exception,
                        restoreException);
                }
            }
            throw;
        }
    }

    private void WriteDurableUnsafe(
        ApplianceUpdateOperationStatus status,
        ref bool replaced)
    {
        var temporary = _operationPath + ".tmp";
        using (var stream = new FileStream(
                   temporary,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        {
            JsonSerializer.Serialize(stream, status);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _operationPath, overwrite: true);
        replaced = true;
        if (OperatingSystem.IsLinux())
        {
            SyncDirectory(Root);
        }
    }

    private static void SyncDirectory(string path)
    {
        // Let libc select directory-open flags; their values differ on ARM64.
        var directory = OpenDirectory(path);
        if (directory == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        try
        {
            var descriptor = DirectoryDescriptor(directory);
            if (descriptor < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            if (Fsync(descriptor) != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            _ = CloseDirectory(directory);
        }
    }

    [DllImport("libc", EntryPoint = "opendir", SetLastError = true)]
    private static extern IntPtr OpenDirectory(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libc", EntryPoint = "dirfd", SetLastError = true)]
    private static extern int DirectoryDescriptor(IntPtr directory);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "closedir")]
    private static extern int CloseDirectory(IntPtr directory);

    private void DeleteOrphanedAttempts()
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     Root,
                     ".*.partial"))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The persisted staging state at {Path} is invalid.")]
    private partial void LogInvalidState(Exception exception, string path);
}
