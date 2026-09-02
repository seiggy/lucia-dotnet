using System.Text.Json;

namespace lucia.AgentHost.Appliance;

public sealed partial class ApplianceUpdateStagingStore
{
    private readonly object _gate = new();
    private readonly string _operationPath;
    private readonly ILogger<ApplianceUpdateStagingStore> _logger;
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

    internal ApplianceUpdateStagingStore(
        string root,
        ILogger<ApplianceUpdateStagingStore> logger)
    {
        _logger = logger;
        Root = root;
        _operationPath = Path.Combine(Root, "operation.json");
        Directory.CreateDirectory(Root);
        Load();
    }

    public string Root { get; }

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

    public void SetFailed(string channel, string tag, string message) =>
        Set(new("stage", channel, "failed", tag, message));

    public void Clear() =>
        Set(new("none", "none", "idle", null, null));

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
            if (_status.Status is "queued" or "running")
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
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The persisted staging state at {Path} is invalid.")]
    private partial void LogInvalidState(Exception exception, string path);
}
