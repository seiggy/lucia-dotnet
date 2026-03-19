#!/usr/bin/env bash
set -e

# Read HA add-on options
HA_URL=$(bashio::config 'ha_url' 2>/dev/null || echo "${HA_URL:-}")
HA_TOKEN=$(bashio::config 'ha_token' 2>/dev/null || echo "${HA_TOKEN:-}")

# Export as environment variables for the .NET app
export HomeAssistant__BaseUrl="${HA_URL}"
export HomeAssistant__AccessToken="${HA_TOKEN}"
export DataProvider__Cache="InMemory"
export DataProvider__Store="SQLite"
export DataProvider__SqlitePath="/data/lucia.db"
export Deployment__Mode="standalone"

exec dotnet /app/lucia.AgentHost.dll
