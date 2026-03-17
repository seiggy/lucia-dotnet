namespace lucia.Wyoming.WakeWord;

public sealed record CalibrationDetection
{
    public required bool Detected { get; init; }
    public required float Confidence { get; init; }
    public required int AudioDurationMs { get; init; }
}
