# Copilot Agent Guide

Welcome! This guide explains how GitHub Copilot agents should operate inside the `lucia-dotnet` repository. Read it fully before making changes so you can follow the house rules, leverage the documentation, and ship updates safely.

## 1. Product & Repository Snapshot

- **Mission:** Lucia delivers a privacy-first, multi-agent assistant that orchestrates Home Assistant automations locally using [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/).
- **Primary Projects:**
  - `lucia.AppHost` – .NET Aspire host and preferred development entrypoint for orchestrating local services.
  - `lucia.AgentHost` – ASP.NET Core minimal API host for orchestrated AI agents.
  - `lucia.A2AHost` – ASP.NET Core minimal API host for A2A-facing endpoints and registry integration.
  - `lucia.ServiceDefaults` – Shared resilience, telemetry, and health-check extensions.
  - `lucia.HomeAssistant` – Home Assistant API client and integration layer.
  - `custom_components/lucia` – Python custom component for Home Assistant integration.
  - `lucia.Agents` – Domain-specific agent implementations, skills, and registry support.
  - `lucia.Tests` - XUnit tests for the app

## 2. Updated Tech Stack Overview (2025-08-06)

- **Backend:** ASP.NET Core Web API (.NET 10 / C# 14) orchestrated with .NET Aspire 13
- **AI Runtime:** Microsoft Agent Framework RC, Custom Regex|SLM|LLM multi-agent orchestration patterns, Azure OpenAI, Gemini, Claude, LLaMa, Open Router Provider support.
- **Data & State:** MongoDB 8, Redis 7.x for task persistence (per latest spec), configuration via ASP.NET Core config + Secrets/K8s secrets.
- **Home Assistant Integration:** REST + Conversation + LLM APIs today, WebSocket streaming upcoming; Python custom component built on aiohttp 3.x.
- **Infrastructure:** Docker containers, Kubernetes deployment target, optional Istio service mesh; Observability powered by OpenTelemetry (traces/metrics/logging).
- **Testing:** xUnit + FakeItEasy; Aspire.Hosting.Testing for integration. Use `dotnet test` from repo root or target projects explicitly.

Refer to `.docs/product/tech-stack.md` for deeper detail and version updates before modifying dependencies.

## 3. Required Workflows for Agents

**TDD expectation:** Write or update tests before implementing public behavior changes. Never merge failing tests.

## 4. Development Quick Reference

- **Restore & Build:** `dotnet restore`, `dotnet build lucia-dotnet.slnx`
- **Run AppHost:** `dotnet run --project lucia.AppHost`
- **Run AgentHost directly:** `dotnet run --project lucia.AgentHost`
- **Run A2AHost directly:** `dotnet run --project lucia.A2AHost`
- **Run Tests:** `dotnet test` (or target a project like `dotnet test lucia.Tests`)
- **Python component:** Lives under `custom_components/lucia`; follow Home Assistant custom component guidelines when editing.
- **Regenerate HA test snapshot:** `.\scripts\Export-HomeAssistantSnapshot.ps1 -Endpoint $env:HA_ENDPOINT -Token $env:HA_TOKEN`

## 5. Home Assistant Environment Variables

The following environment variables provide access to the user's Home Assistant instance for testing, snapshot export, and Jinja template validation:

| Variable       | Purpose                                                              |
| -------------- | -------------------------------------------------------------------- |
| `HA_ENDPOINT`  | Base URL of the Home Assistant instance (e.g. `http://homeassistant.local:8123`) |
| `HA_TOKEN`     | Long-lived access token (generated in HA → Profile → Long-Lived Access Tokens) |

Use these when you need to:
- Test Jinja templates against the real HA instance
- Regenerate the test fixture snapshot (`lucia.Tests/TestData/ha-snapshot.json`)
- Validate entity/area data during development

Always confirm commands in the repo root PowerShell environment before execution. Document any manual steps you perform in the completion summary.

## 6. Constitutional Governance

### Non-Negotiable Principles

The constitution defines five core principles that MUST be followed:

1. **One Class Per File** - Each `.cs` file contains exactly one class definition
2. **Test-First Development (TDD)** - Tests written and verified to fail before implementation
3. **Privacy-First Architecture** - Local processing by default, cloud services optional
4. **Observability & Telemetry** - OpenTelemetry instrumentation for all agents and services

Review the full constitution before making changes to understand governance rules, enforcement mechanisms, and quality standards.

## 7. Do's & Don'ts for AI Agents

### ✅ Do’s

- **Read the spec and decision log first** so changes align with current priorities and architectural direction.
- **Reference `.docs/product/tech-stack.md`** when touching dependencies to ensure versions remain aligned.
- **Write tests and run `dotnet test`** (or relevant suites) after making executable code changes.
- **Document assumptions** and call out blockers in the completion summary when requirements are unclear.
- **Respect feature flags and configuration** defaults described in specs when adding new functionality.

### ❌ Don’ts

- **Don’t introduce new dependencies** without updating the tech stack documentation and explaining the rationale.
- **Don’t skip todo status updates**—work is considered incomplete if the task tracker is stale.
- **Don’t leave Mermaid or markdown lint issues**; validate diagrams and keep docs consistent with templates.
- **Don’t push code without tests**—failing or missing tests block acceptance.
- **Don't remove or disable telemetry** without replacing it or documenting the change in the spec/tasks.

***IMPORTANT***: ONLY ONE CLASS PER FILE!!! NEVER PUT MORE THAN ONE CLASS IN A FILE !!!IMPORTANT!!!