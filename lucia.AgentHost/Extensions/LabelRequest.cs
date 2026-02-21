using lucia.Agents.Training.Models;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for updating a trace label.
/// </summary>
public sealed class LabelRequest
{
    public LabelStatus Status { get; set; }

    public string? ReviewerNotes { get; set; }

    public string? CorrectionText { get; set; }
}
