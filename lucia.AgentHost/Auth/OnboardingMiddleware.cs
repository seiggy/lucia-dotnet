namespace lucia.AgentHost.Auth;

/// <summary>
/// Middleware that detects first-run (no Auth:SetupComplete in config store)
/// and redirects all non-setup requests to the onboarding wizard.
/// </summary>
public sealed class OnboardingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OnboardingMiddleware> _logger;

    /// <summary>
    /// Paths that are always accessible regardless of setup state.
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    [
        "/api/setup",
        "/api/auth/status",
        "/api/auth/login",
        "/agents",
        "/setup",
        "/health",
        "/alive",
        "/_framework",
        "/openapi",
        "/scalar",
    ];

    /// <summary>
    /// Static file extensions that are always accessible.
    /// </summary>
    private static readonly string[] StaticExtensions =
    [
        ".js", ".css", ".html", ".png", ".jpg", ".svg", ".ico", ".woff", ".woff2", ".map", ".json",
    ];

    public OnboardingMiddleware(RequestDelegate next, ILogger<OnboardingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // Always allow exempt paths and static files
        if (IsExemptPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Check if setup is complete
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

        try
        {
            var setupComplete = configuration["Auth:SetupComplete"];
            if (string.Equals(setupComplete, "true", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            // MongoDB may be unreachable on first boot — show error, don't crash
            _logger.LogWarning(ex, "Failed to check setup status — config store may be unavailable");
        }

        // Setup not complete — redirect API requests with 503, browser requests to /setup
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/agents/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "setup_required",
                message = "Lucia has not been configured yet. Please complete the setup wizard.",
                setupUrl = "/setup",
            }).ConfigureAwait(false);
            return;
        }

        // Browser request — redirect to setup
        context.Response.Redirect("/setup");
    }

    private static bool IsExemptPath(string path)
    {
        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Static file extensions
        foreach (var ext in StaticExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
