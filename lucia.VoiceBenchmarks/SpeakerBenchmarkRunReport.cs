using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkRunReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CommandLine { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string ManifestSha256 { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public string Processor { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string SherpaOnnxVersion { get; init; } = string.Empty;
    public string OnnxRuntimeVersion { get; init; } = string.Empty;
    public int WarmupRuns { get; init; }
    public int MeasuredRuns { get; init; }
    public int Concurrency { get; init; }
    public string SplitPolicy { get; init; } = string.Empty;
    public string ScorePolicy { get; init; } = string.Empty;
    public string AudioPreprocessing { get; init; } = string.Empty;
    public IReadOnlyList<BenchmarkClipProvenance> DatasetClips { get; init; } =
        Array.Empty<BenchmarkClipProvenance>();
    public IReadOnlyList<SpeakerBenchmarkModelResult> Models { get; init; } = Array.Empty<SpeakerBenchmarkModelResult>();

    public void WriteJson(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(path, json);
    }

    public void WriteMarkdown(string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Voice embedding benchmark report");
        builder.AppendLine();
        builder.AppendLine($"- Generated at UTC: {GeneratedAtUtc:O}");
        builder.AppendLine($"- Manifest: `{ManifestPath}`");
        builder.AppendLine($"- Manifest SHA-256: `{ManifestSha256}`");
        builder.AppendLine($"- Command: `{CommandLine}`");
        builder.AppendLine($"- Output directory: `{OutputDirectory}`");
        builder.AppendLine($"- OS: {OperatingSystem}");
        builder.AppendLine($"- Runtime: {Runtime}");
        builder.AppendLine($"- Processor: {Processor}");
        builder.AppendLine($"- Architecture: {Architecture}");
        builder.AppendLine($"- sherpa-onnx: {SherpaOnnxVersion}");
        builder.AppendLine($"- ONNX Runtime: {OnnxRuntimeVersion}");
        builder.AppendLine($"- Warm-up runs: {WarmupRuns}");
        builder.AppendLine($"- Measured runs: {MeasuredRuns}");
        builder.AppendLine($"- Concurrency: {Concurrency}");
        builder.AppendLine($"- Split policy: {SplitPolicy}");
        builder.AppendLine($"- Score policy: {ScorePolicy}");
        builder.AppendLine($"- Audio preprocessing: {AudioPreprocessing}");
        builder.AppendLine();
        builder.AppendLine("## Dataset clips");
        builder.AppendLine();
        builder.AppendLine("| Split | Speaker | SHA-256 | Path |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var clip in DatasetClips)
        {
            builder.AppendLine($"| {clip.Split} | {clip.SpeakerId} | `{clip.Sha256}` | `{clip.Path}` |");
        }
        builder.AppendLine();

        if (Models.Count == 0)
        {
            builder.AppendLine("No model results were produced.");
        }
        else
        {
            foreach (var model in Models)
            {
                builder.AppendLine($"## {model.ModelName}");
                builder.AppendLine();
                builder.AppendLine($"- Model path: `{model.ModelPath}`");
                builder.AppendLine($"- Model SHA-256: `{model.ModelSha256}`");
                builder.AppendLine($"- Model source: `{model.ModelSourceUri}`");
                builder.AppendLine($"- Threshold development manifest: `{model.ThresholdDevelopmentManifestPath}`");
                builder.AppendLine($"- Threshold development manifest SHA-256: `{model.ThresholdDevelopmentManifestSha256}`");
                builder.AppendLine($"- Provider: {model.Provider}");
                builder.AppendLine($"- Threads: {model.ThreadCount}");
                builder.AppendLine($"- Embedding dimension: {model.EmbeddingDimension}");
                builder.AppendLine($"- Speakers: {model.SpeakerCount}");
                builder.AppendLine($"- Enrollment clips: {model.EnrollmentClipCount}");
                builder.AppendLine($"- Test clips: {model.TestClipCount}");
                builder.AppendLine($"- Top-1 accuracy: {model.Top1Accuracy.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Equal error rate: {model.EqualErrorRate.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Verification threshold: {model.VerificationThreshold.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- False acceptance rate: {model.FalseAcceptanceRate.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- False rejection rate: {model.FalseRejectionRate.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Normalized minDCF (Ptarget=0.01): {model.NormalizedMinDcf.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Mean real-time factor: {model.MeanRealTimeFactor.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Wall duration (s): {model.WallDurationSeconds.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- CPU core-equivalents: {model.CpuCoreEquivalents.ToString("0.0000", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"- Mean CPU utilization: {model.CpuUtilizationPercent.ToString("0.00", CultureInfo.InvariantCulture)}%");
                builder.AppendLine($"- Managed allocation delta (bytes): {model.ManagedAllocationDeltaBytes}");
                builder.AppendLine($"- Working set before/after (bytes): {model.WorkingSetBeforeBytes} / {model.WorkingSetAfterBytes}");
                builder.AppendLine("- Note: managed allocation values exclude native ONNX allocations; working set is process-level and is not a clean model-peak measurement.");
                builder.AppendLine($"- Genuine scores: {FormatRange(model.GenuineScores)}");
                builder.AppendLine($"- Impostor scores: {FormatRange(model.ImpostorScores)}");
                builder.AppendLine();
            }
        }

        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, builder.ToString());
    }

    private static string FormatRange(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", values.Select(value => value.ToString("0.000000", CultureInfo.InvariantCulture))) + "]";
    }
}
