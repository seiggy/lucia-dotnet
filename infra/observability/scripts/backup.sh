#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
destination="${1:-./backups/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$destination"
destination="$(realpath "$destination")"

mapfile -t running_services < <(docker compose ps --services --filter status=running)
mapfile -t volumes < <(docker compose config --volumes)

restore_services() {
    if ((${#running_services[@]} > 0)); then
        docker compose start "${running_services[@]}"
    fi
}

if ((${#running_services[@]} > 0)); then
    docker compose stop "${running_services[@]}"
fi
trap restore_services EXIT

for volume in "${volumes[@]}"; do
    docker run --rm \
        -v "lucia-observability_${volume}:/source:ro" \
        -v "${destination}:/backup" \
        alpine:3.22.1 tar -C /source -czf "/backup/${volume}.tar.gz" .
done

cp compose.yaml "$destination/"
cp -R config "$destination/"
chmod -R go-rwx "$destination"
echo "Backup written to ${destination}"