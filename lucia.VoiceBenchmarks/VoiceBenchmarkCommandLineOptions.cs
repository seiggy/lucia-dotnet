using System.Globalization;

namespace lucia.VoiceBenchmarks;

public sealed class VoiceBenchmarkCommandLineOptions
{
    public string ManifestPath { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> ModelPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ModelSourceUris { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> ModelThresholds { get; init; } = Array.Empty<double>();
    public IReadOnlyList<string> ModelThresholdManifestPaths { get; init; } = Array.Empty<string>();

    public static VoiceBenchmarkCommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("No arguments were provided.");
        }

        string? manifestPath = null;
        string? outputDirectory = null;
        var modelPaths = new List<string>();
        var modelSourceUris = new List<string>();
        var modelThresholds = new List<double>();
        var modelThresholdManifestPaths = new List<string>();
        var positionals = new Queue<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (index == 0
                && string.Equals(argument, "speaker", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(argument, "--manifest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-m", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--manifest requires a path.");
                }

                manifestPath = args[++index];
                continue;
            }

            if (string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "-o", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--output requires a path.");
                }

                outputDirectory = args[++index];
                continue;
            }

            if (string.Equals(argument, "--model", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-M", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--model requires a model path.");
                }

                modelPaths.Add(args[++index]);
                continue;
            }

            if (string.Equals(argument, "--model-source", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--model-source requires an absolute URL.");
                }

                modelSourceUris.Add(args[++index]);
                continue;
            }

            if (string.Equals(argument, "--model-threshold", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length
                    || !double.TryParse(
                        args[++index],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var threshold)
                    || !double.IsFinite(threshold)
                    || threshold is < -1d or > 1d)
                {
                    throw new ArgumentException("--model-threshold requires a number from -1 to 1.");
                }

                modelThresholds.Add(threshold);
                continue;
            }

            if (string.Equals(argument, "--model-threshold-manifest", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("--model-threshold-manifest requires a path.");
                }

                modelThresholdManifestPaths.Add(args[++index]);
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown option '{argument}'.");
            }

            positionals.Enqueue(argument);
        }

        if (manifestPath is null && positionals.Count > 0)
        {
            manifestPath = positionals.Dequeue();
        }

        if (outputDirectory is null && positionals.Count > 0)
        {
            outputDirectory = positionals.Dequeue();
        }

        while (positionals.Count > 0)
        {
            modelPaths.Add(positionals.Dequeue());
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("A manifest path is required.");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.");
        }

        var normalizedModelPaths = modelPaths
            .Where(static modelPath => !string.IsNullOrWhiteSpace(modelPath))
            .Distinct(FileSystemPathComparer.Instance)
            .ToArray();
        if (normalizedModelPaths.Length == 0)
        {
            throw new ArgumentException("At least one model path is required.");
        }
        if (modelSourceUris.Count != normalizedModelPaths.Length
            || modelSourceUris.Any(static source =>
                !Uri.TryCreate(source, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            throw new ArgumentException(
                "Each model requires one absolute HTTP(S) --model-source URL in the same order.");
        }
        if (modelThresholds.Count != normalizedModelPaths.Length)
        {
            throw new ArgumentException(
                "Each model requires one frozen --model-threshold value in the same order.");
        }
        if (modelThresholdManifestPaths.Count != normalizedModelPaths.Length)
        {
            throw new ArgumentException(
                "Each model requires one --model-threshold-manifest path in the same order.");
        }

        return new VoiceBenchmarkCommandLineOptions
        {
            ManifestPath = manifestPath,
            OutputDirectory = outputDirectory,
            ModelPaths = normalizedModelPaths,
            ModelSourceUris = modelSourceUris.ToArray(),
            ModelThresholds = modelThresholds.ToArray(),
            ModelThresholdManifestPaths = modelThresholdManifestPaths.ToArray(),
        };
    }
}
