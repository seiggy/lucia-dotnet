using lucia.Agents.Abstractions;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;

namespace lucia.Tests.Services.EntityLocation;

public sealed class InProcessEntityLocationServiceTests : EntityLocationServiceContract
{
    protected override Task<StoreScope<IEntityLocationService>> CreateAsync(SampleHomeAssistant home)
    {
        var service = InProcessEntityLocation.Create(home.Client, EmbeddingResolver(), Matcher());
        return Task.FromResult(new StoreScope<IEntityLocationService>(service, () => ValueTask.CompletedTask));
    }
}