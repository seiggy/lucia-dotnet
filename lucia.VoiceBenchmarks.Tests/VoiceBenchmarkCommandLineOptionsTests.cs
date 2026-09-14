namespace lucia.VoiceBenchmarks.Tests;

public sealed class VoiceBenchmarkCommandLineOptionsTests
{
    [Fact]
    public void Parse_RejectsWhitespaceOnlyModelPath()
    {
        var args = new[]
        {
            "speaker",
            "--manifest",
            "manifest.json",
            "--output",
            "results",
            "--model",
            " ",
            "--model-source",
            "https://example.com/model.onnx",
            "--model-threshold",
            "0.7",
            "--model-threshold-manifest",
            "development-manifest.json",
        };

        Assert.Throws<ArgumentException>(() => VoiceBenchmarkCommandLineOptions.Parse(args));
    }

    [Fact]
    public void Parse_DistinguishesManifestAndModelShortOptions()
    {
        var options = VoiceBenchmarkCommandLineOptions.Parse(
        [
            "speaker",
            "-m", "manifest.json",
            "-o", "results",
            "-M", "model.onnx",
            "--model-source", "https://example.com/model.onnx",
            "--model-threshold", "0.7",
            "--model-threshold-manifest", "development-manifest.json",
        ]);

        Assert.Equal("manifest.json", options.ManifestPath);
        Assert.Equal(["model.onnx"], options.ModelPaths);
    }

    [Fact]
    public void Parse_RejectsNonFiniteModelThreshold()
    {
        var args = new[]
        {
            "speaker",
            "--manifest", "manifest.json",
            "--output", "results",
            "--model", "model.onnx",
            "--model-source", "https://example.com/model.onnx",
            "--model-threshold", "NaN",
            "--model-threshold-manifest", "development-manifest.json",
        };

        Assert.Throws<ArgumentException>(() => VoiceBenchmarkCommandLineOptions.Parse(args));
    }

    [Fact]
    public void Parse_AllowsSpeakerAsPositionalModelPath()
    {
        var options = VoiceBenchmarkCommandLineOptions.Parse(
        [
            "manifest.json",
            "results",
            "speaker",
            "--model-source", "https://example.com/model.onnx",
            "--model-threshold", "0.7",
            "--model-threshold-manifest", "development-manifest.json",
        ]);

        Assert.Equal(["speaker"], options.ModelPaths);
    }
}
