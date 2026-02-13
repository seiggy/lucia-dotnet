# Diagram Plan

> Created: 2025-10-02
> Audience: Solution architects, backend engineers, Home Assistant integrators

## Goals

- Explain how Home Assistant and the Lucia multi-agent platform interact at a system level.
- Highlight the core .NET containers and supporting libraries that compose the Lucia platform.
- Capture the end-to-end request choreography from a Home Assistant conversation to the specialized agents.
- Illustrate the runtime deployment topology for a privacy-first home lab setup with optional cloud services.

## Planned Diagrams

1. **System Context** – Show primary actors, boundaries, and external dependencies.
2. **Container View** – Detail the Lucia solution projects, supporting libraries, and key integrations.
3. **Conversation Flow (Sequence)** – Document the runtime steps for processing a user request.
4. **Deployment Topology** – Depict on-prem/lab versus cloud placement, plus optional data services.

## Key Assumptions

- Home Assistant invokes Lucia through the custom `conversation` integration and REST APIs.
- Agents are orchestrated via Semantic Kernel hosted within the `lucia-dotnet` service and auxiliary hosts.
- External LLM providers are optional and can be swapped for local models without topology changes.
- Persistent storage (PostgreSQL/Redis) is planned but not yet part of the minimal deployment.

## References

- `.docs/product/mission.md`
- `.docs/product/roadmap.md`
- `.docs/product/tech-stack.md`
- `lucia-dotnet/Program.cs`
- `lucia.HomeAssistant/Extensions/ServiceCollectionExtensions.cs`
- `custom_components/lucia/`
