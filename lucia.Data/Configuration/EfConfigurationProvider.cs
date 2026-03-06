using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace lucia.Data.Configuration;

/// <summary>
/// Configuration provider that reads key-value pairs from an EF Core database.
/// Supports periodic polling for change detection to enable IOptionsMonitor hot-reload.
/// </summary>
public sealed class EfConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly Func<DbContextOptions<LuciaDbContext>> _optionsFactory;
    private readonly TimeSpan _pollInterval;
    private Timer? _pollTimer;
    private DateTime _lastLoadTime = DateTime.MinValue;

    public EfConfigurationProvider(
        Func<DbContextOptions<LuciaDbContext>> optionsFactory,
        TimeSpan? pollInterval = null)
    {
        _optionsFactory = optionsFactory;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public override void Load()
    {
        try
        {
            using var db = new LuciaDbContext(_optionsFactory());

            db.Database.EnsureCreated();

            var entries = db.ConfigEntries.AsNoTracking().ToList();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                data[entry.Key] = entry.Value;
            }

            Data = data;
            _lastLoadTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // If DB is unavailable, fall through to lower-priority config sources
            Console.WriteLine($"[lucia] EfConfigurationProvider: Failed to load — {ex.Message}");
        }

        // Start polling for changes after initial load
        _pollTimer ??= new Timer(PollForChanges, null, _pollInterval, _pollInterval);
    }

    private void PollForChanges(object? state)
    {
        try
        {
            using var db = new LuciaDbContext(_optionsFactory());

            var hasChanges = db.ConfigEntries
                .AsNoTracking()
                .Any(e => e.UpdatedAt > _lastLoadTime);

            if (hasChanges)
            {
                Load();
                OnReload();
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw — config stays at last known good state
            Console.Error.WriteLine($"[lucia] EfConfigurationProvider: Poll failed — {ex.Message}");
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}
