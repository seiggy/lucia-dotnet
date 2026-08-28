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
}
