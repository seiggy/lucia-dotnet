using System.Reflection;
using lucia.AgentHost.Apis;

namespace lucia.Tests.Apis;

public sealed class ConfigurationApiSensitiveKeyTests
{
    [Fact]
    public void IsSensitiveKey_OtlpHeaders_IsRedacted()
    {
        var method = typeof(ConfigurationApi).GetMethod(
            "IsSensitiveKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method.Invoke(null, ["OtlpHeaders"])!);
    }
}
