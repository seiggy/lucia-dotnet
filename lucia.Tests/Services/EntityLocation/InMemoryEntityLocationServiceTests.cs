using lucia.Agents.Abstractions;
using lucia.Data.InMemory;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services.EntityLocation;

public sealed class InMemoryEntityLocationServiceTests : EntityLocationServiceContract
{
    protected override Task<StoreScope<IEntityLocationService>> CreateAsync(SampleHomeAssistant home)
    {
        var service = new InMemoryEntityLocationService(
            home.Client,
            EmbeddingResolver(),
            Matcher(),
            NullLogger<InMemoryEntityLocationService>.Instance);
        return Task.FromResult(new StoreScope<IEntityLocationService>(service, () => ValueTask.CompletedTask));
    }
}