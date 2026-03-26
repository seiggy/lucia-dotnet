---
updated_at: 2026-03-26T16:57:00Z
focus_area: Evaluation library development
active_issues: []
---

# What We're Focused On

Building a comprehensive evaluation library for all lucia system agents. Key priorities:

1. **Expand eval coverage** — ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent all need eval suites
2. **GitHub issue ingestion** — Pull user-reported issues to build real-world eval scenarios
3. **Trace→eval conversion** — Use captured conversation traces as eval test data
4. **Real LLMs** — All evals run against Zack's local Ollama deployment with real models
5. **Robust infrastructure** — Make adding new eval suites trivial via shared base classes
