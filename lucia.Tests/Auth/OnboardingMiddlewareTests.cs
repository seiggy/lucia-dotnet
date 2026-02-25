using FakeItEasy;
using lucia.AgentHost.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Auth;

public class OnboardingMiddlewareTests
{
    private readonly ILogger<OnboardingMiddleware> _logger = A.Fake<ILogger<OnboardingMiddleware>>();

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/setup")]
    [InlineData("/setup/step-2")]
    [InlineData("/api/auth/status")]
    [InlineData("/_framework/blazor.js")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    [InlineData("/agents/register")]
    [InlineData("/agents/lucia")]
    public async Task ExemptPaths_PassThroughWithoutRedirect(string path)
    {
        var nextCalled = false;
        var next = CreateNext(() => nextCalled = true);
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/styles.css")]
    [InlineData("/app.js")]
    [InlineData("/logo.png")]
    [InlineData("/favicon.ico")]
    [InlineData("/font.woff2")]
    public async Task StaticFileExtensions_PassThrough(string path)
    {
        var nextCalled = false;
        var next = CreateNext(() => nextCalled = true);
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/agents/list")]
    public async Task ApiPaths_Get503WhenSetupIncomplete(string path)
    {
        var next = CreateNext();
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: false);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/setup/status")]
    [InlineData("/api/setup/generate-dashboard-key")]
    [InlineData("/api/setup/configure-ha")]
    [InlineData("/api/setup/complete")]
    public async Task SetupPaths_PassThroughWhenSetupIncomplete(string path)
    {
        var nextCalled = false;
        var next = CreateNext(() => nextCalled = true);
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/setup/status")]
    [InlineData("/api/setup/generate-dashboard-key")]
    [InlineData("/api/setup/regenerate-dashboard-key")]
    [InlineData("/api/setup/configure-ha")]
    [InlineData("/api/setup/test-ha-connection")]
    [InlineData("/api/setup/generate-ha-key")]
    [InlineData("/api/setup/validate-ha-connection")]
    [InlineData("/api/setup/ha-status")]
    [InlineData("/api/setup/complete")]
    public async Task SetupPaths_Get403WhenSetupComplete(string path)
    {
        var nextCalled = false;
        var next = CreateNext(() => nextCalled = true);
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/")]
    [InlineData("/settings")]
    public async Task BrowserPaths_GetRedirectToSetup(string path)
    {
        var next = CreateNext();
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: false);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/setup", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/api/agents/list")]
    [InlineData("/dashboard")]
    [InlineData("/settings")]
    public async Task AllPaths_PassThroughWhenSetupComplete(string path)
    {
        var nextCalled = false;
        var next = CreateNext(() => nextCalled = true);
        var middleware = new OnboardingMiddleware(next, _logger);
        var context = CreateContext(path, setupComplete: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static RequestDelegate CreateNext(Action? onCalled = null)
    {
        return _ =>
        {
            onCalled?.Invoke();
            return Task.CompletedTask;
        };
    }

    private static DefaultHttpContext CreateContext(string path, bool setupComplete)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SetupComplete"] = setupComplete ? "true" : "false",
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Request.Path = path;

        return context;
    }
}
