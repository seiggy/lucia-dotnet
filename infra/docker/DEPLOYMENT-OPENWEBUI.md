# Lucia Alongside Open Web UI and Remote Home Assistant

This guide covers deploying Lucia on the **same host** as an existing Open Web UI Docker Compose stack, with Home Assistant running on a **different machine** (e.g. 192.168.1.198 or home-assist.dunn.local). No changes are made to the Open Web UI compose; Lucia runs as a separate compose project and reaches shared services via `host.docker.internal`.

## Prerequisites

- Existing Open Web UI stack (Ollama, optional: SearXNG, ComfyUI, Whisper, Piper, MetaMCP) on Machine A
- Home Assistant on Machine B (e.g. 192.168.1.198 or behind VIRTUAL_HOST)
- Docker and Docker Compose on Machine A

## Quick Start

```bash
cd lucia-dotnet/infra/docker
cp .env.lucia.example .env
# Edit .env: set HomeAssistant__AccessToken and optionally HomeAssistant__BaseUrl
./deploy-lucia.sh
```

Then open http://localhost:7233 and complete the setup wizard (or use headless setup below). On Home Assistant, add the Lucia integration and set **Agent Repository URL** to `http://<machine-A-ip>:7233`.

### Headless setup (skip wizard)

Add to `.env` and restart Lucia:

```
DASHBOARD_API_KEY=lk_YourGeneratedKey   # Log in to Lucia dashboard
LUCIA_HA_API_KEY=lk_YourGeneratedKey    # Enter in HA integration → Lucia → API Key
```

With `HomeAssistant__BaseUrl`, `HomeAssistant__AccessToken`, and `DASHBOARD_API_KEY` set, Lucia auto-configures on startup and skips the wizard. Use `LUCIA_HA_API_KEY` when adding the Lucia integration in HA.

## Shared Capabilities (Machine A)

Both Home Assistant and Lucia can use these services. Ports are host bindings from your Open Web UI stack.

| Capability           | Service         | Host port    | Used by HA                        | Used by Lucia                                                                 |
|----------------------|-----------------|-------------|-----------------------------------|-------------------------------------------------------------------------------|
| **LLM**              | Ollama          | 11434       | —                                 | Yes (via host.docker.internal; primary inference)                              |
| **Web search**       | SearXNG         | 8081        | Optional (Assist / custom)       | Via MCP or custom agent tool (http://host.docker.internal:8081)              |
| **Image generation** | ComfyUI         | 8188        | Optional (custom)                 | Via MCP or custom agent tool (http://host.docker.internal:8188)              |
| **STT**              | Wyoming Whisper | 10300       | Yes (Assist — Wyoming protocol)   | Lucia returns text; HA does STT for voice input                               |
| **STT (HTTP)**       | Whisper ASR     | 8083        | Optional                          | Future / custom tool                                                          |
| **TTS**              | Wyoming Piper   | 8084 (10200)| Yes (Assist — Wyoming protocol)   | Lucia returns text; HA does TTS for voice output                              |
| **TTS (HTTP)**       | Piper bridge    | 8085        | Optional                          | Future / custom tool                                                          |
| **MCP tools**        | MetaMCP         | 12008       | —                                 | Optional (register in Lucia dashboard)                                        |

- **Home Assistant:** Configure Assist to use Wyoming Whisper (port 10300 on Machine A) and Wyoming Piper (8084). Lucia only returns text; HA handles STT/TTS for voice.
- **Lucia:** Uses Ollama via `host.docker.internal:11434`. For web search, image gen, or TTS/STT, use MCP (e.g. MetaMCP at `http://host.docker.internal:12008`) or custom agents; see `.env.lucia.example` for commented capability URLs.

## Compose Files

- **Base:** `docker-compose.yml` — Lucia, Redis, MongoDB
- **Sidecar:** `docker-compose.lucia-sidecar.yml` — Adds `extra_hosts` and optional env for HA + Ollama

Use both:

```bash
docker compose -f docker-compose.yml -f docker-compose.lucia-sidecar.yml --env-file .env up -d
```

Or use the script:

```bash
./deploy-lucia.sh
```

## Environment

Copy and edit the sidecar env template:

```bash
cp .env.lucia.example .env
```

Set at least:

- `HomeAssistant__BaseUrl` — e.g. `http://192.168.1.198:8123` or `http://home-assist.dunn.local:8123`
- `HomeAssistant__AccessToken` — Long-lived token from HA (Profile → Long-Lived Access Tokens)
- `ConnectionStrings__chat-model` — Ollama: `Endpoint=http://host.docker.internal:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama`

Optional capability URLs (for MCP or future use) are in `.env.lucia.example` as comments.

## Home Assistant Configuration

1. On the HA host (192.168.1.198 or via your VIRTUAL_HOST):
   - Install the Lucia integration (HACS: add repo `https://github.com/seiggy/lucia-dotnet`, or copy `custom_components/lucia`).
   - Add integration → **Agent Repository URL** = `http://<machine-A-ip>:7233` (the host where Lucia runs).
   - Use the same long-lived token in Lucia (in `.env` or via setup wizard).
   - Set Lucia as the conversation/Assist backend if desired.
2. Keep STT/TTS in Assist pointing at Wyoming Whisper (10300) and Wyoming Piper (8084) on Machine A; Lucia only provides text.

## Setup: MetaMCP and Fixing "No Agents"

Lucia shows **no agents** until both are done:
1. **Setup wizard completed** (Home Assistant URL + token, and at least one chat provider like Ollama)
2. **At least one chat model provider** configured (Ollama is seeded when `ConnectionStrings__chat-model` is set; otherwise add one in the wizard)

### Step 1 — Complete the setup wizard

1. Open `http://<machine-A-ip>:7233` (Lucia dashboard).
2. If prompted, complete the setup wizard:
   - **Home Assistant URL** — e.g. `http://192.168.1.198:8123`
   - **Access token** — Long-lived token from HA (Profile → Long-Lived Access Tokens)
   - **Chat model** — Add Ollama; or rely on `ConnectionStrings__chat-model` in `.env` and restart Lucia.
3. Restart Lucia after updating `.env`:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.lucia-sidecar.yml --env-file .env restart lucia
   ```
4. Verify: **Agents** should list built-in agents (General Assistant, Light Controller, Climate Controller, Orchestrator, Scene Agent, etc.).

### Step 2 — Add MetaMCP server

**Option A (automatic):** Set `METAMCP_URL=http://host.docker.internal:12008` in `.env` and restart Lucia. Lucia seeds MetaMCP on startup when this is set.

**Option B (manual):** In Lucia dashboard → **MCP Servers** (or **Settings → MCP Servers**):
1. **Add server**:
   - **Id / Name:** `metamcp` (or any label)
   - **Transport:** `http` or `sse` (MetaMCP typically supports both)
   - **URL:** `http://host.docker.internal:12008/metamcp/openwebui-api/sse` (full SSE endpoint)
   - **Headers:** `Authorization: Bearer sk_mt_...` (API key from MetaMCP → API Keys)
3. **Enable** and click **Connect**.
4. After connection, the server’s tools/resources will be available for dynamic agents.

### Step 3 — Create dynamic agents using MetaMCP tools

1. **Agent Definitions** → **Add agent**.
2. Name it (e.g. "Meta Tools Agent").
3. Assign tools from the MetaMCP server (search, image gen, etc.).
4. Choose a chat model provider (Ollama) and save.
5. The new agent will appear in the catalog and be routable via the Orchestrator.

### Web search (SearXNG)

Lucia has native SearXNG support. Set `SEARXNG_URL=http://host.docker.internal:8081` in `.env` and restart. The **General Agent** will automatically get a `web_search` tool for current events, news, and facts.

### Other resources (ComfyUI, Whisper, Piper)

These are not built into Lucia’s core. Use them via:

- **MetaMCP** — If MetaMCP exposes tools for ComfyUI, Whisper, Piper, etc., assign those tools to a dynamic agent.
- **Custom MCP servers** — Add other MCP servers in **MCP Servers** that wrap these services.
- **Custom agent tools** — Future Lucia support for env-based tool URLs (not yet implemented; see `.env.lucia.example` for commented URLs).

## Network

- Port **7233** on Machine A must be reachable from the Home Assistant host so the Lucia integration can call the Agent Repository.
- Redis and MongoDB in the Lucia stack stay localhost-bound; no need to expose them to HA.

## See Also

- [DEPLOYMENT.md](DEPLOYMENT.md) — General Docker deployment, configuration, and troubleshooting
- [README](../../README.md) — Lucia overview and architecture
