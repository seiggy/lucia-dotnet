#!/bin/sh
set -eu

for endpoint in \
    http://otel-collector:13133/ \
    http://grafana:3000/api/health \
    http://loki:3100/ready \
    http://prometheus:9090/-/ready \
    http://tempo:3200/ready; do
    wget --quiet --spider --timeout=5 "$endpoint"
done

usage="$(df -P /data/collector | awk 'NR == 2 {gsub(/%/, "", $5); print $5}')"
test "$usage" -lt 90