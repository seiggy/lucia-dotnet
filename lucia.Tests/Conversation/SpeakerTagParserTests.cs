using lucia.AgentHost.Conversation;

namespace lucia.Tests.Conversation;

public sealed class SpeakerTagParserTests
{
    [Fact]
    public void Parse_WithSpeakerTag_ExtractsSpeakerAndCleanText()
    {
        var (speaker, text) = SpeakerTagParser.Parse("<Zack />Turn on the office lights");

        Assert.Equal("Zack", speaker);
        Assert.Equal("Turn on the office lights", text);
    }

    [Fact]
    public void Parse_WithNoTag_ReturnsNullSpeakerAndOriginalText()
    {
        var (speaker, text) = SpeakerTagParser.Parse("Turn on the office lights");

        Assert.Null(speaker);
        Assert.Equal("Turn on the office lights", text);
    }

    [Fact]
    public void Parse_WithSpacesInTag_TrimsSpeakerName()
    {
        var (speaker, text) = SpeakerTagParser.Parse("< Sarah />Set the thermostat to 72");

        Assert.Equal("Sarah", speaker);
        Assert.Equal("Set the thermostat to 72", text);
    }

    [Fact]
    public void Parse_WithMultiWordSpeaker_CapturesFullName()
    {
        var (speaker, text) = SpeakerTagParser.Parse("<John Smith />Make it warmer");

        Assert.Equal("John Smith", speaker);
        Assert.Equal("Make it warmer", text);
    }

    [Fact]
    public void Parse_WithEmptyTextAfterTag_ReturnsEmptyCleanText()
    {
        var (speaker, text) = SpeakerTagParser.Parse("<Zack />");

        Assert.Equal("Zack", speaker);
        Assert.Equal("", text);
    }

    [Fact]
    public void Parse_WithTagInMiddle_DoesNotMatch()
    {
        const string input = "Please <Zack />turn on lights";
        var (speaker, text) = SpeakerTagParser.Parse(input);

        Assert.Null(speaker);
        Assert.Equal(input, text);
    }
}
