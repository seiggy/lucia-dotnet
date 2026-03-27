---
updated_at: 2026-03-27T14:00:00Z
focus_area: Cascading Entity Resolution pipeline implementation
active_issues: []
---

# What We're Focused On

Implementing the cascading entity resolution pipeline — a 4-step deterministic fast-path for voice command entity/area matching. Targets <50ms resolution for 80%+ of commands, with graceful fallback to LLM for ambiguous/complex cases.

## Current Priority

**Cascading Entity Resolution Pipeline Implementation**

1. **Design Approved** — 4-step deterministic pipeline (Query Decomposition → Location Grounding → Domain Filtering → Entity Matching)
2. **User Directive** — Integrate speaker identity for possessive resolution ("my light", "my office")
3. **Performance Target** — <50ms p99 for cache-hit resolution
4. **Implementation Phase** — Feature flag, full test coverage, performance benchmarks
5. **Validation Phase** — Parallel execution telemetry, LLM fallback rate monitoring (<5% regression)
