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
        ]);

        Assert.Equal("manifest.json", options.ManifestPath);
        Assert.Equal(["model.onnx"], options.ModelPaths);
    }
}
