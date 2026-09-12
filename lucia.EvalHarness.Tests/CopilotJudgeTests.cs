using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Tui;
using Microsoft.Extensions.AI;
using ModelBilling = GitHub.Copilot.ModelBilling;
using ModelCapabilities = GitHub.Copilot.ModelCapabilities;

namespace lucia.EvalHarness.Tests;

public sealed class CopilotJudgeTests
{
    [Fact]
    public void ChildEnvironment_PreservesCredentialDiscoveryButNotTokenOverrides()
    {
        var environment = JudgeSelector.CreateChildEnvironment();

        Assert.DoesNotContain("GH_TOKEN", environment.Keys);
        Assert.DoesNotContain("GITHUB_TOKEN", environment.Keys);
        Assert.DoesNotContain("COPILOT_GITHUB_TOKEN", environment.Keys);
        Assert.DoesNotContain(environment.Keys, name => name.StartsWith("COPILOT_GH_ACCOUNT_", StringComparison.OrdinalIgnoreCase));
        var homeVariable = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
        Assert.Equal(Environment.GetEnvironmentVariable(homeVariable), environment[homeVariable]);
    }

    [Fact]
    public async Task CreateSessionConfig_UsesSelectedModelContextAndEffortWithoutTools()
    {
        var settings = new CopilotJudgeSettings
        {
            Model = "judge-model",
            ContextTier = "long_context",
            ReasoningEffort = "high"
        };

        var config = CopilotJudgeChatClient.CreateSessionConfig(
            settings, [new ChatMessage(ChatRole.System, "Score the answer.")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json });

        Assert.Equal("judge-model", config.Model);
        Assert.Equal(ContextTier.LongContext, config.ContextTier);
        Assert.Equal("high", config.ReasoningEffort);
        Assert.Empty(config.AvailableTools!);
        Assert.False(config.EnableSkills);
        Assert.False(config.EnableConfigDiscovery);
        Assert.False(config.EnableFileHooks);
        Assert.False(config.EnableHostGitOperations);
        Assert.False(config.EnableSessionStore);
        Assert.True(config.SkipCustomInstructions);
        Assert.False(config.InfiniteSessions?.Enabled);
        Assert.Equal(SystemMessageMode.Replace, config.SystemMessage?.Mode);
        Assert.Contains("Score the answer.", config.SystemMessage?.Content);
        Assert.Contains("JSON", config.SystemMessage?.Content);
        Assert.NotNull(config.OnPermissionRequest);
        var decision = await config.OnPermissionRequest(new PermissionRequest(), new PermissionInvocation());
        Assert.Equal("reject", decision.Kind);
    }

    [Theory]
    [InlineData("long_context", null, false)]
    [InlineData("invalid", null, true)]
    [InlineData("default", "unsupported", true)]
    public void ValidateForModel_UnsupportedControlsFailClearly(string context, string? effort, bool longContext)
    {
        var model = CreateModel(longContext);
        var settings = new CopilotJudgeSettings
        {
            Model = model.Id,
            ContextTier = context,
            ReasoningEffort = effort
        };

        Assert.Throws<InvalidOperationException>(() => settings.ValidateForModel(model));
    }

    [Fact]
    public void ValidateForModel_AcceptsAdvertisedControls()
    {
        var model = CreateModel(longContext: true);
        var settings = new CopilotJudgeSettings
        {
            Model = model.Id,
            ContextTier = "long_context",
            ReasoningEffort = "high"
        };

        settings.ValidateForModel(model);
    }

    [Fact]
    public void BuildPrompt_PreservesConversationOrderWithoutRepeatingSystemInstructions()
    {
        var prompt = CopilotJudgeChatClient.BuildPrompt(
        [
            new ChatMessage(ChatRole.System, "system instructions"),
            new ChatMessage(ChatRole.User, "question"),
            new ChatMessage(ChatRole.Assistant, "answer"),
            new ChatMessage(ChatRole.User, "evaluate")
        ]);

        Assert.Equal("[user]\nquestion\n\n[assistant]\nanswer\n\n[user]\nevaluate", prompt);
    }

    // Copilot exposes context-tier pricing through experimental RPC metadata types.
#pragma warning disable GHCP001
    private static ModelInfo CreateModel(bool longContext) => new()
    {
        Id = "judge-model",
        Name = "Judge",
        Capabilities = new ModelCapabilities
        {
            Supports = new ModelSupports { ReasoningEffort = true },
            Limits = new ModelLimits { MaxContextWindowTokens = 200_000 }
        },
        SupportedReasoningEfforts = ["low", "high"],
        Billing = new ModelBilling
        {
            TokenPrices = new ModelBillingTokenPrices
            {
                MaxPromptTokens = 180_000,
                LongContext = longContext
                    ? new ModelBillingTokenPricesLongContext { MaxPromptTokens = 900_000 }
                    : null
            }
        }
    };
#pragma warning restore GHCP001
}
