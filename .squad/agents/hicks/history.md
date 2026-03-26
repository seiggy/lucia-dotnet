# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** Docker, Kubernetes, Helm, systemd, GitHub Actions, .NET Aspire 13
- **Created:** 2026-03-26

## Key Systems I Own

- `infra/` — Full deployment stack
- Dockerfiles: 9 variants (agenthost, voice-gpu, voice-cpu, voice-rocm, HA, timer-agent, music-agent, a2ahost, assets)
- Docker Compose: standard + voice + sidecar configs
- Kubernetes: raw manifests + Helm chart (Redis, MongoDB, A2A deployments)
- systemd: lucia.service + install.sh
- Aspire AppHost: lucia.AppHost/ (dev orchestration)

## Deployment Modes

- **Standalone:** All agents in one AgentHost process
- **Mesh:** Separate A2A agent processes (timer-agent, music-agent via A2AHost)

## Learnings

<!-- Append new learnings below. -->
