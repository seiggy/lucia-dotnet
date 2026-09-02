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
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(directory);
            if (name != _status.Tag
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
            _status = new("stage", channel, "queued", tag, null);
            PersistUnsafe();
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
        Set(new("stage", channel, "running", tag, null));

    public void SetHandingOff(string channel, string tag)
    {
        lock (_gate)
        {
            _isHandoffRequestActive = true;
            _status = new("handoff", channel, "running", tag, null);
            PersistUnsafe();
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
        Set(new("apply", channel, "running", tag, null));

    public void SetFailed(string channel, string tag, string message) =>
        Set(new("stage", channel, "failed", tag, message));

    public void Clear()
    {
        ApplianceUpdateOperationStatus previous;
        lock (_gate)
        {
            previous = _status;
            _isHandoffRequestActive = false;
            _status = new("none", "none", "idle", null, null);
            PersistUnsafe();
        }
        DeleteFinalizedStage(previous.Tag);
    }

    private void Load()
    {
        if (!File.Exists(_operationPath))
        {
            return;
        }
        try
        {
            _status = JsonSerializer.Deserialize<ApplianceUpdateOperationStatus>(
                    File.ReadAllText(_operationPath))
                ?? _status;
            if (_status.Status == "queued"
                || _status is { Action: "stage", Status: "running" })
            {
                _status = _status with
                {
                    Status = "failed",
                    Message = "AgentHost restarted while staging the update.",
                };
                PersistUnsafe();
            }
        }
        catch (JsonException exception)
        {
            LogInvalidState(exception, _operationPath);
            _status = new(
                "stage",
                "none",
                "failed",
                null,
                "The persisted staging operation is invalid.");
            PersistUnsafe();
        }
    }

    private void Set(ApplianceUpdateOperationStatus status)
    {
        lock (_gate)
        {
            _isHandoffRequestActive = false;
            _status = status;
            PersistUnsafe();
        }
    }

    private void PersistUnsafe()
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
        if (OperatingSystem.IsLinux())
        {
            SyncDirectory(Root);
        }
    }

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
