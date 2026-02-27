# Lucia Home Assistant Integration

This custom component integrates the Lucia AI agent system with Home Assistant, providing natural language conversation capabilities over Lucia's A2A/JSON-RPC message interface.

## Features

- **Natural Language Processing**: Interact with your smart home using natural language
- **Multi-Agent Support**: Connect to multiple Lucia agents for different capabilities
- **Conversation Integration**: Fully integrated with Home Assistant's conversation system
- **Customizable Prompts**: Configure system prompts to customize agent behavior
- **Real-time Communication**: WebSocket support for real-time agent responses

## Installation

### Manual Installation

1. Copy the `lucia` folder to your Home Assistant's `custom_components` directory
2. Restart Home Assistant
3. Go to Settings → Devices & Services → Add Integration
4. Search for "Lucia Home Agent" and follow the setup flow

### HACS Installation

Install via HACS as a custom repository:

1. In Home Assistant, open HACS → Integrations
2. Open the menu (⋮) → Custom repositories
3. Add `https://github.com/seiggy/lucia-dotnet` as category **Integration**
4. Install **Lucia** and restart Home Assistant

## Configuration

### Initial Setup

Start Lucia with AppHost first (recommended):

```bash
dotnet build lucia-dotnet.slnx
dotnet run --project lucia.AppHost
```

When adding the integration, you'll need to provide:

- **Agent Repository URL**: The base URL exposing Lucia's `/agents` catalog (for example, the URL shown for `lucia-agenthost` in the Aspire dashboard)
- **API Key**: The API key for authenticating with your Lucia agent

### Options

After setup, you can configure:

- **System Prompt Template**: Customize the system prompt using Home Assistant template syntax
- **Maximum Response Tokens**: Control the maximum length of agent responses (10-4000 tokens)

## Usage

### Integration shows "1 device and 1 entity"

This is normal. The integration creates **one device** (Lucia) and **one conversation entity**. The name shown next to it (e.g. **orchestrator** or **light-agent**) is the **agent currently selected** for that integration:

- **orchestrator** — Full assistant: routes your requests to the right specialist (lights, music, timer, etc.). Use this for voice and general conversation.
- **light-agent**, **general-assistant**, etc. — Single-purpose agents. Only that agent’s skills are available.

The integration defaults to the **orchestrator** when possible. If you see only a specialist (e.g. light-agent), reload the integration after the Lucia backend has registered the orchestrator, or choose the orchestrator in the integration options if your setup supports agent selection.

### Making Lucia your voice assistant

1. Go to **Settings → Voice assistants** and open your pipeline (e.g. "Default" or "Home Assistant").
2. Set **Conversation agent** to **Lucia Home Agent** (or the name of your Lucia conversation entity).
3. Save. Voice and Assist chat will then use Lucia.

If voice fails (no response, error, or timeout):

- **Use the orchestrator** — Ensure the integration is using the **orchestrator** agent (see above). If it picked "light-agent" or another specialist, reload the integration or change the agent in options so the orchestrator is used.
- **Reachability** — Home Assistant must be able to reach the **Agent Repository URL** (and the agent URL derived from it). If Lucia runs on another host, use that host’s IP or hostname and the correct port (e.g. `http://192.168.1.100:7233`).
- **Logs** — Check **Settings → System → Logs** (and the Lucia container/server logs) for connection errors, timeouts, or 401/500 responses.

### Conversation Agent

Once configured, Lucia appears as a conversation agent in Home Assistant. You can:

1. Select Lucia as your preferred conversation agent in Assist settings (see "Making Lucia your voice assistant" above)
2. Use voice commands through Home Assistant Assist
3. Type messages in the Assist chat interface
4. Change selected agents in integration options without re-adding the integration

### Services

The integration provides the following service:

#### `lucia.send_message`

Send a message directly to the Lucia agent.

**Service data:**
- `message` (required): The message to send
- `agent_id` (optional): Specific agent ID to target

**Example:**
```yaml
service: lucia.send_message
data:
  message: "Turn on the living room lights"
  agent_id: "lucia"
```

## Supported Languages

The integration supports all languages that your Lucia agent system is configured to handle.

## Troubleshooting

### "Requirements for lucia not found: a2a-sdk>=0.3.0" or 500 when adding the integration

Home Assistant installs the integration's Python dependency (`a2a-sdk`) from PyPI when you first add the integration. If that install fails, you get a 500 or `RequirementsNotFound` and the config flow never loads.

**What to do:**

1. **Check network** – The Home Assistant host (or container) must be able to reach the internet (e.g. `files.pythonhosted.org`) to download packages. If HA runs in Docker, ensure the container has outbound access; if behind a proxy, ensure pip/HA can use it.
2. **Check the full log** – In **Settings → System → Logs**, look for the line *before* `RequirementsNotFound`; there is often a pip or SSL error that explains the failure.
3. **Manual install (advanced)** – If you have shell access to the same Python environment Home Assistant uses (e.g. in the HA container or venv), you can try installing the dependency yourself, then add the integration again:
   ```bash
   pip install "a2a-sdk>=0.3.0"
   ```
   Restart Home Assistant after installing.

### Connection Issues

If you're having trouble connecting to your Lucia agent:

1. Verify the agent is running and accessible
2. Check the URL format (include protocol and port)
3. Confirm the API key is correct
4. Check Home Assistant logs for detailed error messages

### Agent Not Responding

If the agent connects but doesn't respond properly:

1. Check the Lucia agent logs for errors
2. Verify the agent card is properly configured
3. Ensure the A2A protocol is correctly implemented
4. Confirm `/agents` is reachable from Home Assistant and returns a catalog

## Development

This integration is part of the larger Lucia project. For development information:

- [Main Repository](https://github.com/seiggy/lucia-dotnet)
- [Issue Tracker](https://github.com/seiggy/lucia-dotnet/issues)

## License

This project is licensed under the MIT License - see the LICENSE file for details.