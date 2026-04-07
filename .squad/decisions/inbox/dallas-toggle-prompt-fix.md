# Decision: LightAgent Toggle Prompt Fix

**Author:** Dallas (Eval Engineer)  
**Date:** 2025-07-23  
**Status:** Implemented & Verified

## Context

Gemma 4 (5.1B Q4_K_M via Ollama, `gemma4:e2b`) failed 2/28 LightAgent eval tests — both toggle variants (`ControlLight_ToggleBedroom_CallsControlLightsForBedroom`). The model interpreted "toggle" as requiring a state check first, called `GetLightsState`, but never followed through with a `ControlLights` call.

## Root Cause

The system prompt's MANDATORY RULES listed "turn on/off, dim, color" as control requests but omitted "toggle." The `ControlLights` tool only accepts `state: "on"` or `"off"` — there is no "toggle" value. Without explicit guidance, the 5.1B model had no clear path to resolve toggle → direct control call.

## Decision

Added toggle guidance to the LightAgent system prompt in two places:

1. **Rule 2** — expanded the control request list to include "toggle": `"For control requests (turn on/off, toggle, dim, color)"`
2. **New Rule 5** — explicit toggle resolution: call `ControlLights` directly with state `"on"` when the current state is unknown; the tool does not support `"toggle"` as a state value.

## Rationale

- Keeps toggle consistent with the existing rule 2 pattern (don't call GetLightsState first)
- Gives small models a deterministic, unambiguous instruction
- No test changes required — the toggle test already accepts either "on" or "off"
- Defaulting to "on" is the safe choice: if the user says "toggle" and we don't know the state, turning on is less disruptive than turning off

## Verification

Ran full LightAgent eval suite against `gemma4:e2b`: **28/28 passed**, including both previously-failing toggle variants. Both resolved toggle → `ControlLights(state: "on")` as instructed.
