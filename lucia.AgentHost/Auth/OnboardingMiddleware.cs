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
    /// Setup endpoints (/api/setup) are NOT in this list — they are
    /// handled separately to enforce auth and block after completion.
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    [
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

        // Always allow exempt paths and static files (health, auth, etc.)
        if (IsExemptPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Determine setup state
        var setupComplete = false;
        try
        {
            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            setupComplete = string.Equals(
                configuration["Auth:SetupComplete"], "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // MongoDB may be unreachable on first boot — show error, don't crash
            _logger.LogWarning(ex, "Failed to check setup status — config store may be unavailable");
        }

        // Setup endpoints: permanently disabled after setup completes,
        // allowed during setup (individual endpoints enforce their own auth)
        if (path.StartsWith("/api/setup", StringComparison.OrdinalIgnoreCase))
        {
            if (setupComplete)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "setup_complete",
                    message = "Setup has already been completed. Setup endpoints are permanently disabled.",
                }).ConfigureAwait(false);
                return;
            }

            // Setup not yet complete — pass through to endpoint handlers
            // (individual endpoints enforce AllowAnonymous or RequireAuthorization)
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Setup complete — let auth middleware handle the rest
        if (setupComplete)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Setup NOT complete — block non-setup requests
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
