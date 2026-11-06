namespace lucia.Agents.Family;

/// <summary>
/// The family member a request runs for, carried through agent and tool execution for one
/// request. No scope means the request is not attributed to a member and is not mediated.
/// </summary>
public sealed class MemberActionScope : IDisposable
{
    private static readonly AsyncLocal<MemberActionScope?> s_current = new();
    private readonly MemberActionScope? _previous;
    private bool _disposed;

    private MemberActionScope(FamilyMember member, ActionBroker broker)
    {
        _previous = s_current.Value;
        Member = member;
        Broker = broker;
        s_current.Value = this;
    }

    public FamilyMember Member { get; }

    public ActionBroker Broker { get; }

    public static MemberActionScope? Current => s_current.Value;

    public static MemberActionScope Begin(FamilyMember member, ActionBroker broker) => new(member, broker);

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
