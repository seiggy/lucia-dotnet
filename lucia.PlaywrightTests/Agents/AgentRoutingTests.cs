using Microsoft.Playwright;
using lucia.PlaywrightTests.Infrastructure;

namespace lucia.PlaywrightTests.Agents;

/// <summary>
/// End-to-end tests verifying the orchestrator can route requests to all three
/// agent types: in-process (light-agent), remote A2A (timer-agent), and dynamic (joke-agent).
/// Each test sends a message through the dashboard chat UI and asserts the response
/// is not an orchestrator error.
/// </summary>
[Collection(TestCollections.Playwright)]
[Trait("Category", "Playwright")]
public sealed class AgentRoutingTests : PlaywrightTestBase
{
    private static readonly string[] ErrorIndicators =
    [
        "NOT AVAILABLE",
        "NAME OR SERVICE NOT KNOWN",
        "CONNECTION REFUSED",
        "UNABLE TO CONNECT",
        "ENCOUNTERED AN ERROR",
        "TASK WAS CANCELED",
        "NO AGENTS",
        "FAILED"
    ];

    public AgentRoutingTests(PlaywrightFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task InProcessAgent_LightAgent_ReturnsResponse()
    {
        await LoginAndSelectOrchestratorAsync();
        var response = await SendMessageAndGetResponseAsync("Are the lights on in the office?");

        AssertNotErrorResponse(response, "light-agent (in-process)");
    }

    [Fact]
    public async Task RemoteAgent_TimerAgent_ReturnsResponse()
    {
        await LoginAndSelectOrchestratorAsync();
        var response = await SendMessageAndGetResponseAsync("Set a 10 second timer to alert me in the office");

        AssertNotErrorResponse(response, "timer-agent (remote A2A)");

        // Timer-specific: should mention timer/reminder creation
        var upper = response.ToUpperInvariant();
        var mentionsTimer = upper.Contains("TIMER")
                            || upper.Contains("REMINDER")
                            || upper.Contains("SET")
                            || upper.Contains("CREATED")
                            || upper.Contains("STARTED")
                            || upper.Contains("SECOND");
        Assert.True(mentionsTimer,
            $"Expected timer-related response, but got: {response}");
    }

    [Fact]
    public async Task DynamicAgent_JokeAgent_ReturnsResponse()
    {
        await LoginAndSelectOrchestratorAsync();
        var response = await SendMessageAndGetResponseAsync("Tell me a joke");

        AssertNotErrorResponse(response, "joke-agent (dynamic)");
    }

    private async Task LoginAndSelectOrchestratorAsync()
    {
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
            // Already authenticated
        }

        await NavigateViaSidebarAsync("Agents");
        await Page.WaitForURLAsync("**/agent-dashboard", new() { Timeout = 10_000 });
        await WaitForLoadingToFinishAsync();

        var orchestratorCard = Page.Locator("h3", new() { HasTextString = "orchestrator" });
        await orchestratorCard.First.WaitForAsync(new() { Timeout = 15_000 });
        await orchestratorCard.First.ClickAsync();

        var detailHeader = Page.Locator("h2", new() { HasTextString = "orchestrator" });
        await detailHeader.WaitForAsync(new() { Timeout = 5_000 });
    }

    private async Task<string> SendMessageAndGetResponseAsync(string message)
    {
        var messageInput = Page.GetByPlaceholder("Send a test message to this agent");
        await messageInput.WaitForAsync(new() { Timeout = 5_000 });

        await messageInput.FillAsync(message);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        // Wait for loading indicator
        await Page.Locator(".animate-bounce").First.WaitForAsync(new() { Timeout = 5_000 });

        // Wait for agent response (up to 90s for LLM + A2A round-trip)
        var agentBubble = Page.Locator("div.justify-start .whitespace-pre-wrap");
        await agentBubble.Last.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 90_000 });

        var responseText = await agentBubble.Last.TextContentAsync();
        Assert.NotNull(responseText);
        Assert.False(string.IsNullOrWhiteSpace(responseText), $"Agent response to '{message}' should not be empty");

        await TakeScreenshotAsync($"routing_{message[..Math.Min(20, message.Length)].Replace(' ', '_')}");

        return responseText!;
    }

    private static void AssertNotErrorResponse(string response, string agentDescription)
    {
        var upper = response.ToUpperInvariant();
        foreach (var indicator in ErrorIndicators)
        {
            Assert.DoesNotContain(indicator, upper,
                StringComparison.Ordinal);
        }
    }

    private static string GetRequiredApiKey()
    {
        var key = Environment.GetEnvironmentVariable("LUCIA_DASHBOARD_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "LUCIA_DASHBOARD_API_KEY environment variable must be set to run Playwright tests.");
        }
        return key;
    }
}
