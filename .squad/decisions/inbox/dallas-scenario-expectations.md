# Decision: Relax eval expectations + add speaker context to LightAgent

**Date:** 2025-07-17
**Author:** Dallas (Eval Engineer)
**Requested by:** Zack Way

## Context

Three `light-agent.yaml` eval scenarios fail against `gemma4:e2b` — not because the model is wrong, but because the test expectations are too strict or miss prompt guidance.

## Decisions

### 1. Relaxed searchTerms in query scenarios (`query_kitchen_state_on`, `query_kitchen_state_off`)

Changed `contains:kitchen light` → `contains:kitchen`.

**Rationale:** The model extracts `["kitchen"]` as the search term rather than `["kitchen light"]`. This is valid — the tool still matches the correct entities. The `turn_on_already_on` scenario already uses `contains:kitchen` and the xUnit tests use `AssertArgumentContains("searchTerms", "kitchen")`. Aligning the YAML scenarios avoids penalizing models for reasonable term extraction.

### 2. Added speaker context rule to LightAgent system prompt (`speaker_context_identity`)

Added rule #6 under a new `## Speaker context` heading instructing the agent to reflect speaker identity from context metadata.

**Rationale (Option A chosen):** The EvalRunner injects `[Speaker: X | Device Area: Y]` into prompts, but the LightAgent prompt had no guidance on using that metadata for identity questions. Without it, models correctly stay in character as "Light Control Agent" when asked "Who am I speaking to?" — which is a valid interpretation. Adding the rule makes speaker awareness an explicit part of the agent contract rather than an implicit assumption.

**Alternatives considered:**
- Option B (change prompt to "What is my name?") — avoids prompt change but hides ambiguity
- Option C (remove scenario) — loses coverage of a real capability
