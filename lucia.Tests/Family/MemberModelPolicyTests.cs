using lucia.Agents.Family;

namespace lucia.Tests.Family;

public sealed class MemberModelPolicyTests
{
    [Theory]
    [InlineData(new string[0], null, "cloud", null)]
    [InlineData(new string[0], "local", "cloud", "local")]
    [InlineData(new string[0], "local", "local", null)]
    [InlineData(new[] { "local" }, null, "cloud", "local")]
    [InlineData(new[] { "local", "cloud" }, null, "cloud", null)]
    [InlineData(new[] { "local", "other" }, "other", "cloud", "other")]
    public void SelectProvider_RoutesOnlyWhenNeeded(string[] allowed, string? preferred, string current, string? expected)
    {
        var controls = new MemberLlmControls { AllowedProviderIds = [.. allowed], PreferredProviderId = preferred };

        Assert.Equal(expected, MemberModelPolicy.SelectProvider(controls, current));
    }

    [Fact]
    public void RequiresManagedModels_WhenAnyModelOrBudgetControlIsSet()
    {
        Assert.False(MemberModelPolicy.RequiresManagedModels(new MemberLlmControls { SystemPrompt = "be kind" }));
        Assert.True(MemberModelPolicy.RequiresManagedModels(new MemberLlmControls { AllowedProviderIds = ["local"] }));
        Assert.True(MemberModelPolicy.RequiresManagedModels(new MemberLlmControls { PreferredProviderId = "local" }));
        Assert.True(MemberModelPolicy.RequiresManagedModels(new MemberLlmControls { DailyBudgetUsd = 1 }));
        Assert.True(MemberModelPolicy.RequiresManagedModels(new MemberLlmControls { MonthlyBudgetUsd = 1 }));
    }
}
