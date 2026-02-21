# Integration Notes

> Created: 2025-10-02  
> Updated: 2025-02-20

Architecture documentation workflow: **Excalidraw files (`.excalidraw`) are the canonical editable source** for all architecture diagrams. No Mermaid mirrors are maintained.

## Link Targets
- `README.md`: Add an **Architecture** section linking to `@.docs/architecture/2025-10-02-lucia-platform-architecture/diagram-index.md`.
- `.docs/product/tech-stack.md`: Reference the container and deployment views for current runtime topology (`AgentHost`, `A2AHosts` for `music-agent` and `timer-agent`, `lucia-dashboard`, `Redis`, `MongoDB`).
- `.docs/specs/` (future Conversation or Agent specs): Reference the sequence flow to explain request choreography across `AgentHost` and `A2AHosts`.
- `custom_components/lucia/README.md`: Link to the system context for contributor onboarding.
- `lucia-dashboard/README.md`: Link to the system context and deployment topology for dashboard/runtime orientation.

## Diagram Files
| # | File | Purpose |
|---|------|---------|
| 1 | `01-system-context.excalidraw` | Actors, boundaries, external dependencies |
| 2 | `02-runtime-containers.excalidraw` | Aspire topology: hosts, libraries, infra |
| 3 | `03-orchestration-components.excalidraw` | Router → Dispatch → Aggregator pipeline |
| 4 | `04-conversation-sequence.excalidraw` | HA → AgentHost → A2AHost request flow |
| 5 | `05-deployment-topology.excalidraw` | Network layout, Aspire services, cloud |

## PR Checklist
- [ ] Add README navigation entry pointing to the diagram index.
- [ ] Cross-link relevant product docs and specs as noted above.
- [ ] Ensure Excalidraw diagrams remain updated as the canonical architecture assets.
- [ ] Confirm the audience and scope remain aligned with the latest roadmap before merging changes.
- [ ] Remove stale references to source-generator-centric architecture and planned PostgreSQL-only storage assumptions.
