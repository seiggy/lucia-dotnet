using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using lucia.Wyoming.Diarization;
using SherpaOnnx;

namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkRunner
{
    private readonly string _manifestPath;
    private readonly IReadOnlyList<string> _modelPaths;
    private readonly string _outputDirectory;
    private readonly string _commandLine;

    public SpeakerBenchmarkRunner(
        string manifestPath,
        IReadOnlyList<string> modelPaths,
        string outputDirectory,
        string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        _manifestPath = Path.GetFullPath(manifestPath);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _modelPaths = modelPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _commandLine = commandLine;
    }

    public SpeakerBenchmarkRunReport Run()
    {
        var manifest = SpeakerBenchmarkManifest.Load(_manifestPath);
        var validationErrors = manifest.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Manifest validation failed: {string.Join("; ", validationErrors)}");
        }

        var metrics = new List<SpeakerBenchmarkModelResult>();
        foreach (var modelPath in _modelPaths)
        {
            var resolvedModelPath = ResolveModelPath(modelPath);
            metrics.Add(EvaluateModel(manifest, resolvedModelPath));
        }

        return new SpeakerBenchmarkRunReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CommandLine = _commandLine,
            ManifestPath = manifest.ManifestPath,
            OutputDirectory = _outputDirectory,
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown",
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Models = metrics
                .OrderBy(static metric => metric.ModelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static metric => metric.ModelPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public void WriteReports(SpeakerBenchmarkRunReport report)
    {
        Directory.CreateDirectory(_outputDirectory);
        var jsonPath = Path.Combine(_outputDirectory, "voice-benchmark-report.json");
        var markdownPath = Path.Combine(_outputDirectory, "voice-benchmark-report.md");
        report.WriteJson(jsonPath);
        report.WriteMarkdown(markdownPath);
    }

    private SpeakerBenchmarkModelResult EvaluateModel(SpeakerBenchmarkManifest manifest, string modelPath)
    {
        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        var managedBefore = GC.GetTotalAllocatedBytes();
        var cpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();

        var enrollClips = manifest.Clips
            .Where(static clip => string.Equals(clip.Split, "enroll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static clip => clip.ResolvedPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var testClips = manifest.Clips
            .Where(static clip => string.Equals(clip.Split, "test", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static clip => clip.ResolvedPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var extractor = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig
        {
            Model = modelPath,
            NumThreads = Math.Max(1, Environment.ProcessorCount),
            Provider = "cpu",
        });

        var centroids = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        var meanRealTimeFactorValues = new List<double>();
        foreach (var speakerGroup in enrollClips
                     .GroupBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var embeddings = new List<float[]>();
            foreach (var clip in speakerGroup.OrderBy(static clip => clip.ResolvedPath, StringComparer.OrdinalIgnoreCase))
            {
                var embedding = ExtractClipEmbedding(extractor, clip.ResolvedPath, meanRealTimeFactorValues);
                embeddings.Add(embedding);
            }

            centroids[speakerGroup.Key] = IDiarizationEngine.ComputeAverageEmbedding(embeddings);
        }

        var genuineScores = new List<double>();
        var impostorScores = new List<double>();
        var predictions = new List<SpeakerBenchmarkPrediction>();
        foreach (var testClip in testClips)
        {
            var testEmbedding = new SpeakerEmbedding
            {
                Vector = ExtractClipEmbedding(extractor, testClip.ResolvedPath, meanRealTimeFactorValues),
            };

            var bestSpeaker = string.Empty;
            var bestSimilarity = double.NegativeInfinity;
            foreach (var centroid in centroids.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var profileEmbedding = new SpeakerEmbedding
                {
                    Vector = centroid.Value,
                };

                var similarity = testEmbedding.CosineSimilarity(profileEmbedding);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestSpeaker = centroid.Key;
                }

                if (string.Equals(testClip.SpeakerId, centroid.Key, StringComparison.OrdinalIgnoreCase))
                {
                    genuineScores.Add(similarity);
                }
                else
                {
                    impostorScores.Add(similarity);
                }
            }

            predictions.Add(new SpeakerBenchmarkPrediction(testClip.SpeakerId, bestSpeaker, bestSimilarity));
        }

        var top1Accuracy = SpeakerBenchmarkMetrics.ComputeTop1Accuracy(predictions);
        var equalErrorRate = SpeakerBenchmarkMetrics.ComputeEer(genuineScores, impostorScores);
        var meanRealTimeFactor = meanRealTimeFactorValues.Count == 0 ? 0d : meanRealTimeFactorValues.Average();
        var wallDurationSeconds = stopwatch.Elapsed.TotalSeconds;
        var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
        var cpuUtilizationPercent = wallDurationSeconds <= 0d
            ? 0d
            : cpuSeconds / (wallDurationSeconds * Math.Max(1, Environment.ProcessorCount)) * 100d;
        var managedAllocationDeltaBytes = GC.GetTotalAllocatedBytes() - managedBefore;
        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        using var modelStream = File.OpenRead(modelPath);
        var modelSha256 = Convert.ToHexStringLower(SHA256.HashData(modelStream));

        return new SpeakerBenchmarkModelResult
        {
            ModelPath = modelPath,
            ModelName = Path.GetFileNameWithoutExtension(modelPath),
            ModelSha256 = modelSha256,
            EmbeddingDimension = centroids.Count == 0 ? 0 : centroids.First().Value.Length,
            SpeakerCount = centroids.Count,
            EnrollmentClipCount = enrollClips.Count,
            TestClipCount = testClips.Count,
            Top1Accuracy = top1Accuracy,
            EqualErrorRate = equalErrorRate,
            MeanRealTimeFactor = meanRealTimeFactor,
            WallDurationSeconds = wallDurationSeconds,
            CpuUtilizationPercent = cpuUtilizationPercent,
            ManagedAllocationDeltaBytes = managedAllocationDeltaBytes,
            WorkingSetBeforeBytes = workingSetBefore,
            WorkingSetAfterBytes = workingSetAfter,
            GenuineScores = genuineScores.OrderBy(static score => score).ToArray(),
            ImpostorScores = impostorScores.OrderBy(static score => score).ToArray(),
            Predictions = predictions
                .OrderBy(static prediction => prediction.ActualSpeakerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static prediction => prediction.PredictedSpeakerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static prediction => prediction.Score)
                .ToArray(),
        };
    }

    private static float[] ExtractClipEmbedding(
        SpeakerEmbeddingExtractor extractor,
        string wavPath,
        ICollection<double> meanRealTimeFactorValues)
    {
        var samples = AudioWaveLoader.LoadMono16KhzFloatSamples(wavPath);
        var audioDurationSeconds = samples.Length / 16000d;
        var timer = Stopwatch.StartNew();

        using var stream = extractor.CreateStream();
        stream.AcceptWaveform(16000, samples);
        stream.InputFinished();
        var embedding = extractor.Compute(stream);
        timer.Stop();

        if (audioDurationSeconds > 0d)
        {
            meanRealTimeFactorValues.Add(timer.Elapsed.TotalSeconds / audioDurationSeconds);
        }

        return embedding;
    }

    private static string ResolveModelPath(string modelPath)
    {
        var fullPath = Path.GetFullPath(modelPath);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (Directory.Exists(fullPath))
        {
            var onnxModel = Directory
                .EnumerateFiles(fullPath, "*.onnx", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (onnxModel is not null)
            {
                return onnxModel;
            }
        }

        throw new FileNotFoundException($"Speaker embedding model not found: {fullPath}", fullPath);
    }
}
