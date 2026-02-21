using lucia.Agents.Agents;

namespace lucia.Tests.Integration;

public sealed class ExtractDeviceIdTests
{
    [Fact]
    public void ExtractDeviceId_WithValidContext_ReturnsDeviceId()
    {
        var message = """
            HOME ASSISTANT CONTEXT:

            REQUEST_CONTEXT:
            {
              "timestamp": "2026-02-19 21:59:49",
              "day_of_week": "Thursday",
              "location": "Home",
              "device": {
                "id": "conversation.lucia",
                "area": "Living Room",
                "type": "conversation"
              }
            }

            turn on the lights
            """;

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Equal("conversation.lucia", result);
    }

    [Fact]
    public void ExtractDeviceId_WithDifferentDevice_ReturnsCorrectId()
    {
        var message = """
            REQUEST_CONTEXT:
            {"device":{"id":"assist_satellite.kitchen","area":"Kitchen","type":"satellite"}}

            play some music
            """;

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Equal("assist_satellite.kitchen", result);
    }

    [Fact]
    public void ExtractDeviceId_WithNoContext_ReturnsNull()
    {
        var message = "turn on the lights in the kitchen";

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDeviceId_WithNullMessage_ReturnsNull()
    {
        var result = OrchestratorAgent.ExtractDeviceId(null);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDeviceId_WithEmptyMessage_ReturnsNull()
    {
        var result = OrchestratorAgent.ExtractDeviceId("");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDeviceId_WithMalformedJson_ReturnsNull()
    {
        var message = """
            REQUEST_CONTEXT:
            { this is not valid json }

            do something
            """;

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDeviceId_WithNoDeviceProperty_ReturnsNull()
    {
        var message = """
            REQUEST_CONTEXT:
            {"timestamp": "2026-02-19", "location": "Home"}

            do something
            """;

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDeviceId_WithEmptyDeviceId_ReturnsNull()
    {
        var message = """
            REQUEST_CONTEXT:
            {"device": {"id": "", "area": "Kitchen"}}

            do something
            """;

        var result = OrchestratorAgent.ExtractDeviceId(message);

        Assert.Null(result);
    }
}
