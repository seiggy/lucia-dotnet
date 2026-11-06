namespace lucia.Agents.Family;

/// <summary>
/// The broker's decision for one capability invocation. <see cref="Record"/> is the approval
/// record that is pending, or the approved record claimed for this execution.
/// </summary>
public sealed record BrokerOutcome(BrokerOutcomeKind Kind, string Message, ActionRecord? Record)
{
    public static BrokerOutcome Proceed(ActionRecord? claimed = null) => new(BrokerOutcomeKind.Proceed, string.Empty, claimed);

    public static BrokerOutcome Denied(string message) => new(BrokerOutcomeKind.Denied, message, null);

    public static BrokerOutcome Pending(string message, ActionRecord record) => new(BrokerOutcomeKind.Pending, message, record);
}
