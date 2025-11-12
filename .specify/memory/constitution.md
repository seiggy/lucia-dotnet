<!--
========================================
SYNC IMPACT REPORT
========================================
Version Change: Template → 1.0.0 (Initial Constitution)
Date: 2025-10-13

Modified Principles:
  - N/A (all new)

Added Sections:
  ✅ Core Principles (5 principles)
  ✅ Development Standards
  ✅ Agent Architecture Requirements
  ✅ Quality & Testing
  ✅ Governance

Removed Sections:
  - N/A (template conversion)

Templates Requiring Updates:
  ✅ plan-template.md - Constitution Check section aligns with defined principles
  ✅ spec-template.md - User stories and acceptance criteria align with testing principles
  ✅ tasks-template.md - TDD workflow aligns with Test-First Development principle
  ⚠️  AGENTS.md - Should reference constitution for governance (pending verification)

Follow-up TODOs:
  - ✅ Ratification date set to 2025-10-13
  - ✅ AGENTS.md updated to reference constitution as authoritative governance document

Commit Message Suggestion:
  docs: ratify constitution v1.0.0 (core principles + governance framework)
========================================
-->

# Lucia Constitution

## Core Principles

### I. One Class Per File (NON-NEGOTIABLE)

Every C# class MUST be defined in its own file with matching filename.

**Rules:**
- Each `.cs` file MUST contain exactly one class definition
- Filename MUST match the class name (e.g., `LightAgent.cs` contains `LightAgent` class)
- Nested classes, records, and interfaces within a class file are permitted only when they are private implementation details
- Partial classes MUST be split with clear naming convention (e.g., `Agent.Core.cs`, `Agent.Skills.cs`)

**Rationale:** This principle ensures code navigability, reduces merge conflicts, aligns with Microsoft coding standards, and supports IDE tooling for rapid file discovery. The multi-agent architecture requires clear boundaries, and one-class-per-file provides that structural clarity.

**Enforcement:** Code reviews MUST reject any PR containing multiple public classes in a single file. IDE analyzers SHOULD be configured to warn on violations.

---

### II. Test-First Development (NON-NEGOTIABLE)

All functional code changes MUST be preceded by corresponding failing tests.

**Rules:**
- Tests MUST be written and verified to fail BEFORE implementing the feature
- All tests MUST pass before code can be merged to main branches
- Test coverage is expected for all business logic, agent skills, and API contracts
- Use xUnit for unit tests, FakeItEasy for mocking, and Playwright for browser/UI tests
- Integration tests MUST be written for agent communication, Home Assistant API interactions, and multi-agent orchestration

**Rationale:** Test-First Development (TDD) ensures code correctness, provides living documentation, enables fearless refactoring, and catches regressions early. Given Lucia's distributed agent architecture and critical home automation use cases, comprehensive test coverage is essential for reliability.

**Enforcement:** Pull requests MUST include test additions/modifications. CI pipeline MUST run all tests and block merges on failure. Code reviewers MUST verify tests were written before implementation.

---

### III. Documentation-First Research (NON-NEGOTIABLE)

All third-party library usage and Microsoft API integrations MUST be researched using official documentation sources BEFORE implementation.

**Rules:**
- Context7 MCP tool MUST be used for ALL third-party library documentation before writing code
- Microsoft.docs MCP tool MUST be used for all Microsoft technology, SDK, and framework references
- Library/API patterns MUST match documented examples and best practices exactly
- Configuration and setup MUST align with official documentation
- No library code may be written without prior documentation reference and validation

**Rationale:** Using authoritative documentation prevents API misuse, ensures best practices, reduces bugs from incorrect assumptions, and maintains alignment with vendor recommendations. This is critical for the AI/ML integrations (Agent Framework, OpenAI, etc.) and Home Assistant APIs that power Lucia.

**Enforcement:** Code reviews MUST verify Context7/Microsoft.docs usage evidence. Implementation MUST reference specific documentation used. PRs MUST link to documentation sources for non-trivial integrations.

---

### IV. Privacy-First Architecture

All features MUST prioritize local processing and user data privacy by default.

**Rules:**
- User conversation history and home automation data MUST be processed locally when possible
- Cloud LLM providers (OpenAI, Gemini, Claude) are permitted but MUST be optional and configurable
- No user data may be transmitted to external services without explicit user configuration and consent
- Local LLM support (LLaMa, Ollama) MUST be provided as an alternative to cloud providers
- Telemetry and observability data MUST NOT include personally identifiable information (PII) or sensitive home automation details
- Home Assistant access tokens and API keys MUST be stored securely using ASP.NET Core Secrets or Kubernetes Secrets

**Rationale:** Privacy is a core differentiator for Lucia versus commercial assistants (Alexa, Google Home). Users choose Lucia specifically to maintain control over their data and avoid cloud dependencies. This principle must never be compromised.

**Enforcement:** Security reviews MUST verify no unintended data exfiltration. Architecture decisions MUST be evaluated for privacy implications. New features MUST provide local-first options.

---

### V. Observability & Telemetry

All services and agents MUST implement comprehensive observability using OpenTelemetry.

**Rules:**
- OpenTelemetry instrumentation MUST be included for all agents, services, and API endpoints
- Structured logging using Microsoft.Extensions.Logging MUST be used consistently
- Spans MUST be created for agent execution, LLM calls, Home Assistant API interactions, and multi-agent coordination
- Metrics MUST track request rates, response times, error rates, and resource usage
- Correlation IDs MUST be propagated across distributed agent calls for request tracing
- Log levels MUST be appropriate: Error for failures, Warning for degraded states, Information for key events, Debug for detailed diagnostics
- NO PII or sensitive data in logs or traces (see Privacy-First Architecture principle)

**Rationale:** Lucia's distributed multi-agent architecture requires comprehensive observability to diagnose issues, optimize performance, and understand system behavior. OpenTelemetry provides vendor-neutral telemetry that works with Prometheus, Grafana, Jaeger, and other tools commonly used in home lab Kubernetes deployments.

**Enforcement:** Code reviews MUST verify OpenTelemetry instrumentation for new agents and services. Telemetry gaps identified in production MUST be addressed promptly. Telemetry MUST be tested in integration tests.

---

## Development Standards

### Code Style & Language

- **Language**: C# 13 with nullable reference types enabled across all projects
- **Framework**: ASP.NET Core Web API on .NET 10
- **Standards**: Microsoft C# Coding Conventions (as per DEC-005)
- **Async/Await**: All I/O operations MUST use async/await patterns
- **Nullability**: Nullable reference types MUST be respected; use `?` for nullable types
- **Version Control**: Conventional Commits specification for all commit messages

### Code Review Requirements

- All changes MUST go through pull request review
- At least one approval required before merge
- PR description MUST include context, testing approach, and Context7/Microsoft.docs references (when applicable)
- Breaking changes MUST be called out explicitly in PR title and description

### Dependency Management

- New dependencies MUST be justified in PR description
- Version numbers MUST be explicit (no wildcards)
- `.docs/product/tech-stack.md` MUST be updated when adding/changing major dependencies
- Security vulnerabilities MUST be addressed within one sprint of discovery

---

## Agent Architecture Requirements

### A2A Protocol Compliance

- All agents MUST implement A2A Protocol v0.3.0 with JSON-RPC 2.0
- Agent cards MUST be registered with the central Agent Registry
- `taskId` MUST be set to `null` (Agent Framework limitation)
- `contextId` MUST be used for conversation threading and context preservation
- Message format MUST comply with A2A message schema

### Agent Implementation

- All agents MUST be registered using `AddAIAgent` in service configuration
- Agent skills MUST be implemented as separate classes in `lucia.Agents/Skills/`
- Each agent MUST provide an `AgentCard` describing capabilities, version, and endpoints
- Agent initialization MUST be async and handle failures gracefully
- Agents MUST implement proper cancellation token support

### Home Assistant Integration

- All Home Assistant API calls MUST use the strongly-typed `IHomeAssistantClient`
- WebSocket streaming support is planned but not yet required
- Home Assistant state changes MUST be cached appropriately to reduce API load
- Integration MUST respect Home Assistant rate limits and implement backoff strategies

---

## Quality & Testing

### Test Requirements

- **Unit Tests**: Required for all business logic, services, and agent skills
- **Integration Tests**: Required for agent communication, API contracts, and Home Assistant integration
- **Contract Tests**: Required for all A2A protocol endpoints and agent registry APIs
- **Browser Tests**: Use Playwright for any UI components (planned Management UI)

### Test Coverage Expectations

- Business logic: >80% coverage expected
- Agent skills and services: All public methods tested
- Edge cases and error handling: Explicit test cases required
- Integration tests: Critical user journeys and agent workflows covered

### Testing Tools

- **Unit Testing**: xUnit
- **Mocking**: FakeItEasy
- **Integration Testing**: Aspire.Hosting.Testing for service integration
- **Browser Testing**: Playwright (when applicable)

### Quality Gates

- All tests MUST pass before merge
- No decrease in test coverage without justification
- Integration tests MUST be included for agent additions or protocol changes
- Performance tests SHOULD be included for latency-sensitive operations

---

## Governance

This constitution is the authoritative governance document for Lucia development. It supersedes conflicting guidance in other documentation, IDE settings, or historical practices.

### Amendment Process

1. Proposed changes MUST be documented with rationale and impact analysis
2. Stakeholder approval required (Product Owner, Tech Lead)
3. Version number MUST be incremented according to semantic versioning:
   - **MAJOR**: Backward incompatible principle removal or redefinition
   - **MINOR**: New principle added or materially expanded guidance
   - **PATCH**: Clarifications, wording fixes, non-semantic refinements
4. Amendment date MUST be recorded in version line
5. Sync Impact Report MUST be updated as HTML comment at top of file

### Compliance & Enforcement

- All pull requests MUST be verified for constitutional compliance during code review
- Architecture Decision Records (ADRs) in `.docs/product/decisions.md` MUST align with these principles
- Violations MUST be justified and documented if unavoidable due to technical constraints
- Repeated violations without justification are grounds for PR rejection

### Runtime Development Guidance

For day-to-day development workflows, agent operation patterns, and detailed implementation guidance, refer to `AGENTS.md` in the repository root. The AGENTS.md file provides operational context while this constitution provides governance.

### Versioning & Review

Constitution changes are tracked using semantic versioning. Regular reviews (quarterly recommended) should assess if principles remain relevant as the project evolves.

---

**Version**: 1.0.0 | **Ratified**: 2025-10-13 | **Last Amended**: 2025-10-13