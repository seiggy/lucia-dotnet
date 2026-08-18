using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies that <see cref="HarnessConfiguration.Validate"/> rejects non-positive
/// deadline values instead of silently disabling the timeout.
/// </summary>
public sealed class HarnessConfigurationValidationTests
{
    [Fact]
    public void Validate_DefaultConfiguration_Passes()
    {
        var config = new HarnessConfiguration();

        config.Validate();

        Assert.True(config.AgentTimeout > TimeSpan.Zero);
        Assert.True(config.JudgeTimeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-300)]
    public void Validate_NonPositiveAgentTimeout_Throws(int seconds)
    {
        var config = new HarnessConfiguration { AgentTimeoutSeconds = seconds };

        var ex = Assert.Throws<InvalidOperationException>(config.Validate);
        Assert.Contains("AgentTimeoutSeconds", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120)]
    public void Validate_NonPositiveJudgeTimeout_Throws(int seconds)
    {
        var config = new HarnessConfiguration { JudgeTimeoutSeconds = seconds };

        var ex = Assert.Throws<InvalidOperationException>(config.Validate);
        Assert.Contains("JudgeTimeoutSeconds", ex.Message);
    }
}
