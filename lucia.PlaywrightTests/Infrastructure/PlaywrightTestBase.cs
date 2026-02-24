using Microsoft.Playwright;
using lucia.PlaywrightTests.Infrastructure;

namespace lucia.PlaywrightTests.Infrastructure;

/// <summary>
/// Base class for Playwright tests providing common helpers and page setup/teardown.
/// </summary>
[Collection(TestCollections.Playwright)]
[Trait("Category", "Playwright")]
public abstract class PlaywrightTestBase : IAsyncLifetime
{
    protected readonly PlaywrightFixture Fixture;
    protected IBrowserContext Context = null!;
    protected IPage Page = null!;

    protected PlaywrightTestBase(PlaywrightFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        Context = await Fixture.NewContextAsync();
        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
    }

    /// <summary>
    /// Navigates to the dashboard and waits for it to be loaded.
    /// </summary>
    protected async Task NavigateToDashboardAsync(string path = "/")
    {
        await Page.GotoAsync(path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30_000
        });
    }

    /// <summary>
    /// Clicks a sidebar nav link by label text and waits for navigation.
    /// </summary>
    protected async Task NavigateViaSidebarAsync(string label)
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = label }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for any loading indicators to disappear.
    /// </summary>
    protected async Task WaitForLoadingToFinishAsync(int timeoutMs = 15_000)
    {
        // Wait for the "Loading..." text to disappear
        var loading = Page.GetByText("Loading...");
        if (await loading.CountAsync() > 0)
        {
            await loading.First.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
        }
    }

    /// <summary>
    /// Takes a screenshot for debugging failed tests.
    /// </summary>
    protected async Task TakeScreenshotAsync(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{name}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png"),
            FullPage = true
        });
    }
}
