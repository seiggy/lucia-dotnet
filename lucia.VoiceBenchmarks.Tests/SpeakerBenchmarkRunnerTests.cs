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

    [Fact]
    public void Run_RejectsDevelopmentManifestWithEvaluationAudio()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var clipNames = new[] { "a-enroll.wav", "a-test.wav", "b-enroll.wav", "b-test.wav" };
        for (var index = 0; index < clipNames.Length; index++)
        {
            File.WriteAllBytes(Path.Combine(tempDirectory, clipNames[index]), [1, 2, (byte)index]);
        }

        var evaluationManifestPath = Path.Combine(tempDirectory, "evaluation.json");
        var developmentManifestPath = Path.Combine(tempDirectory, "development.json");
        const string Manifest = """
            {
              "clips": [
                { "path": "a-enroll.wav", "speaker_id": "a", "split": "enroll" },
                { "path": "a-test.wav", "speaker_id": "a", "split": "test" },
                { "path": "b-enroll.wav", "speaker_id": "b", "split": "enroll" },
                { "path": "b-test.wav", "speaker_id": "b", "split": "test" }
              ]
            }
            """;
        File.WriteAllText(evaluationManifestPath, Manifest);
        File.WriteAllText(developmentManifestPath, $"{Manifest}{Environment.NewLine}");
        try
        {
            var runner = new SpeakerBenchmarkRunner(
                evaluationManifestPath,
                ["model.onnx"],
                ["https://example.com/model.onnx"],
                [0.7],
                [developmentManifestPath],
                tempDirectory,
                "test");

            var exception = Assert.Throws<InvalidOperationException>(runner.Run);

            Assert.Contains("audio must not overlap", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_RejectsDuplicateAudioWithinDevelopmentManifest()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var evaluationManifestPath = Path.Combine(tempDirectory, "evaluation.json");
        var developmentManifestPath = Path.Combine(tempDirectory, "development.json");
        File.WriteAllText(
            evaluationManifestPath,
            """
            {
              "clips": [
                { "path": "evaluation-a-enroll.wav", "speaker_id": "a", "split": "enroll" },
                { "path": "evaluation-a-test.wav", "speaker_id": "a", "split": "test" },
                { "path": "evaluation-b-enroll.wav", "speaker_id": "b", "split": "enroll" },
                { "path": "evaluation-b-test.wav", "speaker_id": "b", "split": "test" }
              ]
            }
            """);
        File.WriteAllText(
            developmentManifestPath,
            """
            {
              "clips": [
                { "path": "development-a-enroll.wav", "speaker_id": "a", "split": "enroll" },
                { "path": "development-a-test.wav", "speaker_id": "a", "split": "test" },
                { "path": "development-b-enroll.wav", "speaker_id": "b", "split": "enroll" },
                { "path": "development-b-test.wav", "speaker_id": "b", "split": "test" }
              ]
            }
            """);
        var evaluationNames = new[]
        {
            "evaluation-a-enroll.wav",
            "evaluation-a-test.wav",
            "evaluation-b-enroll.wav",
            "evaluation-b-test.wav",
        };
        for (var index = 0; index < evaluationNames.Length; index++)
        {
            File.WriteAllBytes(Path.Combine(tempDirectory, evaluationNames[index]), [1, 2, 3, (byte)index]);
        }
        File.WriteAllBytes(Path.Combine(tempDirectory, "development-a-enroll.wav"), [4, 5, 6]);
        File.WriteAllBytes(Path.Combine(tempDirectory, "development-a-test.wav"), [7, 8, 9]);
        File.WriteAllBytes(Path.Combine(tempDirectory, "development-b-enroll.wav"), [10, 11, 12]);
        File.WriteAllBytes(Path.Combine(tempDirectory, "development-b-test.wav"), [7, 8, 9]);
        try
        {
            var runner = new SpeakerBenchmarkRunner(
                evaluationManifestPath,
                ["model.onnx"],
                ["https://example.com/model.onnx"],
                [0.7],
                [developmentManifestPath],
                tempDirectory,
                "test");

            var exception = Assert.Throws<InvalidOperationException>(runner.Run);

            Assert.Contains("appears more than once", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
