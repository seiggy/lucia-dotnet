"""Constants for Lucia integration."""
from logging import Logger, getLogger

_LOGGER: Logger = getLogger(__package__)

DOMAIN = "lucia"
NAME = "Lucia Home Agent"

# Configuration
CONF_REPOSITORY = "repository"
CONF_API_KEY = "api_key"
CONF_VERIFY_SSL = "verify_ssl"
CONF_PROMPT_OVERRIDE = "prompt_override"
UNIQUE_ID = "unique_id"

# Legacy keys kept for migration compat
CONF_AGENT_ID = "agent_id"
CONF_PROMPT = "prompt"

# Defaults
DEFAULT_NAME = "Lucia Home Agent"
