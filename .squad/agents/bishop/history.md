# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** Python 3.x (aiohttp), .NET 10/C# 14, Home Assistant REST/WebSocket APIs
- **Created:** 2026-03-26

## Key Systems I Own

- `custom_components/lucia/` — HA custom component
  - `__init__.py` — integration setup
  - `config_flow.py` — UI config flow
  - `conversation.py` — conversation platform
  - `fast_conversation.py` — fast-path conversation
  - `conversation_tracker.py` — multi-turn context mapping
  - `a2a_payload.py` — A2A protocol payloads
  - `tests/` — component tests
- `lucia.HomeAssistant/` — .NET HA client
  - `IHomeAssistantClient` — full HA API surface (states, services, events, history, calendars, registry, media, todo, etc.)
  - `HomeAssistantOptions` — config with SSL toggle and bearer auth
  - Typed models for entities, states, services, areas, devices

## HA Integration Points

- Conversation API: HA → lucia `/api/conversation` (API key auth)
- Service: `lucia.send_message`
- Events: `lucia_conversation_input_required` (follow-up input)
- Entity registries: area, entity, device (for entity matching)
- Entity visibility/exposure filtering
- Test snapshots: `scripts/Export-HomeAssistantSnapshot.ps1`

## Learnings

<!-- Append new learnings below. -->
