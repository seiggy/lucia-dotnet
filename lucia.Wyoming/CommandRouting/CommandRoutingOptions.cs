namespace lucia.Wyoming.CommandRouting;

public sealed class CommandRoutingOptions
{
    public const string SectionName = "Wyoming:CommandRouting";

    public bool Enabled { get; set; } = true;

    public float ConfidenceThreshold { get; set; } = 0.8f;

    public bool FallbackToLlm { get; set; } = true;
}
