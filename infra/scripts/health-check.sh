#!/usr/bin/env bash

# Lucia Health Check Script
# Comprehensive health checking for all deployment methods
# 
# Usage:
#   ./health-check.sh                          # Check all services
#   ./health-check.sh --service lucia          # Check specific service
#   ./health-check.sh --service redis          # Check Redis only
#   ./health-check.sh --verbose                # Show detailed output
#   ./health-check.sh --config /path/to/.env   # Use specific config file
#
# Exit codes:
#   0 = All healthy
#   1 = At least one service unhealthy
#   2 = Configuration error

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
VERBOSE=false
SERVICE_FILTER=""
CONFIG_FILE=".env"
LUCIA_PORT="${LUCIA_PORT:-5000}"
LUCIA_HOST="${LUCIA_HOST:-localhost}"
HOMEASSISTANT_URL="${HOMEASSISTANT_URL:-}"
HOMEASSISTANT_ACCESS_TOKEN="${HOMEASSISTANT_ACCESS_TOKEN:-}"
REDIS_CONNECTION_STRING="${REDIS_CONNECTION_STRING:-redis://localhost:6379}"
CHECK_TIMEOUT=5

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --service)
            SERVICE_FILTER="$2"
            shift 2
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --config)
            CONFIG_FILE="$2"
            shift 2
            ;;
        --timeout)
            CHECK_TIMEOUT="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 2
            ;;
    esac
done

# Load configuration
load_config() {
    if [[ -f "$CONFIG_FILE" ]]; then
        if $VERBOSE; then
            echo -e "${BLUE}Loading configuration from $CONFIG_FILE${NC}"
        fi
        # Source the config file, but don't fail if variables are undefined
        set +u
        source "$CONFIG_FILE" 2>/dev/null || true
        set -u
    fi
}

# Logging functions
log_info() {
    echo -e "${BLUE}ℹ${NC} $*"
}

log_success() {
    echo -e "${GREEN}✓${NC} $*"
}

log_warning() {
    echo -e "${YELLOW}⚠${NC} $*"
}

log_error() {
    echo -e "${RED}✗${NC} $*"
}

log_verbose() {
    if $VERBOSE; then
        echo -e "${BLUE}→${NC} $*"
    fi
}

# Health check functions
check_lucia_api() {
    log_info "Checking Lucia API (http://$LUCIA_HOST:$LUCIA_PORT)"
    
    local url="http://$LUCIA_HOST:$LUCIA_PORT/health"
    log_verbose "Endpoint: $url"
    
    if response=$(curl -s -w "\n%{http_code}" --max-time $CHECK_TIMEOUT "$url" 2>/dev/null); then
        local body=$(echo "$response" | head -n -1)
        local http_code=$(echo "$response" | tail -n 1)
        
        if [[ "$http_code" == "200" ]]; then
            log_success "Lucia API responding (HTTP $http_code)"
            if $VERBOSE; then
                echo "Response: $body"
            fi
            return 0
        else
            log_error "Lucia API returned HTTP $http_code"
            if $VERBOSE; then
                echo "Response: $body"
            fi
            return 1
        fi
    else
        log_error "Lucia API not responding (connection refused or timeout)"
        return 1
    fi
}

check_redis() {
    log_info "Checking Redis ($REDIS_CONNECTION_STRING)"
    
    # Parse connection string
    local redis_host="localhost"
    local redis_port="6379"
    
    if [[ "$REDIS_CONNECTION_STRING" =~ redis://([^:]+):([0-9]+) ]]; then
        redis_host="${BASH_REMATCH[1]}"
        redis_port="${BASH_REMATCH[2]}"
    elif [[ "$REDIS_CONNECTION_STRING" =~ redis://([^/:]+) ]]; then
        redis_host="${BASH_REMATCH[1]}"
    fi
    
    log_verbose "Host: $redis_host, Port: $redis_port"
    
    if response=$(redis-cli -h "$redis_host" -p "$redis_port" --no-raw ping 2>/dev/null); then
        if [[ "$response" == "PONG" ]]; then
            log_success "Redis responding"
            
            # Try to get Redis info
            if info=$(redis-cli -h "$redis_host" -p "$redis_port" info server 2>/dev/null); then
                if $VERBOSE; then
                    echo "Redis info:"
                    echo "$info" | grep -E "redis_version|used_memory|connected_clients" || true
                fi
            fi
            return 0
        else
            log_error "Redis returned unexpected response: $response"
            return 1
        fi
    else
        log_error "Redis not responding (connection refused or timeout)"
        log_verbose "Make sure Redis is running on $redis_host:$redis_port"
        return 1
    fi
}

check_home_assistant() {
    if [[ -z "$HOMEASSISTANT_URL" ]]; then
        log_warning "Home Assistant URL not configured (skipping)"
        return 0
    fi
    
    log_info "Checking Home Assistant ($HOMEASSISTANT_URL)"
    
    if [[ -z "$HOMEASSISTANT_ACCESS_TOKEN" ]]; then
        log_error "Home Assistant access token not configured"
        return 1
    fi
    
    log_verbose "Making API call to Home Assistant"
    
    if response=$(curl -s -w "\n%{http_code}" \
        --max-time $CHECK_TIMEOUT \
        -H "Authorization: Bearer $HOMEASSISTANT_ACCESS_TOKEN" \
        "$HOMEASSISTANT_URL/api/" 2>/dev/null); then
        
        local body=$(echo "$response" | head -n -1)
        local http_code=$(echo "$response" | tail -n 1)
        
        if [[ "$http_code" == "200" ]]; then
            log_success "Home Assistant API responding (HTTP $http_code)"
            
            # Extract Home Assistant version if available
            if $VERBOSE && echo "$body" | grep -q "message"; then
                local version=$(echo "$body" | grep -oP '"message":\s*"Home Assistant API: \K[^"]*' || true)
                if [[ -n "$version" ]]; then
                    echo "Version: $version"
                fi
            fi
            return 0
        else
            log_error "Home Assistant API returned HTTP $http_code"
            if $VERBOSE; then
                echo "Response: $body"
            fi
            return 1
        fi
    else
        log_error "Home Assistant not responding (connection refused, timeout, or invalid URL)"
        return 1
    fi
}

check_agent_registry() {
    local registry_url="${AGENT_REGISTRY_URL:-http://localhost:5001}"
    log_info "Checking Agent Registry ($registry_url)"
    
    log_verbose "Endpoint: $registry_url/health"
    
    if response=$(curl -s -w "\n%{http_code}" --max-time $CHECK_TIMEOUT "$registry_url/health" 2>/dev/null); then
        local body=$(echo "$response" | head -n -1)
        local http_code=$(echo "$response" | tail -n 1)
        
        if [[ "$http_code" == "200" ]]; then
            log_success "Agent Registry responding (HTTP $http_code)"
            if $VERBOSE && [[ -n "$body" ]]; then
                echo "Agents: $(echo "$body" | jq -r '.agents | length' 2>/dev/null || echo "$body")"
            fi
            return 0
        else
            log_error "Agent Registry returned HTTP $http_code"
            return 1
        fi
    else
        log_error "Agent Registry not responding"
        log_verbose "Agent Registry may not be running; this is optional"
        return 0  # Don't fail overall if registry is optional
    fi
}

check_systemd_service() {
    log_info "Checking systemd service"
    
    if systemctl is-active --quiet lucia 2>/dev/null; then
        log_success "Service is active"
        
        if $VERBOSE; then
            systemctl status lucia --no-pager 2>/dev/null | head -5 || true
        fi
        return 0
    else
        log_error "Service is not active"
        if $VERBOSE; then
            systemctl status lucia --no-pager 2>/dev/null || true
        fi
        return 1
    fi
}

check_docker_container() {
    log_info "Checking Docker container"
    
    local container_name="${1:-lucia}"
    
    if docker inspect "$container_name" &>/dev/null; then
        local status=$(docker inspect -f '{{.State.Status}}' "$container_name" 2>/dev/null || echo "unknown")
        
        if [[ "$status" == "running" ]]; then
            log_success "Container is running"
            
            if $VERBOSE; then
                docker inspect "$container_name" --format='Status: {{.State.Status}}, Uptime: {{.State.StartedAt}}' 2>/dev/null || true
            fi
            return 0
        else
            log_error "Container is $status (not running)"
            return 1
        fi
    else
        log_error "Container not found"
        return 1
    fi
}

check_kubernetes_pod() {
    log_info "Checking Kubernetes pod"
    
    local pod_name="${1:-lucia}"
    local namespace="${2:-default}"
    
    if kubectl get pod "$pod_name" -n "$namespace" &>/dev/null; then
        local status=$(kubectl get pod "$pod_name" -n "$namespace" -o jsonpath='{.status.phase}' 2>/dev/null || echo "unknown")
        
        if [[ "$status" == "Running" ]]; then
            log_success "Pod is running"
            
            if $VERBOSE; then
                kubectl describe pod "$pod_name" -n "$namespace" 2>/dev/null | grep -E "Status:|Conditions:" || true
            fi
            return 0
        else
            log_error "Pod status is $status (not running)"
            return 1
        fi
    else
        log_error "Pod not found"
        return 1
    fi
}

# Summary
print_summary() {
    local total=$1
    local passed=$2
    
    echo ""
    echo -e "${BLUE}═══════════════════════════════${NC}"
    echo -e "${BLUE}Health Check Summary${NC}"
    echo -e "${BLUE}═══════════════════════════════${NC}"
    
    if [[ $passed -eq $total ]]; then
        echo -e "${GREEN}✓ All $total checks passed${NC}"
        return 0
    else
        local failed=$((total - passed))
        echo -e "${RED}✗ $failed of $total checks failed${NC}"
        return 1
    fi
}

# Main execution
main() {
    load_config
    
    echo -e "${BLUE}╔══════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║     Lucia Health Check Script        ║${NC}"
    echo -e "${BLUE}╚══════════════════════════════════════╝${NC}"
    echo ""
    
    local total_checks=0
    local passed_checks=0
    local failed=false
    
    # Determine deployment method and run appropriate checks
    if command -v docker &>/dev/null && docker ps &>/dev/null 2>&1; then
        echo -e "${YELLOW}Detected Docker environment${NC}"
        echo ""
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "lucia" ]]; then
            ((total_checks++))
            if check_lucia_api; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "redis" ]]; then
            ((total_checks++))
            if check_redis; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "home-assistant" ]]; then
            ((total_checks++))
            if check_home_assistant; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
    elif command -v systemctl &>/dev/null; then
        echo -e "${YELLOW}Detected systemd environment${NC}"
        echo ""
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "lucia" ]]; then
            ((total_checks++))
            if check_lucia_api; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "service" ]]; then
            ((total_checks++))
            if check_systemd_service; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "redis" ]]; then
            ((total_checks++))
            if check_redis; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
    elif command -v kubectl &>/dev/null; then
        echo -e "${YELLOW}Detected Kubernetes environment${NC}"
        echo ""
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "lucia" ]]; then
            ((total_checks++))
            if check_lucia_api; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
        
        if [[ -z "$SERVICE_FILTER" ]] || [[ "$SERVICE_FILTER" == "redis" ]]; then
            ((total_checks++))
            if check_redis; then
                ((passed_checks++))
            else
                failed=true
            fi
        fi
    else
        # Generic checks for unknown environment
        log_info "Unknown deployment environment; running generic checks"
        echo ""
        
        ((total_checks++))
        if check_lucia_api; then
            ((passed_checks++))
        else
            failed=true
        fi
        
        ((total_checks++))
        if check_redis; then
            ((passed_checks++))
        else
            failed=true
        fi
    fi
    
    # Print summary and exit
    print_summary $total_checks $passed_checks
    
    if [[ "$failed" == true ]]; then
        exit 1
    else
        exit 0
    fi
}

# Run main function
main "$@"
