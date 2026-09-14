#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f .env ]]; then
    set -a
    source .env
    set +a
fi

: "${OTLP_HOSTNAME:?Set OTLP_HOSTNAME}"
: "${OTLP_USERNAME:?Set OTLP_USERNAME}"
: "${OTLP_PASSWORD:?Set OTLP_PASSWORD}"
: "${MANAGEMENT_BIND_ADDRESS:?Set MANAGEMENT_BIND_ADDRESS}"
: "${GRAFANA_ADMIN_USER:?Set GRAFANA_ADMIN_USER}"
: "${GRAFANA_ADMIN_PASSWORD:?Set GRAFANA_ADMIN_PASSWORD}"

endpoint="https://${OTLP_HOSTNAME}:4318"
grafana="https://${OTLP_HOSTNAME}:3000"
trace_id="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
span_id="$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
service="lucia-observability-smoke"
metric_name="process_runtime_dotnet_gc_collections_count"
dashboard_uids=(lucia-speech-pipeline lucia-service-health lucia-jetson-host lucia-postgresql lucia-redis)
now_ns="$(($(date +%s) * 1000000000))"
end_ns="$((now_ns + 1000000))"
auth="Authorization: Basic $(printf '%s:%s' "$OTLP_USERNAME" "$OTLP_PASSWORD" | base64 -w0)"
curl_args=(--fail --silent --show-error --connect-timeout 5 --max-time 10)

echo "WAIT: Grafana"
for _ in {1..15}; do
    if curl "${curl_args[@]}" --cacert secrets/tls.crt -u "${GRAFANA_ADMIN_USER}:${GRAFANA_ADMIN_PASSWORD}" "${grafana}/api/health" >/dev/null 2>&1; then
        echo "PASS: Grafana"
        break
    fi
    sleep 2
done

curl "${curl_args[@]}" --cacert secrets/tls.crt -u "${GRAFANA_ADMIN_USER}:${GRAFANA_ADMIN_PASSWORD}" "${grafana}/api/health" >/dev/null

post_otlp() {
    local signal="$1"
    local payload="$2"
    curl "${curl_args[@]}" --cacert secrets/tls.crt \
        -H "$auth" -H 'Content-Type: application/json' \
        --data "$payload" "${endpoint}/v1/${signal}" >/dev/null
    echo "SENT: ${signal}"
}

post_otlp traces "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"${service}\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"smoke\"},\"spans\":[{\"traceId\":\"${trace_id}\",\"spanId\":\"${span_id}\",\"name\":\"observability-smoke-${trace_id}\",\"kind\":2,\"startTimeUnixNano\":\"${now_ns}\",\"endTimeUnixNano\":\"${end_ns}\"}]}]}]}"
post_otlp metrics "{\"resourceMetrics\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"${service}\"}}]},\"scopeMetrics\":[{\"scope\":{\"name\":\"smoke\"},\"metrics\":[{\"name\":\"process.runtime.dotnet.gc.collections.count\",\"gauge\":{\"dataPoints\":[{\"attributes\":[{\"key\":\"smoke_id\",\"value\":{\"stringValue\":\"${trace_id}\"}}],\"timeUnixNano\":\"${now_ns}\",\"asInt\":\"1\"}]}}]}]}]}"
post_otlp logs "{\"resourceLogs\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"${service}\"}}]},\"scopeLogs\":[{\"scope\":{\"name\":\"smoke\"},\"logRecords\":[{\"timeUnixNano\":\"${now_ns}\",\"severityNumber\":9,\"severityText\":\"INFO\",\"body\":{\"stringValue\":\"lucia observability smoke log ${trace_id}\"},\"traceId\":\"${trace_id}\",\"spanId\":\"${span_id}\"}]}]}]}"

retry_query() {
    local name="$1"
    local url="$2"
    local expected="$3"
    local response

    echo "WAIT: ${name}"
    for _ in {1..15}; do
        response="$(curl "${curl_args[@]}" --cacert secrets/tls.crt -u "${GRAFANA_ADMIN_USER}:${GRAFANA_ADMIN_PASSWORD}" "$url" || true)"
        if grep -q "$expected" <<<"$response"; then
            echo "PASS: ${name}"
            return 0
        fi
        sleep 2
    done

    echo "FAIL: ${name}" >&2
    return 1
}

retry_query trace "${grafana}/api/datasources/proxy/uid/tempo/api/traces/${trace_id}" "observability-smoke-${trace_id}"
retry_query metric "${grafana}/api/datasources/proxy/uid/prometheus/api/v1/query?query=${metric_name}%7Bsmoke_id%3D%22${trace_id}%22%7D" "${trace_id}"
retry_query log "${grafana}/api/datasources/proxy/uid/loki/loki/api/v1/query_range?query=%7Bservice_name%3D%22${service}%22%7D%20%7C%20trace_id%3D%22${trace_id}%22" "lucia observability smoke log ${trace_id}"
retry_query "Jetson host metric" "${grafana}/api/datasources/proxy/uid/prometheus/api/v1/query?query=system_uptime_seconds%7Bdevice_id%3D%22orin-voice%22%7D" '"device_id":"orin-voice"'
retry_query "PostgreSQL metric" "${grafana}/api/datasources/proxy/uid/prometheus/api/v1/query?query=pg_up%7Bdevice_id%3D%22orin-voice%22%7D" '"value":\['
retry_query "Redis metric" "${grafana}/api/datasources/proxy/uid/prometheus/api/v1/query?query=redis_up%7Bdevice_id%3D%22orin-voice%22%7D" '"value":\['
for dashboard_uid in "${dashboard_uids[@]}"; do
    retry_query "dashboard ${dashboard_uid}" "${grafana}/api/dashboards/uid/${dashboard_uid}" "\"uid\":\"${dashboard_uid}\""
done