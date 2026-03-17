using System.Reflection;
using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Wyoming;

public sealed class SkillDispatcherTests
{
    [Fact]
    public void FormatFastPathCommand_BrightnessWithArea_UsesAreaLightsTarget()
    {
        var route = CreateRoute(
            action: "brightness",
            captures: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["area"] = "kitchen",
                ["value"] = "35",
            });

        var formatted = FormatFastPathCommand(route);

        Assert.Equal("Set the kitchen lights brightness to 35 percent", formatted);
    }

    [Fact]
    public void FormatFastPathCommand_SetTemperatureWithArea_AddsAreaAndDegrees()
    {
        var route = CreateRoute(
            action: "set_temperature",
            captures: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["area"] = "bedroom",
                ["value"] = "68",
            });

        var formatted = FormatFastPathCommand(route);

        Assert.Equal("Set the thermostat in the bedroom to 68 degrees", formatted);
    }

    [Fact]
    public void FormatFastPathCommand_AdjustWithArea_FormatsComfortAdjustment()
    {
        var route = CreateRoute(
            action: "adjust",
            captures: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "warmer",
                ["area"] = "office",
            });

        var formatted = FormatFastPathCommand(route);

        Assert.Equal("Make it warmer in the office", formatted);
    }

    private static CommandRouteResult CreateRoute(string action, IReadOnlyDictionary<string, string> captures) => new()
    {
        IsMatch = true,
        Confidence = 0.9f,
        MatchDuration = TimeSpan.FromMilliseconds(1),
        CapturedValues = captures,
        MatchedPattern = new CommandPattern
        {
            Id = $"test-{action}",
            SkillId = "TestSkill",
            Action = action,
            Templates = [$"{action} template"],
        },
    };

    private static string FormatFastPathCommand(CommandRouteResult route)
    {
        var method = typeof(SkillDispatcher).GetMethod(
            "FormatFastPathCommand",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [route]));
    }
}
