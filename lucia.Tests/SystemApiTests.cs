using System.Reflection;
using System.Text.Json;
using lucia.AgentHost.Apis;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests;

public sealed class SystemApiTests
{
    [Theory]
    [InlineData("1.5.1", "1.5.1")]
    [InlineData("1.5.1-voice", "1.5.1-voice")]
    [InlineData("0.0.0-preview.abc1234", "0.0.0-preview.abc1234")]
    [InlineData(null, "development")]
    public void Version_ReportsBuildMetadataWithoutInventingARelease(string? configured, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LUCIA_VERSION"] = configured }).Build();
        var handler = typeof(SystemApi).GetMethod("GetVersion", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(handler);

        var result = Assert.IsType<Ok<object>>(handler.Invoke(null, [configuration]));
        var json = JsonSerializer.SerializeToElement(result.Value);

        Assert.Equal(expected, json.GetProperty("Version").GetString());
    }
}
