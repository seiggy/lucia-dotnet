using System.Text.Json;
using lucia.Agents.Skills;

namespace lucia.Tests.Skills;

public sealed class AttributeParserTests
{
    private static Dictionary<string, object> With(object value) => new() { ["modes"] = value };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParseStringList_StringArray_ReturnsTheStrings()
        => Assert.Equal(["auto", "low"], AttributeParser.ParseStringList(With(Json("[\"auto\",\"low\"]")), "modes"));

    [Fact]
    public void ParseStringList_NumericArray_KeepsTheNumbersAsText()
        => Assert.Equal(["4", "1", "2", "3"], AttributeParser.ParseStringList(With(Json("[4,1,2,3]")), "modes"));

    [Fact]
    public void ParseStringList_MixedArray_ReturnsEveryItemAsText()
        => Assert.Equal(["auto", "2", "true"], AttributeParser.ParseStringList(With(Json("[\"auto\",2,true]")), "modes"));

    [Fact]
    public void ParseStringList_NullItem_IsKeptAsText()
        => Assert.Equal(["auto", "null"], AttributeParser.ParseStringList(With(Json("[\"auto\",null]")), "modes"));

    [Fact]
    public void ParseStringList_MissingKey_ReturnsEmpty()
        => Assert.Empty(AttributeParser.ParseStringList(new Dictionary<string, object>(), "modes"));

    [Theory]
    [InlineData("\"auto\"")]
    [InlineData("{\"a\":1}")]
    [InlineData("null")]
    [InlineData("5")]
    public void ParseStringList_NotAnArray_ReturnsEmpty(string json)
        => Assert.Empty(AttributeParser.ParseStringList(With(Json(json)), "modes"));

    [Fact]
    public void ParseStringList_StringHoldingAJsonArray_IsParsed()
        => Assert.Equal(["a", "1"], AttributeParser.ParseStringList(With("[\"a\",1]"), "modes"));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void ParseStringList_StringThatIsNotAnArray_ReturnsEmpty(string text)
        => Assert.Empty(AttributeParser.ParseStringList(With(text), "modes"));

    [Fact]
    public void ParseStringList_ArbitraryObject_ReturnsEmpty()
        => Assert.Empty(AttributeParser.ParseStringList(With(new List<string> { "a" }), "modes"));
}
