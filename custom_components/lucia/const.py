"""Constants for Lucia A2A integration."""
from logging import Logger, getLogger

_LOGGER: Logger = getLogger(__package__)

DOMAIN = "lucia"
NAME = "Lucia A2A Agent"

# Configuration
CONF_REPOSITORY = "repository"
CONF_API_KEY = "api_key"
CONF_AGENT_ID = "agent_id"
CONF_AGENT_NAME = "agent_name"
CONF_PROMPT = "prompt"
CONF_MAX_TOKENS = "max_tokens"
CONF_TEMPERATURE = "temperature"
CONF_TOP_P = "top_p"
UNIQUE_ID = "unique_id"

# Defaults
DEFAULT_NAME = "Lucia A2A Agent"
DEFAULT_MAX_TOKENS = 150
DEFAULT_TEMPERATURE = 0.5
DEFAULT_TOP_P = 1.0
DEFAULT_PROMPT = """HOME ASSISTANT CONTEXT:

REQUEST_CONTEXT:
{
  "timestamp": "{{ now().strftime('%Y-%m-%d %H:%M:%S') }}",
  "day_of_week": "{{ now().strftime('%A') }}",
  "location": "{{ ha_name }}",
  "device": {
    "id": "{{ device_id }}",
    "area": "{{ device_area }}",
    "type": "{{ device_type }}",
    "capabilities": {{ device_capabilities | tojson }}
  }
}

The user is requesting assistance with their Home Assistant-controlled smart home. Use the entity IDs above to reference specific devices when delegating to specialized agents. Consider the current time and device states when planning actions."""

# A2A Agent Constants
A2A_WELL_KNOWN_PATH = "/.well-known/agent.json"
A2A_EXTENDED_PATH = "/api/agent"
