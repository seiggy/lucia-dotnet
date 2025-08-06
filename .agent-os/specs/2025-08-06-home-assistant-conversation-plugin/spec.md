# Spec Requirements Document

> Spec: Home Assistant Conversation Plugin
> Created: 2025-08-06
> Status: Planning

## Overview

Implement a Home Assistant custom component that integrates Lucia's AI agents as a conversation integration within the Assistant pipeline, enabling natural language control of home automation through the distributed agent system.

## User Stories

### Home Assistant User Configuration

As a Home Assistant user, I want to add Lucia as a conversation agent, so that I can use natural language to control my home through AI-powered agents.

The user will navigate to Settings → Devices & Services → Add Integration → Lucia. They'll be presented with a configuration flow where they enter the Lucia API endpoint URL and API key. The integration will verify connectivity, fetch the list of available agents from the registry, and allow the user to select which agent handles conversation requests. Once configured, Lucia appears as an available conversation agent in the Assistant settings.

### Natural Language Home Control

As a homeowner, I want to speak or type natural commands to control my home, so that I don't need to remember specific device names or commands.

When the user says "turn on the living room lights" through any Home Assistant interface (voice assistant, chat, or mobile app), the request is sent to the configured Lucia agent. The agent processes the natural language, understands the intent, and executes the appropriate actions through the Home Assistant API. The user receives a natural language response confirming the action.

### Multi-Turn Conversations

As a user, I want to have contextual conversations with my assistant, so that I can refine commands or ask follow-up questions.

The user initiates a conversation like "what's the temperature in the bedroom?" and receives a response. They can then follow up with "make it warmer" without repeating the room context. The plugin maintains the conversation ID throughout the exchange, allowing the Lucia agents to understand context and provide intelligent responses.

## Spec Scope

1. **Config Flow Implementation** - User interface for adding and configuring the Lucia integration with API endpoint and agent selection
2. **Conversation Entity** - Home Assistant conversation entity that processes natural language through Lucia agents
3. **A2A Protocol Client** - Python implementation of the A2A protocol for agent communication
4. **Error Handling & Notifications** - Robust error handling with persistent notifications for connection issues
5. **Agent Registry Integration** - Fetch and validate available agents from the Lucia registry

## Out of Scope

- Local conversation history storage (relies on Home Assistant's ChatLog)
- OAuth/JWT authentication (using simple API key for now)
- Agent health monitoring UI (backend only)
- Voice/speech processing (handled by Home Assistant)

## Expected Deliverable

1. A functional Home Assistant custom component installable via HACS or manual installation that appears in the integrations list
2. Users can successfully configure the Lucia API endpoint, authenticate, and select an orchestration agent
3. Natural language commands sent through Home Assistant's conversation interface are processed by Lucia agents and return appropriate responses