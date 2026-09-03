using lucia.AgentHost.Apis;

namespace lucia.Tests.Appliance;

public sealed class ApplianceApiTests
{
    [Fact]
    public void ValidationCredential_RequiresRootIssuedValue()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lucia-validation-{Guid.NewGuid():N}.key");
        var credential = Guid.NewGuid().ToString("D");
        try
        {
            File.WriteAllText(path, credential);

            Assert.False(
                ApplianceApi.IsValidValidationCredential(
                    Guid.NewGuid().ToString("D"),
                    path));
            Assert.True(
                ApplianceApi.IsValidValidationCredential(credential, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("queued", true)]
    [InlineData("running", true)]
    [InlineData("succeeded", false)]
    [InlineData("failed", false)]
    [InlineData("idle", false)]
    public void IsUpdateInProgress_RecognizesActiveStates(
        string status,
        bool expected)
    {
        Assert.Equal(expected, ApplianceApi.IsUpdateInProgress(status));
    }
}
