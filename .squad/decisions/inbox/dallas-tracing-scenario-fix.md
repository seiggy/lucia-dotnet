# Decision: Auto-enable conversation tracing for scenario evaluation

**Date:** 2025-07-18
**Author:** Dallas (Eval Engineer)
**Status:** Implemented

## Context

All scenario-based eval tests reported "Expected N tool call(s) but only got 0" across every model. The root cause: `ScenarioValidator.ValidateToolCalls` reads tool calls from `agentInstance.Tracer?.Turns`, but the `ConversationTracer` is only created when `RealAgentFactory.EnableTracing == true`. Tracing defaulted to `false` in the TUI, making the conversation list empty and every scenario silently fail tool call validation.

## Decision

1. **Auto-enable tracing** in `Program.cs` for the standard agent eval flow. The TUI no longer asks — tracing is always on because scenario datasets are the primary evaluation mode and structurally depend on the tracer for tool call validation.

2. **Add a guard** in `EvalRunner.EvaluateScenariosAsync` that throws `InvalidOperationException` if the tracer is null and any scenario has `ExpectedToolCalls`. This prevents silent false-failures if someone calls the method directly without tracing.

## Trade-offs

- Tracing adds minor overhead per LLM call (records messages in memory). Acceptable for an eval harness.
- Users lose the opt-out for tracing in standard eval. Justified because the alternative was silently wrong results.
- The guard in `EvaluateScenariosAsync` is a fail-fast safety net — better to crash loudly than report bogus scores.

## Files Changed

- `lucia.EvalHarness/Program.cs` — replaced tracing prompt with auto-enable + info message
- `lucia.EvalHarness/Evaluation/EvalRunner.cs` — added tracer-null guard at top of `EvaluateScenariosAsync`
