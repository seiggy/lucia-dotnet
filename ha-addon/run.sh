#!/usr/bin/env bash
set -e

# Load bashio if available (Home Assistant add-on environment)
if [ -f /usr/lib/bashio/bashio.sh ]; then
    # shellcheck disable=SC1091
    . /usr/lib/bashio/bashio.sh
fi

# Read HA add-on options (bashio when available, env vars as fallback)
if type bashio::config >/dev/null 2>&1; then
    HA_URL=$(bashio::config 'ha_url' 2>/dev/null || echo "${HA_URL:-}")
    HA_TOKEN=$(bashio::config 'ha_token' 2>/dev/null || echo "${HA_TOKEN:-}")
else
    HA_URL="${HA_URL:-}"
    HA_TOKEN="${HA_TOKEN:-}"
fi

# Export as environment variables for the .NET app
export HomeAssistant__BaseUrl="${HA_URL}"
export HomeAssistant__AccessToken="${HA_TOKEN}"
export DataProvider__Cache="InMemory"
export DataProvider__Store="SQLite"
export DataProvider__SqlitePath="/data/lucia.db"
export Deployment__Mode="standalone"

exec dotnet /app/lucia.AgentHost.dll
