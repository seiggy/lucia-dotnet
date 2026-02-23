using System.Text.Encodings.Web;
using lucia.AgentHost.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Auth;

public sealed class InternalTokenAuthenticationHandlerTests
{
    private const string ValidToken = "test-internal-secret-token-12345";

    private static InternalTokenAuthenticationHandler CreateHandler(
        string configuredToken,
        string? authorizationHeader)
    {
        var tokenOptions = Options.Create(new InternalTokenOptions { Token = configuredToken });
        var tokenMonitor = new OptionsMonitorStub<InternalTokenOptions>(tokenOptions.Value);
        var schemeOptions = new OptionsMonitorStub<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions());
        var loggerFactory = NullLoggerFactory.Instance;
        var encoder = UrlEncoder.Default;

        var handler = new InternalTokenAuthenticationHandler(
            tokenMonitor, schemeOptions, loggerFactory, encoder);

        // Create a fake HttpContext with the authorization header
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var scheme = new AuthenticationScheme(
            InternalTokenDefaults.AuthenticationScheme,
            InternalTokenDefaults.AuthenticationScheme,
            typeof(InternalTokenAuthenticationHandler));

        handler.InitializeAsync(scheme, httpContext).GetAwaiter().GetResult();

        return handler;
    }

    [Fact]
    public async Task ValidBearerToken_ReturnsSuccess()
    {
        var handler = CreateHandler(ValidToken, $"Bearer {ValidToken}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);
        Assert.Equal("internal-service",
            result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("internal_token",
            result.Principal.FindFirst("auth_method")?.Value);
    }

    [Fact]
    public async Task InvalidBearerToken_ReturnsFail()
    {
        var handler = CreateHandler(ValidToken, "Bearer wrong-token");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("Invalid internal token", result.Failure?.Message);
    }

    [Fact]
    public async Task NoBearerHeader_ReturnsNoResult()
    {
        var handler = CreateHandler(ValidToken, null);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure); // NoResult, not Fail
    }

    [Fact]
    public async Task ApiKeyHeader_ReturnsNoResult()
    {
        // Non-Bearer auth headers should be ignored (handled by ApiKey handler)
        var handler = CreateHandler(ValidToken, "ApiKey some-api-key");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure); // NoResult
    }

    [Fact]
    public async Task EmptyConfiguredToken_ReturnsNoResult()
    {
        var handler = CreateHandler("", $"Bearer {ValidToken}");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure); // NoResult when no token configured
    }

    /// <summary>
    /// Simple IOptionsMonitor stub that returns a fixed value.
    /// </summary>
    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
