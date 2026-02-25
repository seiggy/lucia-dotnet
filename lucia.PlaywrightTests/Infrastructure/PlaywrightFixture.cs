using Microsoft.Playwright;

namespace lucia.PlaywrightTests.Infrastructure;

/// <summary>
/// Shared Playwright browser instance for all tests in the collection.
/// Manages browser lifecycle so we don't launch a new browser per test class.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        var headless = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS") != "false";

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            // Accept self-signed certs for local development
            Args = ["--ignore-certificate-errors"]
        });
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }

    /// <summary>
    /// Creates a fresh browser context with ignoreHTTPSErrors for local dev certs.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync()
    {
        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            BaseURL = ServiceEndpoints.DashboardUrl
        });
    }

    /// <summary>
    /// Creates a fresh page in a new context pointed at the dashboard.
    /// </summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await NewContextAsync();
        return await context.NewPageAsync();
    }
}
