namespace lucia.VoiceBenchmarks.Tests;

public sealed class SpeakerBenchmarkRunnerTests
{
    [Fact]
    public void Run_RejectsEvaluationManifestAsThresholdDevelopmentManifest()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "clips": [
                { "path": "a-enroll.wav", "speaker_id": "a", "split": "enroll" },
                { "path": "a-test.wav", "speaker_id": "a", "split": "test" },
                { "path": "b-enroll.wav", "speaker_id": "b", "split": "enroll" },
                { "path": "b-test.wav", "speaker_id": "b", "split": "test" }
              ]
            }
            """);
        try
        {
            var runner = new SpeakerBenchmarkRunner(
                manifestPath,
                ["model.onnx"],
                ["https://example.com/model.onnx"],
                [0.7],
                [manifestPath],
                tempDirectory,
                "test");

            var exception = Assert.Throws<InvalidOperationException>(runner.Run);

            Assert.Contains("must differ", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
