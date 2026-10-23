using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Services;

public sealed class ModelTelemetryPayloadTests
{
    [Theory]
    [InlineData("Production", false, false)]
    [InlineData("Production", false, true)]
    [InlineData("Production", true, false)]
    [InlineData("Production", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Development", false, true)]
    [InlineData("Development", true, false)]
    [InlineData("Development", true, true)]
    public async Task Chat_ContentIsPrivateUnlessExplicitlyEnabled_WhileMetadataRemains(
        string environmentName, bool includeContent, bool streaming)
    {
        var model = "synthetic-" + Guid.NewGuid().ToString("N");
        var input = "PRIVATE_INPUT_" + model;
        var output = "PRIVATE_OUTPUT_" + model;
        var activities = new ConcurrentQueue<Activity>();
        using var listener = Listen(model, activities);
        using var handler = new SyntheticModelTelemetryHandler(model, output);
        using var http = new HttpClient(handler);
        var factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient(A<string>._)).Returns(http);
        var options = A.Fake<IOptionsMonitor<ModelTelemetryOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new ModelTelemetryOptions { IncludeModelContent = includeContent });
        var logger = new RecordingLogger();
        var loggerFactory = A.Fake<ILoggerFactory>();
        A.CallTo(() => loggerFactory.CreateLogger(A<string>._)).Returns(logger);
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns(environmentName);
        using var services = new ServiceCollection().AddSingleton(loggerFactory).AddSingleton(environment).BuildServiceProvider();
        var resolver = new ModelProviderResolver(factory, NullLogger<ModelProviderResolver>.Instance, services, options);
        using var client = resolver.CreateClient(Provider(model));

        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, input)])) { }
        }
        else
        {
            Assert.Equal(output, (await client.GetResponseAsync([new ChatMessage(ChatRole.User, input)])).Text);
        }

        Assert.Equal(1, handler.RequestCount);
        Assert.NotEmpty(activities);
        var telemetry = Payload(activities);
        Assert.Contains(model, telemetry);
        Assert.Equal(includeContent, telemetry.Contains(input, StringComparison.Ordinal));
        Assert.Equal(includeContent, telemetry.Contains(output, StringComparison.Ordinal));
        var logs = string.Join("\n", logger.Entries.Select(entry => entry.Message));
        if (!includeContent)
        {
            Assert.DoesNotContain(input, logs);
            Assert.DoesNotContain(output, logs);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Embeddings_DefaultPolicyRetainsMetadataWithoutRawInput(string environmentName)
    {
        var model = "synthetic-" + Guid.NewGuid().ToString("N");
        var input = "PRIVATE_EMBEDDING_" + model;
        var activities = new ConcurrentQueue<Activity>();
        using var listener = Listen(model, activities);
        using var handler = new SyntheticModelTelemetryHandler(model, "unused");
        using var http = new HttpClient(handler);
        var factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient(A<string>._)).Returns(http);
        var environment = A.Fake<IHostEnvironment>();
        A.CallTo(() => environment.EnvironmentName).Returns(environmentName);
        using var services = new ServiceCollection().AddSingleton(environment).BuildServiceProvider();
        var resolver = new ModelProviderResolver(factory, NullLogger<ModelProviderResolver>.Instance, services);
        using var generator = resolver.CreateEmbeddingGenerator(Provider(model));

        Assert.Single(await generator.GenerateAsync([input]));
        Assert.Equal(1, handler.RequestCount);
        Assert.NotEmpty(activities);
        var telemetry = Payload(activities);
        Assert.Contains(model, telemetry);
        Assert.DoesNotContain(input, telemetry);
    }

    private static ActivityListener Listen(string model, ConcurrentQueue<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "lucia",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("gen_ai.request.model")?.ToString() == model) captured.Enqueue(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string Payload(IEnumerable<Activity> activities) => JsonSerializer.Serialize(activities.Select(activity => new
    {
        tags = activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value),
        events = activity.Events.Select(evt => new { evt.Name, tags = evt.Tags.ToDictionary(tag => tag.Key, tag => tag.Value) })
    }));

    private static ModelProvider Provider(string model) => new()
    {
        Id = model, Name = "Synthetic", ProviderType = ProviderType.Ollama,
        Endpoint = "https://telemetry-test.invalid", ModelName = model
    };
}
