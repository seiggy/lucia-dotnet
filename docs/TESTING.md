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
| Auth | `Auth/HmacSessionServiceTests.cs`, `OnboardingMiddlewareTests.cs`, `MongoApiKeyServiceTests.cs`, `InternalTokenAuthenticationHandlerTests.cs` | HMAC signing, middleware gating, API key hashing, internal token validation |
| Orchestration Models | `Models/AgentChoiceResultTests.cs`, `AgentResponseTests.cs`, `InputRequiredPipelineTests.cs` | Router decision parsing, response aggregation, input-required flow |
| Services | `Services/ContextExtractorTests.cs`, `EntityMatchNameFormatterTests.cs`, `PromptCachingChatClientTests.cs`, `ProviderModelCatalogServiceTests.cs`, `TaskPersistenceMetricsTests.cs` | Context extraction, entity name formatting, cache key hashing, provider catalog, task metrics |
| Timer & Alarms | `Timer/TimerSkillTests.cs`, `AlarmSkillTests.cs`, `SchedulerSkillTests.cs` | Timer creation/cancellation, alarm CRUD, scheduler CRON parsing |
| Scheduled Tasks | `ScheduledTasks/` (9 files) | CRON scheduling, task factory, recovery, alarm/timer/agent task execution |
| Training | `Training/ConversationTraceTests.cs`, `JsonlExportTests.cs`, `TraceCaptureObserverTests.cs` | Trace model serialization, JSONL export format, observer lifecycle |
| Plugins | `Plugins/PluginSystemTests.cs`, `PluginConfigSchemaTests.cs`, `PluginUpdateDetectionTests.cs` | Plugin loading, config schema validation, version comparison |
| Presence | `Presence/PresenceDetectionServiceTests.cs`, `MongoPresenceSensorRepositoryTests.cs` | Sensor discovery, occupancy logic, MongoDB mapping persistence |
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

Integration tests verify components with real infrastructure (Redis, MongoDB) using [Testcontainers](https://dotnet.testcontainers.org/) to spin up Docker containers on-demand.

### Infrastructure Requirements

- **Docker** must be running for Testcontainers-based tests
- Redis tests use `Testcontainers.Redis` to start a real Redis instance
- MongoDB tests may require a running MongoDB instance or Testcontainers

### Key Integration Tests

| Test File | Infrastructure | What's Tested |
|-----------|---------------|---------------|
| `Services/RedisTaskStoreTests.cs` | Redis (Testcontainers) | Task CRUD, TTL expiry, key scanning |
| `Services/ModelProviderResolverTests.cs` | None (mocked providers) | Provider creation for all 7 types, OpenTelemetry wrapping |
| `Integration/DurableTaskPersistenceTests.cs` | Redis | Task durability across restarts |
| `Integration/ExtractDeviceIdTests.cs` | None | Device ID extraction from HA entities |

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
