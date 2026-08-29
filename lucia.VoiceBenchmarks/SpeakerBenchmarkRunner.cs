using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using lucia.Wyoming.Diarization;
using Microsoft.ML.OnnxRuntime;
using SherpaOnnx;

namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkRunner
{
    private const string Provider = "cpu";
    private const int WarmupRuns = 1;
    private const int MeasuredRuns = 1;
    private const int Concurrency = 1;
    private const double MinDcfTargetPrior = 0.01d;
    private readonly string _manifestPath;
    private readonly IReadOnlyList<(string Path, string SourceUri, double Threshold, string ThresholdManifestPath)> _models;
    private readonly string _outputDirectory;
    private readonly string _commandLine;

    public SpeakerBenchmarkRunner(
        string manifestPath,
        IReadOnlyList<string> modelPaths,
        IReadOnlyList<string> modelSourceUris,
        IReadOnlyList<double> modelThresholds,
        IReadOnlyList<string> modelThresholdManifestPaths,
        string outputDirectory,
        string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        _manifestPath = Path.GetFullPath(manifestPath);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        if (modelPaths.Count != modelSourceUris.Count
            || modelPaths.Count != modelThresholds.Count
            || modelPaths.Count != modelThresholdManifestPaths.Count)
        {
            throw new ArgumentException("Model paths and source URLs must have matching counts.");
        }

        _models = modelPaths
            .Select((path, index) => (
                Path: path,
                SourceUri: modelSourceUris[index],
                Threshold: modelThresholds[index],
                ThresholdManifestPath: Path.GetFullPath(modelThresholdManifestPaths[index])))
            .OrderBy(static model => model.Path, FileSystemPathComparer.Instance)
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

        var evaluationManifestSha256 = ComputeSha256(manifest.ManifestPath);
        HashSet<string>? evaluationClipHashes = null;
        foreach (var thresholdManifestPath in _models
            .Select(static model => model.ThresholdManifestPath)
            .Distinct(FileSystemPathComparer.Instance))
        {
            var thresholdManifest = SpeakerBenchmarkManifest.Load(thresholdManifestPath);
            var thresholdValidationErrors = thresholdManifest.Validate();
            if (thresholdValidationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Threshold development manifest validation failed: {string.Join("; ", thresholdValidationErrors)}");
            }
            if (string.Equals(
                ComputeSha256(thresholdManifest.ManifestPath),
                evaluationManifestSha256,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Threshold development manifest must differ from the evaluation manifest.");
            }

            evaluationClipHashes ??= manifest.Clips
                .Select(static clip => ComputeSha256(clip.ResolvedPath))
                .ToHashSet(StringComparer.Ordinal);
            var thresholdClips = thresholdManifest.Clips
                .Select(clip => new BenchmarkClipProvenance(
                    clip.Path,
                    clip.SpeakerId,
                    clip.Split,
                    ComputeSha256(clip.ResolvedPath)))
                .ToArray();
            var thresholdContentErrors = SpeakerBenchmarkManifest.ValidateContentHashes(thresholdClips);
            if (thresholdContentErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Threshold development manifest validation failed: {string.Join("; ", thresholdContentErrors)}");
            }

            var thresholdClipHashes = thresholdClips
                .Select(static clip => clip.Sha256)
                .ToHashSet(StringComparer.Ordinal);
            if (evaluationClipHashes.Overlaps(thresholdClipHashes))
            {
                throw new InvalidOperationException(
                    "Threshold development audio must not overlap evaluation audio.");
            }
        }

        var datasetClips = manifest.Clips
            .Select(clip => new BenchmarkClipProvenance(
                clip.Path,
                clip.SpeakerId,
                clip.Split,
                ComputeSha256(clip.ResolvedPath)))
            .OrderBy(static clip => clip.Split, StringComparer.Ordinal)
            .ThenBy(static clip => clip.SpeakerId, StringComparer.Ordinal)
            .ThenBy(static clip => clip.Path, StringComparer.Ordinal)
            .ToArray();
        var contentErrors = SpeakerBenchmarkManifest.ValidateContentHashes(datasetClips);
        if (contentErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Manifest validation failed: {string.Join("; ", contentErrors)}");
        }

        var audioSamples = manifest.Clips.ToDictionary(
            static clip => clip.ResolvedPath,
            static clip => AudioWaveLoader.LoadMono16KhzFloatSamples(clip.ResolvedPath),
            FileSystemPathComparer.Instance);
        var metrics = new List<SpeakerBenchmarkModelResult>();
        foreach (var model in _models)
        {
            var resolvedModelPath = ResolveModelPath(model.Path);
            metrics.Add(EvaluateModel(
                manifest,
                audioSamples,
                resolvedModelPath,
                model.SourceUri,
                model.Threshold,
                model.ThresholdManifestPath));
        }

        return new SpeakerBenchmarkRunReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CommandLine = _commandLine,
            ManifestPath = manifest.ManifestPath,
            ManifestSha256 = evaluationManifestSha256,
            OutputDirectory = _outputDirectory,
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Processor = GetProcessorName(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SherpaOnnxVersion = GetAssemblyVersion(typeof(SpeakerEmbeddingExtractor).Assembly),
            OnnxRuntimeVersion = GetAssemblyVersion(typeof(InferenceSession).Assembly),
            WarmupRuns = WarmupRuns,
            MeasuredRuns = MeasuredRuns,
            Concurrency = Concurrency,
            SplitPolicy = "Manifest-defined enroll/test clips; resolved paths must not overlap.",
            ScorePolicy = "Closed-set cosine top-1; EER and minDCF from all scores; FAR/FRR use each model's frozen threshold.",
            AudioPreprocessing = "NAudio float conversion, stereo downmix to mono, WDL resampling to 16 kHz.",
            DatasetClips = datasetClips,
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

    private SpeakerBenchmarkModelResult EvaluateModel(
        SpeakerBenchmarkManifest manifest,
        IReadOnlyDictionary<string, float[]> audioSamples,
        string modelPath,
        string modelSourceUri,
        double verificationThreshold,
        string thresholdManifestPath)
    {
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

        var threadCount = Math.Max(1, Environment.ProcessorCount);
        using var extractor = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig
        {
            Model = modelPath,
            NumThreads = threadCount,
            Provider = Provider,
        });
        _ = ExtractClipEmbedding(
            extractor,
            audioSamples[enrollClips[0].ResolvedPath],
            meanRealTimeFactorValues: null);

        var modelSha256 = ComputeSha256(modelPath);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var managedBefore = GC.GetTotalAllocatedBytes();
        var cpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();

        var centroids = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        var meanRealTimeFactorValues = new List<double>();
        foreach (var speakerGroup in enrollClips
                     .GroupBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var embeddings = new List<float[]>();
            foreach (var clip in speakerGroup.OrderBy(static clip => clip.ResolvedPath, StringComparer.OrdinalIgnoreCase))
            {
                var embedding = ExtractClipEmbedding(
                    extractor,
                    audioSamples[clip.ResolvedPath],
                    meanRealTimeFactorValues);
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
                Vector = ExtractClipEmbedding(
                    extractor,
                    audioSamples[testClip.ResolvedPath],
                    meanRealTimeFactorValues),
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
        var (falseAcceptanceRate, falseRejectionRate) =
            SpeakerBenchmarkMetrics.ComputeErrorRates(
                genuineScores,
                impostorScores,
                verificationThreshold);
        var normalizedMinDcf = SpeakerBenchmarkMetrics.ComputeNormalizedMinDcf(
            genuineScores,
            impostorScores,
            MinDcfTargetPrior);
        var meanRealTimeFactor = meanRealTimeFactorValues.Count == 0 ? 0d : meanRealTimeFactorValues.Average();
        var wallDurationSeconds = stopwatch.Elapsed.TotalSeconds;
        var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
        var cpuCoreEquivalents = wallDurationSeconds <= 0d ? 0d : cpuSeconds / wallDurationSeconds;
        var cpuUtilizationPercent = wallDurationSeconds <= 0d
            ? 0d
            : cpuSeconds / (wallDurationSeconds * Math.Max(1, Environment.ProcessorCount)) * 100d;
        var managedAllocationDeltaBytes = GC.GetTotalAllocatedBytes() - managedBefore;
        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        return new SpeakerBenchmarkModelResult
        {
            ModelPath = modelPath,
            ModelName = Path.GetFileNameWithoutExtension(modelPath),
            ModelSha256 = modelSha256,
            ModelSourceUri = modelSourceUri,
            ThresholdDevelopmentManifestPath = thresholdManifestPath,
            ThresholdDevelopmentManifestSha256 = ComputeSha256(thresholdManifestPath),
            Provider = Provider,
            ThreadCount = threadCount,
            EmbeddingDimension = centroids.Count == 0 ? 0 : centroids.First().Value.Length,
            SpeakerCount = centroids.Count,
            EnrollmentClipCount = enrollClips.Count,
            TestClipCount = testClips.Count,
            Top1Accuracy = top1Accuracy,
            EqualErrorRate = equalErrorRate,
            FalseAcceptanceRate = falseAcceptanceRate,
            FalseRejectionRate = falseRejectionRate,
            NormalizedMinDcf = normalizedMinDcf,
            VerificationThreshold = verificationThreshold,
            MeanRealTimeFactor = meanRealTimeFactor,
            WallDurationSeconds = wallDurationSeconds,
            CpuCoreEquivalents = cpuCoreEquivalents,
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
        float[] samples,
        ICollection<double>? meanRealTimeFactorValues)
    {
        var audioDurationSeconds = samples.Length / 16000d;
        var timer = Stopwatch.StartNew();

        using var stream = extractor.CreateStream();
        stream.AcceptWaveform(16000, samples);
        stream.InputFinished();
        var embedding = extractor.Compute(stream);
        timer.Stop();

        if (audioDurationSeconds > 0d && meanRealTimeFactorValues is not null)
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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string GetAssemblyVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "Unknown";

    private static string GetProcessorName()
    {
        var windowsName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(windowsName))
        {
            return windowsName;
        }

        const string CpuInfoPath = "/proc/cpuinfo";
        if (!File.Exists(CpuInfoPath))
        {
            return RuntimeInformation.ProcessArchitecture.ToString();
        }

        var cpuInfo = File.ReadLines(CpuInfoPath)
            .Select(static line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length == 2)
            .GroupBy(static parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First()[1], StringComparer.OrdinalIgnoreCase);
        if (cpuInfo.TryGetValue("model name", out var modelName))
        {
            return modelName;
        }

        var identifiers = new[] { "Model", "CPU implementer", "CPU architecture", "CPU part" }
            .Where(cpuInfo.ContainsKey)
            .Select(key => $"{key}: {cpuInfo[key]}")
            .ToArray();
        return identifiers.Length > 0
            ? string.Join(", ", identifiers)
            : RuntimeInformation.ProcessArchitecture.ToString();
    }

}
