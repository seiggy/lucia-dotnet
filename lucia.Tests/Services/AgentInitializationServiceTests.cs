using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using lucia.HomeAssistant.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Services;

public sealed class AgentInitializationServiceTests
{
    [Fact]
    public async Task StartAsync_SeedsDefinitionsBeforeHomeAssistantIsConfigured()
    {
        var seedObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var definitionRepository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => definitionRepository.GetAllAgentDefinitionsAsync(
                A<CancellationToken>._))
            .Invokes(() => seedObserved.TrySetResult())
            .Returns([]);

        var apiKeyService = A.Fake<IApiKeyService>();
        A.CallTo(() => apiKeyService.ListKeysAsync(A<CancellationToken>._))
            .Returns([]);
        var configStore = A.Fake<IConfigStoreWriter>();
        A.CallTo(() => configStore.GetAsync(
                A<string>._,
                A<CancellationToken>._))
            .Returns((string?)null);
        var options = A.Fake<IOptionsMonitor<HomeAssistantOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new HomeAssistantOptions());
        var logger = A.Fake<ILogger<AgentInitializationService>>();
        var service = new AgentInitializationService(
            A.Fake<IAgentRegistry>(),
            [],
            [],
            A.Fake<IServiceProvider>(),
            definitionRepository,
            A.Fake<IModelProviderRepository>(),
            A.Fake<IEntityLocationService>(),
            A.Fake<IPresenceDetectionService>(),
            apiKeyService,
            configStore,
            new ConfigurationBuilder().Build(),
            logger,
            options,
            new AgentInitializationStatus());

        await service.StartAsync(CancellationToken.None);
        try
        {
            await seedObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
