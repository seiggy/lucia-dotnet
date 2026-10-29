using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Services;

public sealed class ModelTelemetryConfigurationTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void RealRegistration_BindsContentPolicyAndDefaultsOn(string? configured, bool expected)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        if (configured is not null)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ModelTelemetry:IncludeModelContent"] = configured });
        builder.AddLuciaAgents();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Equal(expected, services.GetRequiredService<IOptionsMonitor<ModelTelemetryOptions>>().CurrentValue.IncludeModelContent);
    }

    [Theory]
    [InlineData(null, 4096)]
    [InlineData("", 4096)]
    [InlineData("256", 256)]
    [InlineData("8192", 8192)]
    public void RealRegistration_BindsPersonalityBudgetOrUsesFallback(string? configured, int expected)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        if (configured is not null)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PersonalityPrompt:MaxOutputTokens"] = configured });
        builder.AddLuciaAgents();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Equal(expected, services.GetRequiredService<IOptionsMonitor<PersonalityPromptOptions>>().CurrentValue.EffectiveMaxOutputTokens);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void RealRegistration_RejectsNonPositivePersonalityBudget(string configured)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PersonalityPrompt:MaxOutputTokens"] = configured });
        builder.AddLuciaAgents();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptionsMonitor<PersonalityPromptOptions>>().CurrentValue);
    }
}
