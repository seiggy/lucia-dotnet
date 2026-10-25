# Testing Strategy

Lucia uses a layered testing strategy with four tiers: unit tests, integration tests, evaluation (eval) tests, and end-to-end (E2E) tests. Each tier targets different failure modes and runs at different speeds.

## Quick Reference

| Tier | Framework | Location | Run Command |
|------|-----------|----------|-------------|
| Unit | xUnit + FakeItEasy | `lucia.Tests/` | `dotnet test lucia.Tests --filter "Category!=Eval"` |
| Integration | xUnit + Testcontainers | `lucia.Tests/` | `dotnet test lucia.Tests --filter "Category!=Eval"` |
| Eval | xUnit + MS AI Evaluation | `lucia.Tests/Orchestration/` | `dotnet test lucia.Tests --filter "Category=Eval"` |
| E2E | Playwright | `lucia-playwright/e2e/` | `npx playwright test` |

## Unit Tests

Unit tests verify individual classes in isolation using [FakeItEasy](https://fakeiteasy.github.io/) for mocking. They are fast, deterministic, and require no external services.

### What's Covered

| Area | Test Files | What's Tested |
|------|-----------|---------------|
| Auth | `Auth/HmacSessionServiceTests.cs`, `OnboardingMiddlewareTests.cs`, `InternalTokenAuthenticationHandlerTests.cs` | HMAC signing, middleware gating, API key hashing, internal token validation |
| Orchestration Models | `Models/AgentChoiceResultTests.cs`, `AgentResponseTests.cs`, `InputRequiredPipelineTests.cs` | Router decision parsing, response aggregation, input-required flow |
| Services | `Services/ContextExtractorTests.cs`, `EntityMatchNameFormatterTests.cs`, `PromptCachingChatClientTests.cs`, `ProviderModelCatalogServiceTests.cs`, `TaskPersistenceMetricsTests.cs` | Context extraction, entity name formatting, cache key hashing, provider catalog, task metrics |
| Timer & Alarms | `Timer/TimerSkillTests.cs`, `AlarmSkillTests.cs`, `SchedulerSkillTests.cs` | Timer creation/cancellation, alarm CRUD, scheduler CRON parsing |
| Scheduled Tasks | `ScheduledTasks/` (9 files) | CRON scheduling, task factory, recovery, alarm/timer/agent task execution |
| Training | `Training/ConversationTraceTests.cs`, `JsonlExportTests.cs`, `TraceCaptureObserverTests.cs` | Trace model serialization, JSONL export format, observer lifecycle |
| Plugins | `Plugins/PluginSystemTests.cs`, `PluginConfigSchemaTests.cs`, `PluginUpdateDetectionTests.cs` | Plugin loading, config schema validation, version comparison |
| Data providers | `Data/Providers/*Contract.cs`, `Sqlite*Tests.cs`, `Postgres*Tests.cs` | One abstract contract per store interface (API keys, memory, traces, tasks, alarms, speaker profiles, templates, and more) run against SQLite and PostgreSQL on the real migrated schema |
| Presence | `Presence/PresenceDetectionServiceTests.cs` | Sensor discovery, occupancy logic |
| Home Assistant | `HomeAssistantApiTests.cs`, `HomeAssistantClientConfigurationTests.cs`, `HomeAssistantErrorHandlingTests.cs`, `HomeAssistantModelsTests.cs`, `HomeAssistantTemplateClientTests.cs` | HA API calls, configuration, error handling, model deserialization |
| Diagnostics | `Diagnostics/EmbeddingMatchingTests.cs` | Entity matcher scoring and ranking |

### Writing Unit Tests

```csharp
public class MyServiceTests
{
    [Fact]
    public async Task DoWork_WhenCondition_ShouldExpected()
    {
        // Arrange — create fakes
        var dependency = A.Fake<IDependency>();
        A.CallTo(() => dependency.GetDataAsync(A<CancellationToken>._))
            .Returns(new Data("value"));

        var sut = new MyService(dependency);

        // Act
        var result = await sut.DoWorkAsync();

        // Assert
        Assert.Equal("expected", result);
    }
}
```

**Conventions:**
- Test class name: `{ClassUnderTest}Tests`
- Test method name: `{Method}_{Scenario}_{Expected}`
- One assertion per test when practical
- Use `FakeItEasy` for all mocking (not Moq)

## Integration Tests

Integration tests verify components with real infrastructure (Redis, PostgreSQL) using [Testcontainers](https://dotnet.testcontainers.org/) to spin up Docker containers on-demand.

### Infrastructure Requirements

- **Docker** must be running for Testcontainers-based tests
- Redis tests use `Testcontainers.Redis` to start a real Redis instance
- Postgres store tests (`Data/Providers/Postgres*`) share one `postgres:17-alpine` container through the `PostgresStores` collection; they carry `[Trait("Category", "Integration")]`

### Key Integration Tests

| Test File | Infrastructure | What's Tested |
|-----------|---------------|---------------|
| `Services/RedisTaskStoreTests.cs` | Redis (Testcontainers) | Task CRUD, TTL expiry, key scanning |
| `Services/ModelProviderResolverTests.cs` | None (mocked providers) | Provider client creation and OpenTelemetry wrapping |
| `Integration/DurableTaskPersistenceTests.cs` | Redis | Task durability across restarts |
| `Integration/ExtractDeviceIdTests.cs` | None | Device ID extraction from HA entities |

### Redis location snapshot publication

`RedisLocationSnapshotStore.SaveAsync` waits for acknowledgments for the floors,
areas, entities, and loaded-at writes. A failed save propagates to its caller,
so save-then-version-bump operations do not publish a version for a rejected save.
These operations can fail even when the local in-memory snapshot was updated;
they do not roll back local changes or partial Redis writes. Acknowledgment is
not a guarantee of disk durability or atomic multi-key publication.

The regression tests use two complementary checks:

- `Services/EntityLocation/RedisLocationSnapshotStoreTests.cs` and
  `RedisSnapshotPublicationTests.cs` use controlled acknowledgments, without
  timing sleeps. They cover every snapshot write, enqueue/execute/reply failures,
  and version ordering for reload, removal, and embedding eviction.
- `Services/EntityLocation/RedisSnapshotPublicationIntegrationTests.cs` uses an
  isolated Redis container and separate writer/reader connections. It verifies
  snapshot visibility, the existing 24-hour TTL, and a rejected-write case. A
  test-only Redis user cannot issue `SET`, `SETEX`, or `PSETEX`, but can still
  increment the version. The test verifies that denied writes leave the shared
  version unchanged. It does not change permissions on an existing Redis server.

Run the location tests with Docker available:

```powershell
dotnet test lucia.Tests/lucia.Tests.csproj --filter "FullyQualifiedName~Services.EntityLocation"
```

The four snapshot keys and separate version counter remain unchanged. This fix
does not make concurrent publication or reads of multiple keys atomic.

## Evaluation Tests

Eval tests measure agent quality using the [Microsoft.Extensions.AI.Evaluation](https://learn.microsoft.com/dotnet/ai/evaluation/) SDK. They invoke real agents against real LLMs and score responses with LLM-as-judge evaluators.

> **See:** [`lucia.Tests/Orchestration/README.md`](../lucia.Tests/Orchestration/README.md) for detailed eval test configuration and usage.

### Overview

- **Agents tested:** LightAgent, MusicAgent, OrchestratorAgent
- **Evaluators:** Relevance, Coherence, ToolCallAccuracy, TaskAdherence, Latency
- **Parameterization:** Model × prompt variant cross-product via `[MemberData]`
- **Reports:** Generated via `dotnet aieval report` with disk-based reporting

### Running Eval Tests

```bash
# Requires Azure OpenAI credentials in appsettings.json or environment
dotnet test lucia.Tests --filter "Category=Eval"

# Generate HTML report
dotnet aieval report --path lucia.Tests/TestResults/
```

### Configuration

Eval tests read from `lucia.Tests/appsettings.json`:

```json
{
  "EvalConfiguration": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.com/",
      "ApiKey": null
    },
    "Models": [
      { "DeploymentName": "gpt-4o" }
    ],
    "JudgeModel": "gpt-4o"
  }
}
```

Override with environment variables: `EvalConfiguration__AzureOpenAI__ApiKey=sk-...`

### Interactive evaluation harness judge

`lucia.EvalHarness` has separate judge settings under `Harness:AzureOpenAI`.
It uses the Azure OpenAI v1 Responses API by default for task-completion scoring,
personality scoring, and prompt optimization. Set `JudgeDeployment` to the Azure
deployment name. The model under test remains on the selected inference backend.

Configure the judge locally without changing tracked settings:

```powershell
dotnet user-secrets set "Harness:AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com/" --project .\lucia.EvalHarness
dotnet user-secrets set "Harness:AzureOpenAI:JudgeDeployment" "your-judge-deployment" --project .\lucia.EvalHarness
az login
```

The endpoint can be an Azure OpenAI or Foundry resource URL, with or without
`/openai/v1/`. Do not use a Foundry project URL ending in `/api/projects/...`.
With no API key, the judge uses `AzureCliCredential`; the signed-in identity needs
permission to run inference. API-key authentication is also supported through
the `Harness:AzureOpenAI:ApiKey` user secret.

Environment variables such as `Harness__AzureOpenAI__Endpoint` and
`Harness__AzureOpenAI__JudgeDeployment` override JSON settings and user secrets.
Responses requests omit temperature and top-p overrides so reasoning models can
use their supported defaults. They also send `store: false` to disable stored
responses. Set `Harness:AzureOpenAI:UseResponsesApi` to `false` only for a
deployment that requires legacy Chat Completions.

With no judge endpoint, key, or deployment configured, judge metrics remain
unavailable. Partial configuration fails explicitly rather than disabling the judge.

#### GitHub Copilot judge

The harness prompts for a judge provider before selecting models under test.
Choose **GitHub Copilot** to use your Copilot subscription for judging only.
The harness reuses your GitHub login or runs `copilot login --device-code`.
Install the [Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/install-copilot-cli)
to sign in. It never asks for or saves your GitHub password.

After sign-in, choose a model from your account's available models, then context
size and reasoning effort. Long context appears only when the model advertises
it. Context sizes shown beside the choices are prompt-token budgets, not output
limits. Effort choices also come from model metadata. Copilot plan limits,
organization policies, and usage charges apply; this is not unlimited or free inference.

Optional defaults use the same user-secret and environment-variable conventions:

```json
{
  "Harness": {
    "JudgeProvider": "GitHubCopilot",
    "GitHubCopilot": {
      "Model": "gpt-5.4",
      "ContextTier": "default",
      "ReasoningEffort": "low"
    }
  }
}
```

Valid judge providers are `AzureOpenAI`, `GitHubCopilot`, and `None`. Copilot
context tiers are `default` and `long_context`, subject to model availability.
Interactive choices apply to the current run. Save defaults in user secrets or
environment variables rather than changing tracked settings.

Copilot judgments use a fresh session for each request. Tools, skills, custom
instructions, configuration discovery, file hooks, git operations, session
store tooling, and automatic compaction are disabled for these sessions.
Copilot's local runtime files follow its normal retention behavior. Evaluation
instructions and conversation text are sent to Copilot; this is cloud inference,
not an OS sandbox. Sampling options are left to Copilot, and JSON output is
requested through instructions rather than provider-enforced JSON mode.

Check the configured judge without running a local model or the evaluation suite:

```powershell
dotnet run --project .\lucia.EvalHarness -- --check-judge
```

This command uses saved configuration without prompting and makes one small
task-completion judgment. GitHub sign-in must already be complete for Copilot.
Bundled configuration and datasets resolve from the application directory, so
launching from the repository root works. A current-directory `appsettings.json`
can override the bundled defaults.

### Model-under-test backends

Configure named entries in `Harness:Backends`. Backend selection is followed by
model selection for each backend. A model runs only on the backend that advertised
it, including personality evaluations. Parameter sweeps use the primary backend's
model list. Foundry sampling sweeps are not offered because Responses uses provider
sampling defaults.

| Type | Discovery | Inference |
|------|-----------|-----------|
| `Ollama` | `/api/tags` | Native Ollama chat |
| `OpenAICompat` | `/v1/models` | OpenAI-compatible chat, including remote llama.cpp |
| `AzureFoundry` | Azure account deployments | Azure OpenAI v1 Responses |
| `OpenRouter` | `/api/v1/models` | OpenRouter chat completions |

llama.cpp endpoints accept a server root or an existing `/v1` suffix. Router models
can be selected while unloaded; the router loads them when requested. Models
advertised with embedding-server arguments or an embedding-only `capabilities`
array are excluded from chat evaluations. Mixed embedding/chat capabilities and
models without capability metadata remain selectable.
`ApiKey` supplies bearer authentication for OpenAI-compatible servers and
OpenRouter. OpenRouter requires a key to run inference.

### Ollama context budgets

The harness sends an explicit `options.num_ctx` for native Ollama evaluations.
The profile's `NumContext` takes precedence over `Harness:Ollama:DefaultNumContext`.
If both are missing or blank, the harness uses 16,384 tokens. This is a harness
fallback, not an Ollama default or a guarantee that every prompt fits.

```json
{
  "Harness": {
    "Ollama": { "DefaultNumContext": 16384 },
    "ParameterProfiles": {
      "precise": { "Name": "precise", "NumContext": 32768 }
    }
  }
}
```

Environment overrides include `Harness__Ollama__DefaultNumContext` and
`Harness__ParameterProfiles__precise__NumContext`. Built-in profiles inherit the
configured default. The custom-profile prompt also exposes the budget. Zero or
negative budgets fail validation rather than silently selecting a backend default.

Size the budget for the largest serialized agent instructions, tool definitions,
conversation, and output reserve in the run. Check the model's supported context
and memory use on the target machine. Larger contexts use more memory. See
[Ollama context length](https://docs.ollama.com/context-length) and verify the
loaded allocation with `ollama ps`. A positive output limit must be smaller than
the selected context, but this check does not tokenize the input or guarantee fit.
Unlimited output remains supported and does not reserve a bounded output size.

Standard JSON, Markdown, HTML, console profile summaries, personality exports,
and sweep exports retain the resolved profile budget. Reports call it
**requested context**; they do not claim it is a measured backend allocation.
Sweeps use one fixed context across the baseline and combinations, inheriting
the factory's selected profile unless `ParameterSweepConfig.NumContext` overrides
it. Personality runs use the resolved `default` inference profile.

An explicit positive integer `num_ctx` on a caller's `ChatOptions` still wins over
the profile value. Profile reports describe the resolved profile setting, not a
per-request override audit. OpenAI-compatible adapters do not send this
Ollama-only setting; configure their context allocation on the server instead.

The fix does not estimate tokenizer-specific input sizes, automatically select a
budget from VRAM, or collect observed `/api/ps` context lengths. Those are separate
qualification steps. Wire tests run offline through the installed OllamaSharp
adapter, so no model downloads or live inference are required for CI.

For Foundry, set `Endpoint` and `AzureResourceId`. Discovery uses `AzureCliCredential`
against the management API and requires permission to read the account's
deployments. Inference uses the configured API key or Azure CLI credentials.
The model list includes successful deployments that advertise Responses support,
using deployment names rather than undeployed catalog model IDs.

```json
{
  "Harness": {
    "Backends": [
      {"Name": "Ollama", "Type": "Ollama", "Endpoint": "http://localhost:11434"},
      {"Name": "Remote llama.cpp", "Type": "OpenAICompat", "Endpoint": "http://your-server:8000/"},
      {
        "Name": "Foundry",
        "Type": "AzureFoundry",
        "Endpoint": "https://your-resource.openai.azure.com/",
        "AzureResourceId": "/subscriptions/your-subscription/resourceGroups/your-group/providers/Microsoft.CognitiveServices/accounts/your-resource"
      },
      {"Name": "OpenRouter", "Type": "OpenRouter", "Endpoint": "https://openrouter.ai/api/v1"}
    ]
  }
}
```

Keep API keys and machine-specific endpoints in user secrets. For example, with
the ordering above, the OpenRouter key is `Harness:Backends:3:ApiKey`. Existing
configurations can have different indexes.

List configured backends and their available models without starting evaluations:

```powershell
dotnet run --project .\lucia.EvalHarness -- --list-backend-models
```

For Lucia.OS, use `OpenAICompat` with an inference-only key and an already-loaded
chat model. See [Lucia.OS inference qualification](LUCIA_OS_INFERENCE.md) for the
source-pinned contract checks, configuration and separate live acceptance steps.

### Music Agent isolation runs

The Music Agent scenarios live in `lucia.EvalHarness/TestData/music-agent.yaml`.
They cover player lookup, artist, album, song, genre, audiobook and shuffle
playback, transport controls, now-playing, volume, STT errors, and out-of-domain
requests.

Select both music targets in the agent picker to run the same model at two seams:

| Target | What it measures |
|--------|------------------|
| `MusicAgent` | Music Agent tool choice, arguments, tool execution, and final response |
| `MusicAgentOrchestrated` | Router selection first, then the same Music Agent checks |

Music scenarios that set `expected_agent` run through the orchestrated target.
If the router chooses another agent, the report records a routing failure and
does not add misleading missing-tool failures. If routing succeeds, failures
come from the Music Agent or its tools. The separate agent names keep both
result sets side by side in console, JSON, and HTML reports.

The orchestrator router allows 2,048 output tokens. Reasoning models such as
`gpt-oss` may spend hundreds of tokens before emitting the small JSON routing
decision; lower limits can end with reasoning content but no parseable response.

Each scenario starts a fresh agent session, so earlier prompts and tool calls
never reach the model or the validator. Tools that resolve their own player,
such as `NextTrack` and `SetVolume`, list `FindPlayer` with `optional: true`.
An absent optional call is skipped; a present one is still checked, and any
number of consecutive repeats is accepted. An optional entry can list
`alternatives`, so one entry can cover a mix of read-only lookups. Only
`find_player_by_name` requires `FindPlayer`. Response text checks ignore curly
quotes and Unicode spaces, which some models emit between words. The JSON
report adds a `routing` object to orchestrated cases with the router's choice,
confidence, per-agent instruction, and the agent's reply.

The router asks for a JSON schema response. A server that cannot honor it fails
every orchestrated case for that model with no route. For llama.cpp behind
LiteLLM, the server needs xgrammar installed.

### Strict and forbidden tool calls

Scenarios can reject calls left after their ordered expectations and ban specific
tools anywhere in the trace:

```yaml
reject_unlisted_tool_calls: true
forbidden_tools:
  - ActivateScene
expected_tool_calls:
  - tool: ListScenes
```

`reject_unlisted_tool_calls` defaults to `false`. Required calls still match in
order. Declared optional lookups can be absent or repeat consecutively, including
their declared alternatives. With strict mode enabled, a call left after that
chain fails validation. Repeating a required call also fails unless another
expectation declares it. Strict mode does not turn the chain into an unordered
allow-list.

`forbidden_tools` defaults to an empty list. Every assistant tool call is checked,
including calls before, between, and after expected calls. A forbidden tool fails
even if strict mode is off or the same tool appears in the expected chain. Tool
names use the validator's existing case-insensitive comparison and trailing
`Async` normalization. Failure messages include the observed call's zero-based
position and name, without argument values.

Forbidden-tool decisions use AgentEval's `NeverCallTool` assertion. The tracer
captures the tool names offered in actual model requests and supplies that
inventory to AgentEval. A forbidden call always fails. If the tool was offered
but never called, the policy passes. If the tool was not offered, or the offered
inventory is unknown, AgentEval reports an inconclusive result rather than a
vacuous pass. The scenario then has `Passed=false`, a null score, and an
`Inconclusive NeverCallTool(...)` reason. A passing response judge cannot turn
that result into a pass or assign it a numeric score. The strict ordered-chain
matcher remains in Lucia; `NeverCallTool` does not replace it.

Scenarios with no expected calls still reject all calls. Scenarios containing
only optional expectations still reject undeclared calls, regardless of the
strict-mode flag. Both explicit policies require conversation tracing. A policy
violation keeps the scenario's `Passed` result false even when other checks and
the response judge produce a high numeric score. These checks detect observed
calls after execution; they do not prevent a tool from running. Pair them with
`expected_final_state` assertions to verify effects on mock Home Assistant state.

The scene fixtures `list_scenes` and `out_of_domain` now explicitly forbid
`ActivateScene`. The listing fixture also enables strict mode.

### Semantic response checks

Set `response_criteria` on a scenario to have the selected LLM judge evaluate its
final response by meaning. `out_of_domain_weather` uses this for general refusal
or redirection language instead of banning words such as "turn" or "brightness".
The deterministic no-tool-call rule still applies.

The semantic verdict is one additional scenario check. A rejected response fails
the scenario and includes the judge's explanation. If the judge is absent,
unreachable, times out, or returns an invalid verdict, the scenario is reported
as N/A with the reason, not passed through a string-matching fallback. Judge calls
use `JudgeTimeoutSeconds` and do not count toward model-under-test token costs.
Other scenarios keep their existing literal response assertions unless changed.

### Speaker-relative locations

`speaker_disambiguated_office_lights` keeps `Speaker: Dianna` and
`Device Area: Zack's Office` deliberately in conflict. "My office" must resolve to
Dianna's Office: the control call must search that named office, both of Dianna's
office lights must turn on, and Zack's office lights must remain off. A generic
"office" search does not satisfy the scenario.

### Climate scenario context

Implicit requests such as "Set the thermostat to 72 degrees" need `device_area`.
The runner forwards it in the user request header. The corresponding entity must
also declare an `area` that exists in the HA snapshot; entity state alone does not
assign a room. These are separate requirements.

Both eval factories use a shared cache fake that returns cache misses for device
and embedding lookups. An empty collection is a cache hit for some skills and
would prevent them from loading entities injected by the scenario. The climate
regression calls the real area-search tool, not just the location service.

Climate fixtures accept either an area lookup or a room-qualified device search.
They check the control call's exact entity ID and final state. The implicit
thermostat scenario includes a bedroom thermostat that must remain unchanged.
Explicit bedroom requests run with Living Room request context to verify that an
explicit target takes precedence.

`expected_tool_calls[].alternatives` describes equivalent calls with their own
argument assertions. Existing scenarios without alternatives keep their strict
tool-call checks. The current HA test snapshot does not contain floor mappings;
these climate fixtures exercise room context, not floor inference.

### Scene scenario data

The HA snapshot contains no scenes, so each scene scenario explicitly seeds its
scene entities and area assignments before execution. `ListScenes` reads those
mock HA states, and `FindScenesByArea` resolves their registered rooms. Activation
assertions require the discovered scene's exact entity ID, not the skill class
name or an arbitrary scene.

The scene dataset uses `scenarios` with `initial_state`, like the climate dataset.
Keep it in that format; a generic `data` dataset does not perform state setup.

### Music scenario speakers

Music fixtures explicitly name and assign the requested endpoints: Office Speaker
in Zack's Office, Bedroom Speaker in Bedroom, and Yamaha Speakers in Living Room.
The office artist case is `play_artist_office`, renamed from the misleading
`play_artist_kitchen` identifier.
The kitchen STT case seeds a Kitchen Speaker instead of an office endpoint.
The snapshot matcher uses substring matching, so fixture names must support the
speaker descriptions used in the prompts. Exact entity names and IDs take
precedence over broader room-substring matches; an exact room query still resolves
the room's devices. Real `FindPlayer` regressions cover
each playback/control fixture rather than assuming a `media_player` state is
enough to make the requested speaker discoverable.

### Token usage, estimated cost, and recommendations

Each executed test records the model-under-test's reported input, cached-input,
and output tokens across its LLM calls, plus estimated USD cost. This also applies
to streaming/tool-loop calls and personality evaluations. Judge requests are
separate and are not included in these model costs. Reasoning tokens already
included in output usage are not charged a second time.

OpenRouter prices come from its model catalog. Per-token prices are converted to
USD per million tokens. Context pricing overrides apply to each request's input
count, not the total across a test. Fixed request fees are added once per LLM call.
Unsupported time-dependent pricing stays unknown instead of using a misleading
base estimate. These are estimates, not invoices.

Foundry prices are configured by deployment name under each backend's
`ModelPricing`. For example:

```json
{
  "ModelPricing": {
    "your-deployment": {
      "InputUsdPerMillionTokens": 4,
      "OutputUsdPerMillionTokens": 20,
      "CachedInputUsdPerMillionTokens": 0.4
    }
  }
}
```

Use your deployment's region, SKU, context tier, and negotiated rates.
The example rates match GPT-5.6-sol Global Standard short-context USD meters in
East US 2, effective September 1, 2026, from the
[Azure Retail Prices API](https://learn.microsoft.com/rest/api/cost-management/retail-prices/azure-retail-prices).
They are not defaults for other models. For tiered prices, add `ContextTiers`
entries with `MinimumInputTokens` and the rates that change. The configured
minimum is inclusive; the OpenRouter importer converts its exclusive thresholds.
Update Foundry rates when the deployment's pricing changes or when testing
long-context requests.

The estimator charges uncached input at the input rate, cached input at the
cached-input rate, and output at the output rate. If a provider does not report
cache counts, no cache discount is assumed. Missing cloud usage or required rates
produce unavailable/partial estimates, never zero-cost cloud results.

Provider charges are independent of the API protocol and endpoint address.
`OpenAICompat` can point at a paid service or a local proxy to one; native Ollama
can also be hosted remotely. Neither protocol automatically means free inference.

`NoProviderCharge` defaults to `false` for every backend. With that default,
configured `ModelPricing` and reported usage produce an estimate. Missing usage
or required prices remain unknown, not zero. OpenRouter can populate its pricing
from discovery; generic OpenAI-compatible providers need configured rates.

For a server with no model-under-test provider charge, explicitly set
`Harness:Backends:<index>:NoProviderCharge` to `true`. The result is recorded as
`no_provider_charge`, even if usage is unavailable. This declaration overrides
stored prices; set it back to `false` when changing to a paid host. It does not
claim that electricity, hardware, a subscription, or judge calls are free.

```json
{
  "Harness": {
    "Backends": [
      {
        "Name": "Self-hosted llama.cpp",
        "Type": "OpenAICompat",
        "Endpoint": "http://localhost:1234",
        "NoProviderCharge": true
      },
      {
        "Name": "Bitdeer",
        "Type": "OpenAICompat",
        "Endpoint": "https://api-inference.bitdeer.ai/v1",
        "NoProviderCharge": false
      }
    ]
  }
}
```

Keep the Bitdeer key in user secrets at `Harness:Backends:1:ApiKey` for this
example. Add rates under `Harness:Backends:1:ModelPricing:<exact-model-id>` using
the pricing fields above and the provider's applicable rates. Until then, its
cost remains unknown. This example configures an API connection; it is not a
claim that a particular Bitdeer model has been qualified.

When named `Backends` are absent, `Harness:Ollama:NoProviderCharge` supplies the
same declaration to the synthesized Ollama backend. It also defaults to `false`.
Existing local configurations must opt in to retain known-zero provider costs.
Unexecuted tests remain `no_calls`, not free runs. Earlier exported `local`
statuses remain recognized when aggregating historical results.

Console, JSON, Markdown, and HTML reports include cost information and token
usage. Recommendations rank by quality first, then lower known mean cost per
test when quality scores tie. Cost never compensates for worse quality.
Parameter-sweep selection retains its quality and variance criteria before cost.

## Conversation Pipeline Tests

Tests for the `/api/conversation` fast-path command parser.

| Test File | Coverage |
|-----------|----------|
| `ConversationCommandProcessorTests.cs` | Main pipeline routing (command vs LLM) |
| `DirectSkillExecutorTests.cs` | Direct skill execution for each skill/action |
| `ResponseTemplateRendererTests.cs` | Template interpolation and fallback |
| `ContextReconstructorTests.cs` | Prompt reconstruction for LLM fallback |

```bash
# Run conversation tests
dotnet test lucia.Tests/lucia.Tests.csproj --filter "FullyQualifiedName~Conversation" -v minimal
```

## End-to-End Tests (Playwright)

E2E tests exercise the full stack (dashboard → API → agents) through browser automation using [Playwright](https://playwright.dev/).

### Test Files

| Spec | What's Tested |
|------|--------------|
| `01-setup-wizard.spec.ts` | Complete onboarding flow (API key gen, HA config, plugin validation) |
| `02-prompt-cache.spec.ts` | Cache hit/miss, eviction, stats accuracy |
| `03-skill-optimizer-traces.spec.ts` | Trace import, search term extraction, optimization run |
| `04-entity-location-impersonate.spec.ts` | Entity location overrides via dashboard |
| `05-domain-settings-persist.spec.ts` | Agent domain settings roundtrip persistence |
| `06-plugin-update-detection.spec.ts` | Plugin version comparison and update badges |

### Running E2E Tests

```bash
cd lucia-playwright

# Install Playwright browsers (first time only)
npx playwright install

# Run all E2E tests (requires running Lucia stack)
npx playwright test

# Run with UI mode for debugging
npx playwright test --ui
```

**Prerequisites:**
- The full Lucia stack must be running (`dotnet run --project lucia.AppHost`)
- Set `DASHBOARD_API_KEY` in a `.env` file for authentication

## Docker Testing

For testing Docker Compose deployments, see [`infra/docker/TESTING.md`](../infra/docker/TESTING.md). This covers:

- Local environment setup (with and without Home Assistant)
- Core functionality validation (health checks, API, device control)
- Integration tests (semantic search, task persistence)
- Performance testing and debugging techniques

## CI/CD

GitHub Actions runs validation on every push and pull request:

| Workflow | What It Does |
|----------|-------------|
| `docker-build-push.yml` | Multi-platform Docker builds (amd64/arm64), Trivy security scanning |
| `helm-lint.yml` | Helm chart linting, template rendering, schema validation |
| `validate-infrastructure.yml` | Docker Compose validation, K8s manifest linting, systemd unit checks |
| `hacs-validate.yml` | Home Assistant Community Store plugin validation |

## Coverage

Code coverage is collected via [coverlet](https://github.com/coverlet-coverage/coverlet):

```bash
dotnet test lucia.Tests --collect:"XPlat Code Coverage"
```

Coverage reports are written to `lucia.Tests/TestResults/`.
