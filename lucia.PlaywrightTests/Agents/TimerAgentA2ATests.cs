using Microsoft.Playwright;
using lucia.PlaywrightTests.Infrastructure;

namespace lucia.PlaywrightTests.Agents;

/// <summary>
/// End-to-end test: send a timer request through the orchestrator via the dashboard,
/// verifying that the orchestrator can successfully invoke the remote timer-agent
/// via A2A protocol with Aspire service discovery.
/// </summary>
[Collection(TestCollections.Playwright)]
[Trait("Category", "Playwright")]
public sealed class TimerAgentA2ATests : PlaywrightTestBase
{
    public TimerAgentA2ATests(PlaywrightFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Orchestrator_CanInvokeTimerAgent_ViaA2A()
    {
        // ── Arrange: Login ──────────────────────────────────────
        var apiKey = GetRequiredApiKey();

        await NavigateToDashboardAsync("/");

        var apiKeyInput = Page.Locator("#apiKey");
        try
        {
            await apiKeyInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            await apiKeyInput.FillAsync(apiKey);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
            await Page.WaitForURLAsync("**/", new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            // Already authenticated — no login page shown
        }

        // ── Arrange: Navigate to Agents page ────────────────────
        await NavigateViaSidebarAsync("Agents");
        await Page.WaitForURLAsync("**/agent-dashboard", new() { Timeout = 10_000 });
        await WaitForLoadingToFinishAsync();

        // ── Arrange: Select the orchestrator agent ──────────────
        var orchestratorCard = Page.Locator("h3", new() { HasTextString = "orchestrator" });
        await orchestratorCard.First.WaitForAsync(new() { Timeout = 15_000 });
        await orchestratorCard.First.ClickAsync();

        var detailHeader = Page.Locator("h2", new() { HasTextString = "orchestrator" });
        await detailHeader.WaitForAsync(new() { Timeout = 5_000 });

        // ── Act: Send a timer request ───────────────────────────
        var messageInput = Page.GetByPlaceholder("Send a test message to this agent");
        await messageInput.WaitForAsync(new() { Timeout = 5_000 });

        const string testMessage = "create a 30 second timer in the office";
        await messageInput.FillAsync(testMessage);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        // Wait for the loading indicator
        await Page.Locator(".animate-bounce").First.WaitForAsync(new() { Timeout = 5_000 });

        // Wait for agent response (up to 90s for LLM + A2A round-trip)
        var agentBubble = Page.Locator("div.justify-start .whitespace-pre-wrap");
        await agentBubble.Last.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 90_000 });

        var responseText = await agentBubble.Last.TextContentAsync();
        Assert.NotNull(responseText);
        Assert.False(string.IsNullOrWhiteSpace(responseText), "Agent response should not be empty");

        // Take screenshot for debugging regardless of outcome
        await TakeScreenshotAsync("timer_agent_a2a_response");

        // ── Assert: Response should indicate timer was created ───
        // The response should NOT contain service discovery errors
        var responseUpper = responseText!.ToUpperInvariant();
        Assert.DoesNotContain("NAME OR SERVICE NOT KNOWN", responseUpper);
        Assert.DoesNotContain("CONNECTION REFUSED", responseUpper);
        Assert.DoesNotContain("UNABLE TO CONNECT", responseUpper);

        // The response should mention the timer being created (various phrasings possible)
        var mentionsTimer = responseUpper.Contains("TIMER")
                            || responseUpper.Contains("REMINDER")
                            || responseUpper.Contains("SET")
                            || responseUpper.Contains("CREATED")
                            || responseUpper.Contains("STARTED");
        Assert.True(mentionsTimer,
            $"Expected response to mention timer/reminder creation, but got: {responseText}");
    }

    private static string GetRequiredApiKey()
    {
        var key = Environment.GetEnvironmentVariable("LUCIA_DASHBOARD_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "LUCIA_DASHBOARD_API_KEY environment variable must be set to run Playwright tests. " +
                "This is the API key used to authenticate with the Lucia dashboard.");
        }
        return key;
    }
}
