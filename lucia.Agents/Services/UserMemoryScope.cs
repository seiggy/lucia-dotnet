namespace lucia.Agents.Services;

/// <summary>
/// Carries a server-verified enrolled profile ID for one request, never for an agent session.
/// </summary>
public sealed class UserMemoryScope : IDisposable
{
    private static readonly AsyncLocal<string?> s_currentUserId = new();
    private readonly string? _previousUserId;
    private bool _disposed;

    private UserMemoryScope(string? enrolledProfileId)
    {
        _previousUserId = s_currentUserId.Value;
        s_currentUserId.Value = string.IsNullOrWhiteSpace(enrolledProfileId) ? null : enrolledProfileId;
    }

    public static string? CurrentUserId => s_currentUserId.Value;

    /// <summary>
    /// Begins a scope using only an authorized, nonprovisional enrolled profile ID.
    /// Pass null for unknown speakers, including inside an existing scope.
    /// </summary>
    public static UserMemoryScope Begin(string? enrolledProfileId) => new(enrolledProfileId);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        s_currentUserId.Value = _previousUserId;
        _disposed = true;
    }
}
