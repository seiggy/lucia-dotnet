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

HOME_STATE:
{
  "areas": {{ areas() | tojson }},
  "entities": {
    {%- set ns = namespace(first=true) %}
    {%- for entity in exposed_entities %}
    {%- set domain = entity.entity_id.split('.')[0] %}
    {%- if not ns.first %},{% endif %}
    "{{ entity.entity_id }}": {
      "name": "{{ entity.name }}",
      "state": "{{ entity.state }}",
      "domain": "{{ domain }}",
      {%- if entity.aliases %}
      "aliases": {{ entity.aliases | tojson }},
      {%- endif %}
      {%- if domain == 'light' %}
      "type": "light",
      {%- if entity.attributes.brightness is defined %}
      "brightness": {{ entity.attributes.brightness }},
      {%- endif %}
      {%- if entity.attributes.color_temp is defined %}
      "color_temp": {{ entity.attributes.color_temp }},
      {%- endif %}
      {%- if entity.attributes.rgb_color is defined %}
      "rgb_color": {{ entity.attributes.rgb_color | tojson }},
      {%- endif %}
      {%- elif domain == 'climate' %}
      "type": "climate",
      {%- if entity.attributes.current_temperature is defined %}
      "current_temperature": {{ entity.attributes.current_temperature }},
      {%- endif %}
      {%- if entity.attributes.temperature is defined %}
      "target_temperature": {{ entity.attributes.temperature }},
      {%- endif %}
      {%- if entity.attributes.humidity is defined %}
      "humidity": {{ entity.attributes.humidity }},
      {%- endif %}
      {%- if entity.attributes.hvac_modes is defined %}
      "hvac_modes": {{ entity.attributes.hvac_modes | tojson }},
      {%- endif %}
      {%- elif domain == 'cover' %}
      "type": "cover",
      {%- if entity.attributes.current_position is defined %}
      "position": {{ entity.attributes.current_position }},
      {%- endif %}
      {%- elif domain == 'media_player' %}
      "type": "media_player",
      {%- if entity.attributes.volume_level is defined %}
      "volume": {{ (entity.attributes.volume_level * 100) | int }},
      {%- endif %}
      {%- if entity.attributes.source is defined %}
      "source": "{{ entity.attributes.source }}",
      {%- endif %}
      {%- if entity.attributes.media_title is defined %}
      "media_title": "{{ entity.attributes.media_title }}",
      {%- endif %}
      {%- elif domain == 'fan' %}
      "type": "fan",
      {%- if entity.attributes.percentage is defined %}
      "speed": {{ entity.attributes.percentage }},
      {%- endif %}
      {%- elif domain == 'timer' %}
      "type": "timer",
      {%- if entity.attributes.remaining is defined %}
      "remaining": "{{ entity.attributes.remaining }}",
      {%- endif %}
      {%- elif domain == 'binary_sensor' %}
      "type": "binary_sensor",
      {%- if 'motion' in entity.entity_id or 'occupancy' in entity.entity_id %}
      "sensor_type": "motion",
      {%- elif 'door' in entity.entity_id or 'window' in entity.entity_id %}
      "sensor_type": "door_window",
      {%- endif %}
      {%- elif domain == 'sensor' %}
      "type": "sensor",
      {%- if 'temperature' in entity.entity_id %}
      "sensor_type": "temperature",
      {%- elif 'humidity' in entity.entity_id %}
      "sensor_type": "humidity",
      {%- elif 'battery' in entity.entity_id %}
      "sensor_type": "battery",
      {%- endif %}
      {%- if entity.attributes.unit_of_measurement is defined %}
      "unit": "{{ entity.attributes.unit_of_measurement }}",
      {%- endif %}
      {%- elif domain == 'weather' %}
      "type": "weather",
      {%- if entity.attributes.temperature is defined %}
      "temperature": {{ entity.attributes.temperature }},
      {%- endif %}
      {%- if entity.attributes.humidity is defined %}
      "humidity": {{ entity.attributes.humidity }},
      {%- endif %}
      {%- else %}
      "type": "{{ domain }}",
      {%- endif %}
      "area": "{{ entity_area(entity.entity_id) | default('unknown') }}"
    }
    {%- set ns.first = false %}
    {%- endfor %}
  }
}

The user is requesting assistance with their Home Assistant-controlled smart home. Use the entity IDs above to reference specific devices when delegating to specialized agents. Consider the current time and device states when planning actions."""

# A2A Agent Constants
A2A_WELL_KNOWN_PATH = "/.well-known/agent.json"
A2A_EXTENDED_PATH = "/api/agent"
