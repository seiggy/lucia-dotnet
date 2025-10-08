# Agent Selection Feature

> Added: October 7, 2025
> Version: 1.1.0

## Overview

The Lucia integration now supports selecting which agent to use from the available agent catalog. This allows users to switch between different specialized agents (e.g., light-agent, music-agent) without reconfiguring the integration.

## How It Works

### 1. Agent Catalog Discovery

During integration setup, Lucia fetches the agent catalog from the `/agents` endpoint:

```json
[
  {
    "name": "light-agent",
    "description": "Agent for controlling lights and lighting in Home Assistant",
    "url": "/a2a/light-agent",
    "version": "1.0.0",
    ...
  },
  {
    "name": "music-agent", 
    "description": "Agent that orchestrates Music Assistant playback",
    "url": "/a2a/music-agent",
    "version": "1.0.0",
    ...
  }
]
```

The full catalog is stored in `hass.data[DOMAIN][entry_id]["catalog"]` for use in the options flow.

### 2. Options Configuration Screen

Users can configure agent selection in the integration options screen alongside other settings:

**Available Options:**
- **Agent Selection** (dropdown) - Choose which agent to use for conversations
- **System Prompt Template** - Custom prompt template for agent context
- **Max Response Tokens** - Maximum tokens in agent responses (10-4000)

### 3. Agent Selection Dropdown

The dropdown shows all available agents with their descriptions:

```
light-agent - Agent for controlling lights and lighting in Home Assistant
music-agent - Agent that orchestrates Music Assistant playback
```

**Default Behavior:**
- If no agent is selected, uses the first agent in the catalog
- If a selected agent is no longer available, falls back to first agent with a warning

### 4. Configuration Reload

When the user changes the selected agent:
1. Options are saved to the config entry
2. Integration automatically reloads (via update listener)
3. New agent is loaded from the catalog
4. Conversation platform uses the new agent URL

This provides seamless agent switching without manual integration reload.

## Code Changes

### const.py

Added new configuration constant:

```python
CONF_AGENT_NAME = "agent_name"  # Stores selected agent name
```

### config_flow.py

**New Imports:**
```python
from homeassistant.helpers.selector import (
    SelectSelector,
    SelectSelectorConfig, 
    SelectSelectorMode,
)
from .const import CONF_AGENT_NAME
```

**Updated `LuciaOptionsFlow.async_step_init()`:**
- Fetches agent catalog from `hass.data`
- Builds dropdown options with agent names and descriptions
- Shows agent selector as first option in config screen
- Handles case where no agents are available

### __init__.py

**Updated `async_setup_entry()`:**
- Checks `entry.options.get(CONF_AGENT_NAME)` for user selection
- Searches catalog for agent matching selected name
- Falls back to first agent if selection not found
- Logs selected agent for debugging

**Added Update Listener:**
```python
entry.async_on_unload(entry.add_update_listener(async_reload_entry))
```

This triggers `async_reload_entry()` when options change, ensuring the new agent selection takes effect immediately.

## User Experience

### Initial Setup

1. User adds Lucia integration
2. Enters repository URL and API key
3. Integration fetches agent catalog
4. First agent is used by default

### Changing Agents

1. Go to **Settings** → **Devices & Services** → **Lucia**
2. Click **Configure** on the integration
3. Select different agent from **Agent Selection** dropdown
4. Click **Submit**
5. Integration reloads with new agent
6. Next conversation uses the selected agent

### Configuration Screen

```
┌─────────────────────────────────────────────┐
│ Configure Lucia                             │
├─────────────────────────────────────────────┤
│ Agent Selection                             │
│ ┌─────────────────────────────────────────┐ │
│ │ light-agent - Agent for controlling ... ▼│ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ System Prompt Template                      │
│ ┌─────────────────────────────────────────┐ │
│ │ HOME ASSISTANT CONTEXT:                 │ │
│ │ ...                                     │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ Max Response Tokens                         │
│ ┌─────┐                                     │
│ │ 150 │                                     │
│ └─────┘                                     │
│                                             │
│           [Cancel]  [Submit]                │
└─────────────────────────────────────────────┘
```

## Technical Details

### Agent Resolution Flow

```
Options Changed
     ↓
Update Listener Triggered
     ↓
async_reload_entry()
     ↓
async_unload_entry() - Cleanup old agent
     ↓
async_setup_entry() - Setup new agent
     ↓
Fetch Agent Catalog
     ↓
Get CONF_AGENT_NAME from options
     ↓
Search catalog for matching agent
     ↓
Update hass.data with new agent_url
     ↓
Conversation platform uses new agent
```

### Data Storage

```python
# Entry Options (user configurable)
entry.options = {
    "agent_name": "music-agent",
    "prompt": "...",
    "max_tokens": 150
}

# Runtime Data (managed by integration)
hass.data[DOMAIN][entry_id] = {
    "httpx_client": AsyncClient(...),
    "agent_card": {...},
    "agent_url": "https://localhost:7235/a2a/music-agent",
    "catalog": [...],
    "repository": "https://localhost:7235"
}
```

## Future Enhancements

### Planned Features

1. **Agent Health Monitoring**
   - Show agent status (online/offline) in dropdown
   - Disable unavailable agents
   - Auto-fallback to healthy agent

2. **Multi-Agent Routing**
   - Allow selecting multiple agents
   - Route requests based on intent
   - Enable agent collaboration

3. **Agent Capabilities Display**
   - Show agent skills in UI
   - Display supported domains
   - Example queries per agent

4. **Custom Agent Priority**
   - Set preferred agent order
   - Automatic fallback chain
   - Load balancing support

5. **Agent Discovery Refresh**
   - Manual catalog refresh button
   - Periodic auto-refresh
   - New agent notifications

## Troubleshooting

### Agent Not Appearing in Dropdown

**Possible Causes:**
- Agent catalog not fetched during setup
- Integration loaded before agents started
- Network/connection issues

**Solutions:**
1. Reload the integration
2. Check agent repository is accessible
3. Review Home Assistant logs for errors

### Selected Agent Not Working

**Possible Causes:**
- Agent removed from catalog
- Agent URL changed
- Agent crashed/offline

**Behavior:**
- Integration falls back to first agent
- Warning logged to Home Assistant
- Conversation continues with fallback agent

**Solutions:**
1. Check agent is running and accessible
2. Re-select the agent in options
3. Choose a different agent from dropdown

### Options Not Saving

**Possible Causes:**
- Config entry not writable
- Invalid agent name
- Integration reload failure

**Solutions:**
1. Check Home Assistant logs for errors
2. Restart Home Assistant
3. Reconfigure integration from scratch

## API Reference

### Configuration Constants

```python
CONF_AGENT_NAME = "agent_name"    # Selected agent name (string)
CONF_PROMPT = "prompt"            # System prompt template (string)
CONF_MAX_TOKENS = "max_tokens"    # Max response tokens (int, 10-4000)
```

### hass.data Structure

```python
{
    DOMAIN: {
        entry_id: {
            "httpx_client": httpx.AsyncClient,
            "agent_card": dict,           # Selected agent's card
            "agent_url": str,              # Full agent URL
            "catalog": list[dict],         # All available agents
            "repository": str              # Base repository URL
        }
    }
}
```

### Agent Card Structure

```python
{
    "name": "light-agent",
    "description": "Agent for controlling lights...",
    "url": "/a2a/light-agent",
    "version": "1.0.0",
    "protocolVersion": "0.3.0",
    "capabilities": {...},
    "skills": [...]
}
```
