# Agent Evaluation Tests

This directory contains evaluation tests for Lucia's multi-agent system using the [Microsoft.Extensions.AI.Evaluation](https://learn.microsoft.com/dotnet/ai/evaluation/) SDK.

## Architecture

```
Orchestration/
├── EvalTestFixture.cs              # Shared fixture: Azure client, agent metadata, mock setup
├── EvalTestCollection.cs           # xUnit collection definition
├── AgentEvalTestBase.cs            # Base class: reporting config, model parameterization, helpers
├── EvalConfiguration.cs            # Typed config model for eval settings
├── EvalModelConfig.cs              # Per-model config (deployment name, temperature)
├── AzureOpenAISettings.cs          # Azure OpenAI connection settings
├── LightAgentEvalTests.cs          # LightAgent eval tests (6 tests)
├── MusicAgentEvalTests.cs          # MusicAgent eval tests (7 tests)
├── OrchestratorEvalTests.cs        # OrchestratorAgent eval tests (8 tests)
└── README.md                       # This file
```

## Configuration

Eval tests use `appsettings.json` (located at `lucia.Tests/appsettings.json`) for configuration, following the same ASP.NET Core configuration pattern used in production. Environment variables can override any JSON value using the standard double-underscore convention.

### appsettings.json Schema

```json
{
  "EvalConfiguration": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.com/",
      "ApiKey": null
    },
    "Models": [
      { "DeploymentName": "gpt-4o" },
      { "DeploymentName": "chat-mini", "Temperature": 0.0 },
      { "DeploymentName": "gpt-5.2-chat", "Temperature": 0.3 }
    ],
    "JudgeModel": "gpt-4o",
    "ReportPath": null,
    "ExecutionName": null
  }
}
```

### Configuration Properties

| Property | Type | Description | Default |
|---|---|---|---|
| `AzureOpenAI.Endpoint` | `string` | Azure OpenAI / AI Foundry endpoint URL | **(required)** |
| `AzureOpenAI.ApiKey` | `string?` | API key (uses `AzureCliCredential` when null) | `null` |
| `Models` | `EvalModelConfig[]` | Deployments to benchmark. Each test runs against all models. | `[{ "DeploymentName": "gpt-4o" }]` |
| `Models[].DeploymentName` | `string` | Azure deployment name | — |
| `Models[].Temperature` | `float?` | Temperature override. Omit for reasoning models (o-series). | `null` |
| `JudgeModel` | `string` | Deployment for LLM-as-judge evaluators | `gpt-4o` |
| `ReportPath` | `string?` | Directory for `DiskBasedReportingConfiguration` | `%TEMP%/lucia-eval-reports` |
| `ExecutionName` | `string?` | Report execution name | Timestamp (`yyyyMMddTHHmmss`) |

### Environment Variable Overrides

Any JSON config value can be overridden using environment variables with the `EvalConfiguration__` prefix and double-underscore separators:

```powershell
# Override endpoint
$env:EvalConfiguration__AzureOpenAI__Endpoint = "https://your-resource.openai.azure.com/"

# Override API key
$env:EvalConfiguration__AzureOpenAI__ApiKey = "your-key"

# Override judge model
$env:EvalConfiguration__JudgeModel = "gpt-4o-mini"
```

> **Note:** Array items (like `Models`) are harder to override via env vars. For model changes, editing `appsettings.json` is recommended.

## Running Tests

### Run all eval tests

```powershell
dotnet test lucia.Tests --filter "Category=Eval"
```

### Filter by agent

```powershell
# Only LightAgent tests
dotnet test lucia.Tests --filter "Category=Eval&Agent=Light"

# Only MusicAgent tests
dotnet test lucia.Tests --filter "Category=Eval&Agent=Music"

# Only Orchestrator tests
dotnet test lucia.Tests --filter "Category=Eval&Agent=Orchestrator"
```

### Filter by evaluator type

```powershell
# Only tool call accuracy tests
dotnet test lucia.Tests --filter "Category=Eval&Evaluator=ToolCallAccuracy"

# Only intent resolution tests
dotnet test lucia.Tests --filter "Category=Eval&Evaluator=IntentResolution"

# Only task adherence tests
dotnet test lucia.Tests --filter "Category=Eval&Evaluator=TaskAdherence"
```

### Combine filters

```powershell
# LightAgent tool call accuracy only
dotnet test lucia.Tests --filter "Category=Eval&Agent=Light&Evaluator=ToolCallAccuracy"
```

## Generating Reports

After running eval tests, generate an HTML report with the `dotnet aieval` tool:

```powershell
# Install the tool (first time only)
dotnet tool install -g Microsoft.Extensions.AI.Evaluation.Console

# Generate report from cached data
dotnet aieval report --path "$env:TEMP/lucia-eval-reports" --output eval-report.html

# Or with a custom report path
dotnet aieval report --path C:\TestReports --output eval-report.html
```

## Test Categories (Traits)

Tests use three orthogonal trait dimensions for flexible filtering:

| Trait | Values | Purpose |
|---|---|---|
| `Category` | `Eval` | Distinguishes eval tests from unit/integration tests |
| `Agent` | `Light`, `Music`, `Orchestrator` | Targets a specific agent |
| `Evaluator` | `ToolCallAccuracy`, `IntentResolution`, `TaskAdherence` | Targets a specific evaluation focus |

## Evaluators Used

### Built-in (Microsoft.Extensions.AI.Evaluation.Quality)

- **RelevanceEvaluator** — Measures response relevance to the user's question
- **CoherenceEvaluator** — Measures logical coherence of the response
- **CompletenessEvaluator** — Measures response completeness
- **ToolCallAccuracyEvaluator** — SDK-provided LLM-as-judge evaluator that assesses tool call relevance, parameter correctness against `AIFunctionDeclaration` definitions, and parameter value extraction accuracy. Returns a `BooleanMetric`. Tool definitions are passed via `ToolCallAccuracyEvaluatorContext`.

## How It Works

1. **EvalTestFixture** loads configuration from `appsettings.json` with environment variable overrides, creates an `AzureOpenAIClient`, and uses `AzureCliCredential` when no API key is configured.
2. Agents are constructed with **FakeItEasy** mocks (only `IHomeAssistantClient` is mocked) to extract their `Instructions` and `Tools` properties.
3. Each test creates a **raw `IChatClient`** (no function invocation middleware) for the deployment under test.
4. The user's message is sent with the agent's instructions and tools via `ChatOptions`.
5. The model's response is inspected for:
   - **Tool calls** (`FunctionCallContent`) — verifying the correct tools were selected
   - **Text content** — verifying adherence and domain boundaries
   - **JSON structure** — for orchestrator routing validation
6. **LLM-as-judge evaluators** score the response quality using a separate judge model.
7. Results are persisted via `DiskBasedReportingConfiguration` for `dotnet aieval report`.

## Adding New Test Scenarios

1. Add a new test method to the appropriate `*EvalTests.cs` class
2. Apply `[Trait]` attributes for all three dimensions
3. Use `[SkippableTheory]` + `[MemberData(nameof(ModelIds))]` for model parameterization
4. Call `RunAndEvaluateAsync(...)` with the agent's instructions and tools
5. Assert tool calls and evaluation metrics
