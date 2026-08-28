namespace lucia.VoiceBenchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        const string usage = "Usage: dotnet run --project lucia.VoiceBenchmarks -- speaker --manifest benchmarks/voice/sample-manifest.json --model path/to/a.onnx --model path/to/b.onnx --output benchmarks/results\n       dotnet run --project lucia.VoiceBenchmarks -- speaker benchmarks/voice/sample-manifest.json benchmarks/results path/to/a.onnx path/to/b.onnx";

        try
        {
            if (args.Length == 0 ||
                args.Any(static argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(usage);
                return 0;
            }

            var command = args[0];
            if (!string.Equals(command, "speaker", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Unknown command '{command}'.");
                Console.Error.WriteLine(usage);
                return 1;
            }

            var options = VoiceBenchmarkCommandLineOptions.Parse(args);
            var commandLine = string.Join(" ", args.Select(static value => QuoteIfNeeded(value)));
            var runner = new SpeakerBenchmarkRunner(options.ManifestPath, options.ModelPaths, options.OutputDirectory, commandLine);
            var report = runner.Run();
            runner.WriteReports(report);

            var jsonPath = Path.Combine(options.OutputDirectory, "voice-benchmark-report.json");
            var markdownPath = Path.Combine(options.OutputDirectory, "voice-benchmark-report.md");
            Console.WriteLine($"Benchmark report written to '{jsonPath}' and '{markdownPath}'.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
