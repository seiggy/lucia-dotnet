#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dockerfile="${1:-$script_dir/../../docker/Dockerfile.agenthost-jetson-voice}"

awk '
    found && /^FROM / { exit }
    { print }
    /^FROM scratch AS appliance-voice-assets$/ { found = 1 }
    END { if (!found) exit 1 }
' "$dockerfile" | sha256sum | cut -d' ' -f1
