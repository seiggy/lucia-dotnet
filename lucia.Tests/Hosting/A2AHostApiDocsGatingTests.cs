using System.Net;
using lucia.A2AHost.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace lucia.Tests.Hosting;

/// <summary>
/// Boots the actual <c>lucia.A2AHost</c> assembly in-process and verifies that the
/// OpenAPI and Scalar API-documentation routes are mapped only in the Development
/// environment. In Production the routes must be absent (404); this is the security
/// guarantee (production hardening comes from route absence). The tests execute the
/// real top-level Program and fail if the environment guards are removed.
/// </summary>
public sealed class A2AHostApiDocsGatingTests
{
    private const string OpenApiRoute = "/openapi/v1.json";
    private const string ScalarRoute = "/scalar/v1";

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        AllowAutoRedirect = false,
    };

    [Fact]
    public async Task Production_OpenApiRoute_IsAbsent()
    {
        using var factory = new ApiDocsGatingFactory<AgentHostService>("Production");
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.GetAsync(OpenApiRoute);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Production_ScalarRoute_IsAbsent()
    {
        using var factory = new ApiDocsGatingFactory<AgentHostService>("Production");
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.GetAsync(ScalarRoute);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Development_OpenApiRoute_IsMapped()
    {
        using var factory = new ApiDocsGatingFactory<AgentHostService>("Development");
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.GetAsync(OpenApiRoute);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_ScalarRoute_IsMapped()
    {
        using var factory = new ApiDocsGatingFactory<AgentHostService>("Development");
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.GetAsync(ScalarRoute);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
