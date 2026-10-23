using System.Text;
using System.Text.Json;
using lucia.Agents.Training;

namespace lucia.Tests.Training;

public sealed class ToolResultCapturePolicyTests
{
    [Theory]
    [InlineData("password=TOP_SECRET")]
    [InlineData("{\"nested\":[{\"access_token\":\"TOP_SECRET\",\"ok\":true}]}")]
    [InlineData("{\"Authorization\":{\"nested\":\"TOP_SECRET\"}}")]
    [InlineData("{\"api-key\":\"TOP_SECRET\"}")]
    public void KnownSecrets_AreRedacted(string input)
    {
        var result = new ToolResultCapturePolicy(new()).Sanitize(input, out _);
        Assert.NotNull(result);
        Assert.DoesNotContain("TOP_SECRET", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void CustomPatterns_AreAppliedBeforeTruncation()
    {
        var policy = new ToolResultCapturePolicy(new()
        {
            MaxToolResultBytes = 15,
            RedactionPatterns = ["SECRET-.*-END"]
        });
        Assert.Equal("[REDACTED]", policy.Sanitize("SECRET-" + new string('x', 1000) + "-END", out var truncated));
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(4, "😀")]
    [InlineData(5, "😀")]
    [InlineData(8, "😀😀")]
    public void ByteBound_DoesNotSplitUnicodeScalars(int budget, string expected)
    {
        var result = new ToolResultCapturePolicy(new() { MaxToolResultBytes = budget })
            .Sanitize("😀😀😀", out var truncated);
        Assert.Equal(expected, result);
        Assert.True(truncated);
        Assert.True(Encoding.UTF8.GetByteCount(result!) <= budget);
        Assert.DoesNotContain('\uFFFD', result!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    public void InvalidBudget_FailsClosed(int budget)
    {
        Assert.Null(new ToolResultCapturePolicy(new() { MaxToolResultBytes = budget }).Sanitize("private", out _));
    }

    [Theory]
    [InlineData("[")]
    [InlineData("{invalid-json")]
    [InlineData("{\"PASSWORD\":\"secret\"")]
    [InlineData("{\"password\":\"first\",\"password\":\"second\"}")]
    public void MalformedStructuredInput_IsOmitted(string input)
    {
        Assert.Null(new ToolResultCapturePolicy(new()).Sanitize(input, out _));
    }

    [Fact]
    public void InvalidRedactionPattern_FailsClosed()
    {
        Assert.Null(new ToolResultCapturePolicy(new() { RedactionPatterns = ["["] }).Sanitize("private", out _));
    }

    [Fact]
    public void OversizedInput_IsOmittedRatherThanRedactingOnlyAPrefix()
    {
        Assert.Null(new ToolResultCapturePolicy(new()).Sanitize(new string('x', 65537), out _));
    }

    [Fact]
    public void StructuredSdkResult_IsSanitizedWithoutChangingOriginal()
    {
        using var document = JsonDocument.Parse("{\"token\":\"TOP_SECRET\",\"ok\":true}");
        var result = new ToolResultCapturePolicy(new()).Sanitize(document.RootElement, out _);
        Assert.NotNull(result);
        Assert.DoesNotContain("TOP_SECRET", result);
        Assert.Equal("TOP_SECRET", document.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public void UnsupportedObjects_AreNotStringified()
    {
        Assert.Null(new ToolResultCapturePolicy(new()).Sanitize(new object(), out _));
    }

    [Fact]
    public void EscapedJsonStrings_AreRedactedAfterDecoding()
    {
        var policy = new ToolResultCapturePolicy(new() { RedactionPatterns = ["PRIVATE"] });
        var result = policy.Sanitize("{\"value\":\"PRIV\\u0041TE\"}", out _);
        Assert.NotNull(result);
        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("PRIVATE", result);
    }

    [Fact]
    public void RegexTimeout_SuppressesPreview()
    {
        var policy = new ToolResultCapturePolicy(new() { RedactionPatterns = ["(a+)+$"] });
        Assert.Null(policy.Sanitize(new string('a', 60000) + "!", out _));
    }

    [Fact]
    public void MissingPatternConfiguration_FailsClosed()
    {
        Assert.Null(new ToolResultCapturePolicy(new() { RedactionPatterns = null! }).Sanitize("private", out _));
    }

    [Fact]
    public void DeepJson_IsOmitted()
    {
        var json = new string('[', 40) + "0" + new string(']', 40);
        Assert.Null(new ToolResultCapturePolicy(new()).Sanitize(json, out _));
    }

    [Fact]
    public void InputBound_CountsUtf8BytesNotCharacters()
    {
        Assert.Null(new ToolResultCapturePolicy(new()).Sanitize(new string('界', 22000), out _));
    }
}
