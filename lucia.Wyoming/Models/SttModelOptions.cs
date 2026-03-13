namespace lucia.Wyoming.Models;

public sealed class SttModelOptions
{
    public const string SectionName = "Wyoming:Models:Stt";

    public string ActiveModel { get; set; } = "sherpa-onnx-streaming-zipformer-en-2023-06-26";
    public string ModelBasePath { get; set; } = "/models/stt";
    public int NumThreads { get; set; } = 4;
    public int SampleRate { get; set; } = 16000;
    public bool AllowCustomModels { get; set; } = true;
    public bool AutoDownloadDefault { get; set; } = true;
}
