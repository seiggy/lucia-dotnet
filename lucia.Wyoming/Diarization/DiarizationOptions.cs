namespace lucia.Wyoming.Diarization;

public sealed class DiarizationOptions
{
    public const string SectionName = "Wyoming:Diarization";

    public string ActiveModel { get; set; } = "nemo_en_titanet_small";
    public string ModelBasePath { get; set; } = "./models/speaker-embedding";
    public bool AutoDownloadDefault { get; set; } = true;

    public bool Enabled { get; set; } = true;
    public string SegmentationModelPath { get; set; } = "";
    public string EmbeddingModelPath { get; set; } = "";
    public float SpeakerThreshold { get; set; } = 0.35f;

    /// <summary>
    /// Database name for persistent speaker profile storage.
    /// Used only when a persistent store is available; otherwise profiles are stored in memory.
    /// </summary>
    public string ProfileStoreDatabaseName { get; set; } = "luciawyoming";
}
