using lucia.InstallerHost;

var builder = WebApplication.CreateSlimBuilder(args);

var applianceMode = builder.Configuration["Appliance:Mode"] ?? "Off";
if (!string.Equals(applianceMode, "Installer", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "lucia.InstallerHost requires Appliance:Mode=Installer.");
}

var controlPath = builder.Configuration["Appliance:ControlPath"]
    ?? "/usr/bin/sudo";
var configuredControlCommand =
    builder.Configuration["Appliance:ControlCommand"];
var controlCommand = configuredControlCommand is null
    ? "/usr/libexec/lucia/lucia-installer-control"
    : string.IsNullOrWhiteSpace(configuredControlCommand)
        ? null
        : configuredControlCommand;
var claimPath = builder.Configuration["Appliance:ClaimPath"]
    ?? "/run/lucia-installer/claim.sha256";
var installerOrigin = new Uri("http://lucia.setup");
builder.Services.AddSingleton(
    serviceProvider => new InstallerControlClient(
        controlPath,
        controlCommand,
        serviceProvider.GetRequiredService<ILogger<InstallerControlClient>>()));
builder.Services.AddSingleton(new InstallerClaimStore(claimPath));

var app = builder.Build();

app.Use(async (context, next) =>
{
    var isInstallerApi =
        context.Request.Path.StartsWithSegments("/api/installer");
    if (!string.Equals(
            context.Request.Host.Host,
            installerOrigin.Host,
            StringComparison.OrdinalIgnoreCase))
    {
        if (isInstallerApi)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        else
        {
            context.Response.Redirect(
                new Uri(installerOrigin, "/install").AbsoluteUri);
        }
        return;
    }
    if (isInstallerApi
        && !HttpMethods.IsGet(context.Request.Method)
        && !HasCanonicalOrigin(context.Request, installerOrigin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    if (isInstallerApi
        && context.Request.Path != "/api/installer/capabilities"
        && context.Request.Path != "/api/installer/claim")
    {
        var claimStore = context.RequestServices
            .GetRequiredService<InstallerClaimStore>();
        if (!context.Request.Cookies.TryGetValue(
                InstallerClaimStore.CookieName,
                out var token)
            || !claimStore.IsValid(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next(context).ConfigureAwait(false);
});

app.Use(async (context, next) =>
{
    try
    {
        await next(context).ConfigureAwait(false);
    }
    catch (InstallerControlException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
                new { Error = exception.Message },
                context.RequestAborted)
            .ConfigureAwait(false);
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet(
    "/api/installer/capabilities",
    (InstallerClaimStore claimStore) => Results.Ok(new
    {
        Mode = "installer",
        RequiresSetupCode = false,
        IsClaimed = claimStore.IsClaimed,
    }));
app.MapPost(
    "/api/installer/claim",
    (HttpContext context, InstallerClaimStore claimStore) =>
    {
        if (context.Request.Cookies.TryGetValue(
                InstallerClaimStore.CookieName,
                out var existingToken)
            && claimStore.IsValid(existingToken))
        {
            return Results.Ok(new { Claimed = true });
        }

        var token = claimStore.TryClaim();
        if (token is null)
        {
            return Results.Conflict(new
            {
                Error = "This Lucia is already being set up in another browser.",
            });
        }

        context.Response.Cookies.Append(
            InstallerClaimStore.CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = false,
            });
        return Results.Ok(new { Claimed = true });
    });
app.MapGet(
    "/api/installer/status",
    async (
        InstallerControlClient control,
        CancellationToken cancellationToken) =>
        Results.Ok(await control.GetStatusAsync(cancellationToken)
            .ConfigureAwait(false)));
app.MapGet(
    "/api/installer/disks",
    async (
        InstallerControlClient control,
        CancellationToken cancellationToken) =>
        Results.Ok(await control.GetDisksAsync(cancellationToken)
            .ConfigureAwait(false)));
app.MapGet(
    "/api/installer/networks",
    async (
        InstallerControlClient control,
        CancellationToken cancellationToken) =>
        Results.Ok(await control.GetNetworksAsync(cancellationToken)
            .ConfigureAwait(false)));
app.MapPost(
    "/api/installer/install",
    async (
        InstallerConfigurationRequest request,
        InstallerControlClient control,
        CancellationToken cancellationToken) =>
        Results.Accepted(
            value: await control.StartInstallationAsync(request, cancellationToken)
                .ConfigureAwait(false)));
app.MapPost(
    "/api/installer/retry-network",
    async (
        WifiConfigurationRequest request,
        InstallerControlClient control,
        CancellationToken cancellationToken) =>
        Results.Accepted(
            value: await control.RetryNetworkAsync(request, cancellationToken)
                .ConfigureAwait(false)));
app.MapGet(
    "/",
    () => Results.Redirect(new Uri(installerOrigin, "/install").AbsoluteUri));
foreach (var captivePath in new[]
{
    "/connecttest.txt",
    "/generate_204",
    "/gen_204",
    "/hotspot-detect.html",
    "/library/test/success.html",
    "/ncsi.txt",
})
{
    app.MapGet(
        captivePath,
        () => Results.Redirect(
            new Uri(installerOrigin, "/install").AbsoluteUri));
}
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

static bool HasCanonicalOrigin(HttpRequest request, Uri installerOrigin)
{
    return Uri.TryCreate(
            request.Headers.Origin.ToString(),
            UriKind.Absolute,
            out var requestOrigin)
        && string.Equals(
            requestOrigin.Scheme,
            installerOrigin.Scheme,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            requestOrigin.Host,
            installerOrigin.Host,
            StringComparison.OrdinalIgnoreCase)
        && requestOrigin.Port == installerOrigin.Port
        && requestOrigin.AbsolutePath == "/";
}
