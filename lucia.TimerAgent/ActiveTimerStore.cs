using System.Collections.Concurrent;

namespace lucia.TimerAgent;

/// <summary>
/// Thread-safe singleton store for active timers.
/// Shared between <see cref="TimerSkill"/> (writes) and <see cref="TimerExecutionService"/> (reads/removes).
/// </summary>
public sealed class ActiveTimerStore
{
    private readonly ConcurrentDictionary<string, ActiveTimer> _timers = new();

    /// <summary>
    /// Adds or replaces a timer in the store.
    /// </summary>
    public void Add(ActiveTimer timer) => _timers[timer.Id] = timer;

    /// <summary>
    /// Attempts to remove and return a timer by its ID.
    /// </summary>
    public bool TryRemove(string timerId, out ActiveTimer? timer)
        => _timers.TryRemove(timerId, out timer);

    /// <summary>
    /// Attempts to add a timer only if no timer with the same ID exists.
    /// </summary>
    public bool TryAdd(ActiveTimer timer) => _timers.TryAdd(timer.Id, timer);

    /// <summary>
    /// Returns a snapshot of all active timers.
    /// </summary>
    public IReadOnlyCollection<ActiveTimer> GetAll() => _timers.Values.ToList();

    /// <summary>
    /// Returns the number of active timers.
    /// </summary>
    public int Count => _timers.Count;

    /// <summary>
    /// Returns true if there are no active timers.
    /// </summary>
    public bool IsEmpty => _timers.IsEmpty;
}
