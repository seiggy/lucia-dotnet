namespace lucia.Wyoming.Diarization;

public sealed class DiarizationOptions
{
    public const string SectionName = "Wyoming:Diarization";

    public string ActiveModel { get; set; } = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k";
    public string ModelBasePath { get; set; } = "./models/speaker-embedding";
    public bool AutoDownloadDefault { get; set; } = true;

    public bool Enabled { get; set; } = true;
    public string SegmentationModelPath { get; set; } = "";
    public string EmbeddingModelPath { get; set; } = "";
    public float SpeakerThreshold { get; set; } = 0.7f;

    /// <summary>
    /// MongoDB database name for persistent speaker profile storage.
    /// Used only when a MongoDB connection is available; otherwise profiles are stored in memory.
    /// </summary>
    public string ProfileStoreDatabaseName { get; set; } = "luciawyoming";
}
