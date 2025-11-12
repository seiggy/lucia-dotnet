# Copilot Agent Guide

Welcome! This guide explains how GitHub Copilot agents should operate inside the `lucia-dotnet` repository. Read it fully before making changes so you can follow the house rules, leverage the documentation, and ship updates safely.

## 1. Product & Repository Snapshot

- **Mission:** Lucia delivers a privacy-first, multi-agent assistant that orchestrates Home Assistant automations locally using [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/).
- **Primary Projects:**
  - `lucia.AgentHost` – ASP.NET Core Web API hosting the orchestrated AI agents and A2A integration.
  - `lucia.AppHost` – .NET Aspire host for service discovery, local orchestration, and observability.
  - `lucia.ServiceDefaults` – Shared resilience, telemetry, and health-check extensions.
  - `lucia.HomeAssistant` / `lucia.HomeAssistant.SourceGenerator` - Home Assistant API w/ Source Generator
  - `custom_components/lucia` – Python custom component for Home Assistant integration.
  - `lucia.Agents` – Domain-specific agent implementations, skills, and registry support.
  - `lucia.Tests` - XUnit tests for the app

## 2. What lives in `.docs/`

The `.docs` directory is the single source of truth for product and engineering direction. Copilot agents must reference it before writing code or documentation.

```
.docs/
├─ architecture/
│  └─ 2025-10-02-lucia-platform-architecture/
│     ├─ diagram-plan.md            (rationale for diagrams)
│     ├─ diagram-index.md           (Mermaid previews + links)
│     ├─ integration.md             (where to link diagrams in other docs)
│     └─ diagrams/*.mmd             (Mermaid sources – use validator & preview tools)
├─ product/
│  ├─ mission.md                    (mission, personas, differentiators)
│  ├─ roadmap.md                    (phase progress, upcoming priorities)
│  ├─ tech-stack.md                 (authoritative stack + versions)
│  └─ decisions.md                  (decision log – overrides conflicting guidance)
├─ reports/                         (status reports & historical summaries)
└─ specs/
  ├─ 2025-01-07-multi-agent-orchestration/
  │  ├─ feature-request.md         (high-level need & use cases)
  │  ├─ spec.md                    (spec requirements document)
  │  ├─ tech-specs.md              (technical design + diagrams)
  │  ├─ tasks.md                   (implementation task list)
  │  └─ sub-specs/                 (api-spec.md, tests.md, etc.)
  └─ 2025-08-06-home-assistant-conversation-plugin/
    └─ ...                        (earlier completed spec for reference)
```

## 3. Updated Tech Stack Overview (2025-08-06)

- **Backend:** ASP.NET Core Web API (.NET 10 / C# 13) orchestrated with .NET Aspire 9.5.1.
- **AI Runtime:** Microsoft Agent Framework public preview, Custom Regex|SLM|LLM multi-agent orchestration patterns, OpenAI GPT-4o (primary), Gemini & Claude optional, LLaMa/local models planned.
- **Data & State:** In-memory defaults with planned PostgreSQL 17+, Redis 7.x for task persistence (per latest spec), configuration via ASP.NET Core config + Secrets/K8s secrets.
- **Home Assistant Integration:** REST + Conversation + LLM APIs today, WebSocket streaming upcoming; Python custom component built on aiohttp 3.x.
- **Infrastructure:** Docker containers, Kubernetes deployment target, optional Istio service mesh; Observability powered by OpenTelemetry (traces/metrics/logging).
- **Testing:** xUnit + FakeItEasy; Aspire.Hosting.Testing for integration. Use `dotnet test` from repo root or target projects explicitly.

Refer to `.docs/product/tech-stack.md` for deeper detail and version updates before modifying dependencies.

## 4. Required Workflows for Copilot Agents

1. **Check existing tasks:** Use the `todo-md` MCP tool to review or add work items in `tasks.md` for the active spec. Keep todo status in sync with progress.
2. **Read the spec:** For feature work, open the active spec folder in `.docs/specs/…` and confirm requirements, technical notes, and testing expectations.
3. **Follow instruction files:**
  - Planning/spec creation → `.github/prompts/create-spec.prompt.md` and `.github/instructions/create-spec.instructions.md`
  - Implementation → `.github/instructions/execute-tasks.instructions.md`
  - Architecture diagrams → `.github/instructions/architecture-diagrams.instructions.md`
4. **Use required tools:**
  - `context7` for third-party library documentation.
  - `microsoft.docs` for Microsoft stack references.
  - `mermaid-diagram-validator` (and preview) for every Mermaid edit.
  - `todo-md` to record progress and blocking items.
5. **TDD expectation:** Write or update tests before implementing public behavior changes. Never merge failing tests.
6. **Telemetry alignment:** Maintain OpenTelemetry span/metric naming consistent with existing conventions when adding instrumentation.

## 5. Development Quick Reference

- **Restore & Build:** `dotnet restore`, `dotnet build lucia-dotnet.sln`
- **Run AppHost:** `dotnet run --project lucia.AppHost`
- **Run Tests:** `dotnet test` (or target a project like `dotnet test lucia.Tests`)
- **Python component:** Lives under `custom_components/lucia`; follow Home Assistant custom component guidelines when editing.

Always confirm commands in the repo root PowerShell environment before execution. Document any manual steps you perform in the completion summary.

## 6. Constitutional Governance

**All development must comply with the [Lucia Constitution](.specify/memory/constitution.md)**, which establishes the authoritative governance framework for the project.

### Non-Negotiable Principles

The constitution defines five core principles that MUST be followed:

1. **One Class Per File** - Each `.cs` file contains exactly one class definition
2. **Test-First Development (TDD)** - Tests written and verified to fail before implementation
3. **Documentation-First Research** - Context7/Microsoft.docs MUST be used before coding with libraries/APIs
4. **Privacy-First Architecture** - Local processing by default, cloud services optional
5. **Observability & Telemetry** - OpenTelemetry instrumentation for all agents and services

Review the full constitution before making changes to understand governance rules, enforcement mechanisms, and quality standards.

## 7. Do's & Don'ts for AI Agents

### ✅ Do’s

- **Read the spec and decision log first** so changes align with current priorities and architectural direction.
- **Update the todo list** (`todo-md`) before starting work and as you progress through tasks.
- **Reference `.docs/product/tech-stack.md`** when touching dependencies to ensure versions remain aligned.
- **Validate Mermaid diagrams** with the provided validator before committing diagram changes.
- **Use Context7/Microsoft Docs tooling** prior to coding against third-party or Microsoft APIs.
- **Write tests and run `dotnet test`** (or relevant suites) after making executable code changes.
- **Document assumptions** and call out blockers in the completion summary when requirements are unclear.
- **Respect feature flags and configuration** defaults described in specs when adding new functionality.

### ❌ Don’ts

- **Don’t bypass instruction files** (`create-spec`, `execute-tasks`, architecture rules) even for minor tweaks.
- **Don’t edit `.docs/product/decisions.md`** unless you have explicit approval to log a new decision.
- **Don’t introduce new dependencies** without updating the tech stack documentation and explaining the rationale.
- **Don’t skip todo status updates**—work is considered incomplete if the task tracker is stale.
- **Don’t leave Mermaid or markdown lint issues**; validate diagrams and keep docs consistent with templates.
- **Don’t push code without tests**—failing or missing tests block acceptance.
- **Don't remove or disable telemetry** without replacing it or documenting the change in the spec/tasks.

## 8. Need Help?

- Review `.docs/product/mission.md` and `roadmap.md` for context if a requirement seems ambiguous.
- Check prior specs in `.docs/specs/` for patterns to emulate.
- Surface open questions in the completion summary so maintainers can clarify next steps.

By following this guide, Copilot agents will stay aligned with Lucia’s roadmap, keep documentation authoritative, and ship reliable updates without surprises.

***IMPORTANT***: ONLY ONE CLASS PER FILE!!! NEVER PUT MORE THAN ONE CLASS IN A FILE !!!IMPORTANT!!!