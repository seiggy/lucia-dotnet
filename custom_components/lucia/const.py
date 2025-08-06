"""Constants for Lucia A2A integration."""
from logging import Logger, getLogger

_LOGGER: Logger = getLogger(__package__)

DOMAIN = "lucia"
NAME = "Lucia A2A Agent"

# Configuration
CONF_REPOSITORY = "repository"
CONF_API_KEY = "api_key"
CONF_AGENT_ID = "agent_id"
CONF_PROMPT = "prompt"
CONF_MAX_TOKENS = "max_tokens"
CONF_TEMPERATURE = "temperature"
CONF_TOP_P = "top_p"

# Defaults
DEFAULT_NAME = "Lucia A2A Agent"
DEFAULT_MAX_TOKENS = 150
DEFAULT_TEMPERATURE = 0.5
DEFAULT_TOP_P = 1.0
DEFAULT_PROMPT = """This smart home is controlled by Home Assistant.
Respond naturally to the user's request, and if applicable, suggest actions that can be performed by Home Assistant."""

# A2A Agent Constants
A2A_WELL_KNOWN_PATH = "/.well-known/agent.json"
A2A_EXTENDED_PATH = "/api/agent"