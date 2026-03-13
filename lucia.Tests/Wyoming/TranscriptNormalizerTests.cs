using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

public sealed class TranscriptNormalizerTests
{
    [Theory]
    [InlineData("Turn on the lights", "turn on the lights")]
    [InlineData("TURN OFF THE LIGHTS", "turn off the lights")]
    public void Normalize_LowercasesInput(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("um turn on the lights please", "turn on the lights")]
    [InlineData("uh like set the thermostat", "set the thermostat")]
    [InlineData("okay actually turn off lights", "turn off lights")]
    public void Normalize_RemovesFillerAndPoliteWords(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_CollapsesWhitespace()
    {
        Assert.Equal("turn on lights", TranscriptNormalizer.Normalize("turn   on   lights"));
    }

    [Fact]
    public void Normalize_RemovesPunctuation()
    {
        Assert.Equal("turn on the lights", TranscriptNormalizer.Normalize("Turn on the lights!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Normalize_EmptyOrNull_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, TranscriptNormalizer.Normalize(input!));
    }

    [Fact]
    public void Normalize_OnlyFillerWords_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TranscriptNormalizer.Normalize("um uh like okay please"));
    }

    [Fact]
    public void Tokenize_SplitsOnSpaces()
    {
        var tokens = TranscriptNormalizer.Tokenize("turn on the lights");

        Assert.Equal(["turn", "on", "the", "lights"], tokens);
    }

    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(TranscriptNormalizer.Tokenize(""));
    }

    [Fact]
    public void Normalize_PreservesNumbers()
    {
        Assert.Equal(
            "set thermostat to 72 degrees",
            TranscriptNormalizer.Normalize("set thermostat to 72 degrees"));
    }
}
