using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Tests;

public sealed class JudgeClientFactoryTests
{
    [Fact]
    public void Create_WhollyAbsentConfiguration_ReturnsNull()
    {
        Assert.Null(JudgeClientFactory.Create(new AzureOpenAIJudgeSettings()));
    }

    [Fact]
    public void Create_ApiKeyWithoutEndpoint_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings { ApiKey = "configured" };

        var exception = Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));

        Assert.Contains("Endpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_MalformedEndpoint_FailsClearly()
    {
        var settings = new AzureOpenAIJudgeSettings { Endpoint = "not a URI" };

        Assert.Throws<InvalidOperationException>(() => JudgeClientFactory.Create(settings));
    }
}
