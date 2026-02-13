# Lucia Home Assistant Integration

This custom component integrates the Lucia AI agent system with Home Assistant, providing natural language conversation capabilities powered by the A2A (Agent-to-Agent) protocol.

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

### HACS Installation (Coming Soon)

This integration will be available through HACS in the future.

## Configuration

### Initial Setup

When adding the integration, you'll need to provide:

- **Agent Repository URL**: The URL of your Lucia agent (e.g., `http://localhost:5211`)
- **API Key**: The API key for authenticating with your Lucia agent

### Options

After setup, you can configure:

- **System Prompt Template**: Customize the system prompt using Home Assistant template syntax
- **Maximum Response Tokens**: Control the maximum length of agent responses (10-4000 tokens)

## Usage

### Conversation Agent

Once configured, Lucia appears as a conversation agent in Home Assistant. You can:

1. Select Lucia as your preferred conversation agent in Assist settings
2. Use voice commands through Home Assistant Assist
3. Type messages in the Assist chat interface

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

## Development

This integration is part of the larger Lucia project. For development information:

- [Main Repository](https://github.com/seiggy/lucia-dotnet)
- [Issue Tracker](https://github.com/seiggy/lucia-dotnet/issues)

## License

This project is licensed under the MIT License - see the LICENSE file for details.