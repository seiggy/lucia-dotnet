namespace lucia.Wyoming.Diarization;

public sealed class DiarizationOptions
{
    public const string SectionName = "Wyoming:Diarization";

    public bool Enabled { get; set; } = true;
    public string SegmentationModelPath { get; set; } = "";
    public string EmbeddingModelPath { get; set; } = "";
    public float SpeakerThreshold { get; set; } = 0.7f;
}
