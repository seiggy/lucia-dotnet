---
name: summarize-eval-results
description: "Summarize eval test results from the lucia agent evaluation suite into compact markdown. Parses DiskBasedReportingConfiguration JSON output and produces a metrics table, pass/fail summary, tool call chains, and failed scenario details."
config:
  tool: dotnet-script
  script: scripts/Summarize-EvalResults.csx
  results_path: "%TEMP%/lucia-eval-reports/results"
---

# Eval Results Summary Skill

Parses the JSON output from `Microsoft.Extensions.AI.Evaluation.Reporting` (DiskBasedReportingConfiguration) and produces a compact markdown report for analysis.

## When to Use

- After running `dotnet test` on any eval test suite (Light, Music, Orchestrator)
- When asked to analyze, review, or summarize eval/test results
- Before and after making agent prompt changes to compare results
- Instead of manually reading large JSON files from the results directory

## How to Run

**Latest execution (most common):**

```powershell
cd D:\github\seiggy\lucia-dotnet
dotnet script .copilot/skills/summarize-eval-results/scripts/Summarize-EvalResults.csx
```

**Specific execution by ID:**

```powershell
dotnet script .copilot/skills/summarize-eval-results/scripts/Summarize-EvalResults.csx -- 20260218T182408
```

## Report Contents

1. **Summary line** — total scenarios, pass/fail counts
2. **Metrics table** — every scenario × model with all metric scores and pass/fail icons
3. **Failed scenario details** — for each failure:
   - User prompt
   - Tool call chain (e.g. `FindLight → SetLightState`)
   - Agent response preview (truncated to 200 chars)
   - Which metrics failed with rating and reason

## Metric Reference

| Metric | Type | Range | Description |
|--------|------|-------|-------------|
| Latency | Custom | 1-5 | Response time score (informational, never fails assertions) |
| Relevance | LLM Judge | 1-5 | Is the response relevant to the question? |
| Coherence | LLM Judge | 1-5 | Is the response well-structured and clear? |
| Task Adherence | LLM Judge | 1-5 | Did the agent accomplish what was asked? |
| Tool Call Accuracy | Built-in | True/False | Did the agent call the right tools? |
| A2A.Routing | Custom | 1-5 | Did the orchestrator produce a valid routing decision? |
| A2A.AgentTargeting | Custom | 1-5 | Were the expected agents selected? |
| A2A.AgentExecution | Custom | 1-5 | Did dispatched agents execute successfully? |
| A2A.Aggregation | Custom | 1-5 | Was a non-empty aggregated response produced? |
| A2A.Workflow | Custom | 1-5 | Composite score of all A2A sub-dimensions |

Scores **< 4** are flagged as failed by test assertions (except Latency which is informational only).

## Results Location

Results are stored by the eval SDK at:
```
%TEMP%/lucia-eval-reports/results/<execution-id>/<scenario-name>/1.json
```

Each `1.json` contains: `scenarioName`, `messages` (input), `modelResponse` (tool calls + final text), `evaluationResult.metrics`, `chatDetails.turnDetails`, and `tags`.
