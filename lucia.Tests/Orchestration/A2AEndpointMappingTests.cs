using A2A;
using lucia.Agents.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace lucia.Tests.Orchestration;

public sealed class A2AEndpointMappingTests
{
    [Fact]
    public async Task MapA2ALazy_ServesTheAgentCardWithTheRestRoutes()
    {
        await using var app = WebApplication.CreateSlimBuilder().Build();
        var card = new AgentCard { Name = "music-agent", Description = "Plays music" };

        app.MapA2ALazy(() => throw new InvalidOperationException("Not invoked."), "/music", card)
            .WithTags("caller-convention");

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var cardEndpoint = Assert.Single(endpoints,
            endpoint => endpoint.RoutePattern.RawText?.EndsWith(".well-known/agent-card.json") == true);
        var sendEndpoint = Assert.Single(endpoints,
            endpoint => endpoint.RoutePattern.RawText?.EndsWith("message:send") == true);
        Assert.StartsWith("/music", cardEndpoint.RoutePattern.RawText);
        Assert.Contains("caller-convention", cardEndpoint.Metadata.GetMetadata<ITagsMetadata>()!.Tags);
        Assert.Contains("caller-convention", sendEndpoint.Metadata.GetMetadata<ITagsMetadata>()!.Tags);
    }
}
