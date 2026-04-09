# Decision: Timer Agent Eval Coverage & Router Hint

**Author:** Lambert (QA / Eval Scenario Engineer)
**Date:** 2025-07-24
**Status:** Implemented
**Requested by:** Zack Way

## Context

A real production failure: "setup a task to turn off the office AC unit in 5 minutes" was routed to `general-assistant` at 0% confidence instead of `timer-agent`. The router couldn't even produce valid routing JSON — the model returned empty content.

Root cause: the router system prompt had no domain inference hints for timer/schedule language. The model saw "AC" and had no guidance that time-delay qualifiers should override device-domain routing.

## Decision

Three coordinated changes:

1. **Router system prompt** (`RouterExecutorOptions.cs` Rule 8): Added timer/schedule language inference hint and an explicit IMPORTANT callout that time-delayed device actions route to timer-agent, NOT the device agent.

2. **YAML eval dataset** (`orchestrator.yaml`): Added 15 timer scenarios across 4 categories — basic timer (3), scheduled-action (4), alarm (3), cross-domain-timer (5). Cross-domain scenarios include both delayed and immediate contrasts.

3. **C# eval tests** (`OrchestratorEvalTests.cs`): Added 4 test methods with negative assertions (DoesNotContain climate/general/light) to catch cross-domain misrouting.

## Rationale

- The scheduled-action failure is a **cross-domain confusion** bug — identical device nouns route to different agents depending on temporal modifiers
- Without explicit prompt guidance, LLMs anchor on device nouns ("AC" → climate) and ignore time qualifiers
- Negative assertions are critical — a test that only checks "contains timer" would pass if the router returned both timer AND climate

## Files Changed

- `lucia.Agents/Orchestration/RouterExecutorOptions.cs` — Rule 8 timer inference hint
- `lucia.EvalHarness/TestData/orchestrator.yaml` — 15 new timer scenarios
- `lucia.Tests/Orchestration/OrchestratorEvalTests.cs` — 4 new test methods

## Risk

- Dallas is simultaneously adding the timer-agent card to EvalTestFixture. If the fixture doesn't register timer-agent, the new eval tests will fail at agent creation. Coordinate with Dallas.
- The router hint changes system prompt text — all existing routing tests should be re-run to confirm no regressions.
