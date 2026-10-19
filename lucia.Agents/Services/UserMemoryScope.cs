namespace lucia.Agents.Services;

/// <summary>
/// Carries a server-verified enrolled profile ID for one request, never for an agent session.
/// </summary>
public sealed class UserMemoryScope : IDisposable
{
    private static readonly AsyncLocal<UserMemoryScope?> s_current = new();
    private readonly UserMemoryScope? _previous;
    private int _turnClaimed;
    private bool _disposed;

    private UserMemoryScope(string? enrolledProfileId, string? utterance)
    {
        _previous = s_current.Value;
        UserId = string.IsNullOrWhiteSpace(enrolledProfileId) ? null : enrolledProfileId;
        Utterance = UserId is null || string.IsNullOrWhiteSpace(utterance) ? null : utterance;
        s_current.Value = this;
    }

    private string? UserId { get; }

    private string? Utterance { get; }

    public static string? CurrentUserId => s_current.Value?.UserId;

    /// <summary>
    /// What the verified speaker actually said this turn, without generated prompt context.
    /// </summary>
    public static string? CurrentUtterance => s_current.Value?.Utterance;

    /// <summary>
    /// Begins a scope using only an authorized, nonprovisional enrolled profile ID.
    /// Pass null for unknown speakers, including inside an existing scope.
    /// </summary>
    public static UserMemoryScope Begin(string? enrolledProfileId, string? utterance = null) => new(enrolledProfileId, utterance);

    /// <summary>
    /// Returns true once per verified scope, so agents running in parallel record the turn a single time.
    /// </summary>
    public static bool TryClaimTurn() =>
        s_current.Value is { UserId: not null } scope && Interlocked.Exchange(ref scope._turnClaimed, 1) == 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        s_current.Value = _previous;
        _disposed = true;
    }
}
