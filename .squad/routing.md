# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, design, code review | Ripley | Eval framework design, coverage planning, review PRs |
| Eval infrastructure, engine, metrics | Dallas | Base classes, fixtures, adapters, harness engine |
| Data pipelines, GitHub issues, traces | Ash | Issue ingestion, trace→scenario conversion, datasets |
| Eval scenarios, test suites, coverage | Lambert | Write eval tests, YAML datasets, edge cases |
| Dashboard UI, React components, frontend | Kane | Pages, components, API client, Tailwind, Vite |
| Backend APIs, orchestration, agents, data layer | Parker | AgentHost, A2AHost, agent system, plugins, data providers |
| Voice pipeline, STT, TTS, Wyoming | Brett | Audio pipeline, wake word, VAD, model management |
| Deployment, containers, CI/CD | Hicks | Docker, K8s, Helm, systemd, GitHub Actions |
| HA integration, Python component, entity matching | Bishop | Custom component, .NET HA client, entity visibility |
| Scope & priorities | Ripley | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Ripley |
| `squad:ripley` | Architecture/review work | Ripley |
| `squad:dallas` | Eval infrastructure work | Dallas |
| `squad:ash` | Data pipeline work | Ash |
| `squad:lambert` | Eval scenario/test work | Lambert |
| `squad:kane` | Dashboard/frontend work | Kane |
| `squad:parker` | Backend/platform work | Parker |
| `squad:brett` | Voice/speech work | Brett |
| `squad:hicks` | DevOps/infrastructure work | Hicks |
| `squad:bishop` | HA integration work | Bishop |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Ripley** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Ripley's review.

## Domain Keywords

| Keywords | Route To |
|----------|----------|
| "design", "architecture", "review", "coverage matrix", "plan" | Ripley |
| "eval infrastructure", "base class", "fixture", "metrics", "harness", "adapter", "factory" | Dallas |
| "data pipeline", "GitHub issues", "traces", "dataset", "ingestion", "export" | Ash |
| "test suite", "scenarios", "YAML", "eval tests", "edge cases", "STT variants" | Lambert |
| "dashboard", "UI", "React", "component", "page", "frontend", "Tailwind", "Vite" | Kane |
| "API", "endpoint", "backend", "agent", "orchestration", "plugin", "data layer", "A2A" | Parker |
| "voice", "speech", "STT", "TTS", "Wyoming", "wake word", "audio", "VAD", "diarization" | Brett |
| "Docker", "Kubernetes", "Helm", "deploy", "CI/CD", "container", "systemd", "infra" | Hicks |
| "Home Assistant", "HA", "custom component", "entity matching", "conversation platform", "Python" | Bishop |

## Multi-Agent Patterns

| Pattern | Agents | Notes |
|---------|--------|-------|
| New eval suite for an agent | Lambert (scenarios) + Dallas (if infra needed) | Lambert writes tests, Dallas supports |
| New data source integration | Ash (pipeline) + Lambert (scenarios from data) | Ash ingests, Lambert converts to tests |
| "Team" or broad eval work | Ripley + Dallas + Lambert | Full eval coverage push |
| Dashboard feature + API | Kane (frontend) + Parker (backend) | Full-stack feature |
| Voice feature | Brett (pipeline) + Parker (API integration) | Voice + backend |
| HA integration change | Bishop (both sides) + Parker (if API changes) | Integration + backend |
| Deployment change | Hicks (infra) + Parker (if app changes needed) | Infra + app |
| New agent type | Parker (implementation) + Dallas (eval infra) + Lambert (eval suite) | Full agent lifecycle |

## Agent-Specific Routes

| Agent Name Mentioned | Route To |
|---------------------|----------|
| "Ripley" | Ripley |
| "Dallas" | Dallas |
| "Ash" | Ash |
| "Lambert" | Lambert |
| "Kane" | Kane |
| "Parker" | Parker |
| "Brett" | Brett |
| "Hicks" | Hicks |
| "Bishop" | Bishop |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Ripley handles all `squad` (base label) triage.
