namespace lucia.Agents.Training.Models;

/// <summary>
/// Human-applied quality label for a conversation trace.
/// </summary>
public sealed class TraceLabel
{
    public LabelStatus Status { get; set; } = LabelStatus.Unlabeled;

    public string? ReviewerNotes { get; set; }

    public string? CorrectionText { get; set; }

    public DateTime? LabeledAt { get; set; }
}
