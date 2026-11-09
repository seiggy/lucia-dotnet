using FakeItEasy;
using lucia.AgentHost.Harness;
using lucia.Agents.Family;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;

namespace lucia.Tests.Family;

public sealed class AssistantHarnessRunnerTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task V2_RunsOnlyWhenTheFlagAndTheMembersOptInAreOn(bool featureOn, bool memberOptIn, bool expected)
    {
        var features = A.Fake<IFeatureManager>();
        A.CallTo(() => features.IsEnabledAsync(AssistantHarnessRunner.FeatureName)).Returns(featureOn);
        var runner = new AssistantHarnessRunner(features, null!, null!, null!, NullLoggerFactory.Instance);
        var member = new FamilyMember { Id = "m", DisplayName = "Alex", Role = FamilyRole.Adult, UseAssistantHarness = memberOptIn };

        Assert.Equal(expected, await runner.IsEnabledForAsync(member));
        Assert.False(await runner.IsEnabledForAsync(null));
    }
}
