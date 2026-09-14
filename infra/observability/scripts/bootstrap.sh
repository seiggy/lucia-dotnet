#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if [[ ! -f .env ]]; then
    cp .env.example .env
    chmod 600 .env
    echo "Created .env. Set the management-network addresses and credentials, then rerun this script." >&2
    exit 1
fi

set -a
source .env
set +a

: "${MANAGEMENT_BIND_ADDRESS:?Set MANAGEMENT_BIND_ADDRESS in .env}"
: "${OTLP_HOSTNAME:?Set OTLP_HOSTNAME in .env}"
: "${OTLP_USERNAME:?Set OTLP_USERNAME in .env}"
: "${OTLP_PASSWORD:?Set OTLP_PASSWORD in .env}"
: "${OTLP_PASSWORD_HASH:?Set OTLP_PASSWORD_HASH in .env}"
: "${GRAFANA_ADMIN_PASSWORD:?Set GRAFANA_ADMIN_PASSWORD in .env}"

case "$MANAGEMENT_BIND_ADDRESS" in
    127.*|10.*|192.168.*|100.6[4-9].*|100.[7-9][0-9].*|100.1[01][0-9].*|100.12[0-7].*|::1|fc*|fd*) ;;
    172.*)
        second_octet="${MANAGEMENT_BIND_ADDRESS#172.}"
        second_octet="${second_octet%%.*}"
        if ((second_octet < 16 || second_octet > 31)); then
            echo "MANAGEMENT_BIND_ADDRESS must be a private management or VPN address." >&2
            exit 1
        fi
        ;;
    *)
        echo "MANAGEMENT_BIND_ADDRESS must be a private management or VPN address." >&2
        exit 1
        ;;
esac

if ! ip -o address show | grep -Fq " ${MANAGEMENT_BIND_ADDRESS}/"; then
    echo "MANAGEMENT_BIND_ADDRESS is not assigned to this host." >&2
    exit 1
fi

for value in "$OTLP_PASSWORD" "$OTLP_PASSWORD_HASH" "$GRAFANA_ADMIN_PASSWORD"; do
    if [[ "$value" == replace-* || "$value" == *replace_with* ]]; then
        echo "Replace every placeholder credential in .env before deployment." >&2
        exit 1
    fi
done

mkdir -p secrets
chmod 700 secrets

if [[ ! -s secrets/tls.crt || ! -s secrets/tls.key ]]; then
    openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
        -keyout secrets/tls.key \
        -out secrets/tls.crt \
        -subj "/CN=${OTLP_HOSTNAME}" \
        -addext "subjectAltName=DNS:${OTLP_HOSTNAME}"
    chmod 600 secrets/tls.key
fi

docker compose config --quiet
docker compose up -d --wait
docker compose ps