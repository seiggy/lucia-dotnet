#!/usr/bin/env bash

# Lucia Deployment Validation Script
# Pre-deployment configuration validation for all deployment methods
#
# Usage:
#   ./validate-deployment.sh                   # Validate all requirements
#   ./validate-deployment.sh --method docker   # Validate Docker-specific
#   ./validate-deployment.sh --method k8s      # Validate Kubernetes-specific
#   ./validate-deployment.sh --method systemd  # Validate systemd-specific
#   ./validate-deployment.sh --fix             # Auto-fix common issues
#   ./validate-deployment.sh --verbose         # Show detailed validation output
#
# Exit codes:
#   0 = All validations passed
#   1 = Validation failed (deployment will likely fail)
#   2 = Configuration error

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Options
VERBOSE=false
METHOD_FILTER=""
AUTO_FIX=false
CONFIG_FILE=".env"

# Validation results
VALIDATION_PASSED=0
VALIDATION_FAILED=0
VALIDATION_WARNINGS=0

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --method)
            METHOD_FILTER="$2"
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
        --fix)
            AUTO_FIX=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 2
            ;;
    esac
done

# Logging
log_info() {
    echo -e "${BLUE}ℹ${NC} $*"
}

log_pass() {
    echo -e "${GREEN}✓${NC} $*"
    ((VALIDATION_PASSED++))
}

log_fail() {
    echo -e "${RED}✗${NC} $*"
    ((VALIDATION_FAILED++))
}

log_warn() {
    echo -e "${YELLOW}⚠${NC} $*"
    ((VALIDATION_WARNINGS++))
}

log_verbose() {
    if $VERBOSE; then
        echo -e "${BLUE}→${NC} $*"
    fi
}

# Load configuration
load_config() {
    if [[ -f "$CONFIG_FILE" ]]; then
        log_verbose "Loading configuration from $CONFIG_FILE"
        set +u
        source "$CONFIG_FILE" 2>/dev/null || true
        set -u
    else
        log_warn "Configuration file not found: $CONFIG_FILE"
    fi
}

# Validation functions
validate_required_env() {
    log_info "Validating required environment variables..."
    
    local required_vars=(
        "HOMEASSISTANT_URL"
        "HOMEASSISTANT_ACCESS_TOKEN"
        "REDIS_CONNECTION_STRING"
        "OPENAI_API_KEY"
    )
    
    for var in "${required_vars[@]}"; do
        if [[ -z "${!var:-}" ]]; then
            log_fail "Required variable not set: $var"
        else
            log_pass "Required variable set: $var"
        fi
    done
}

validate_urls() {
    log_info "Validating URL formats..."
    
    # Validate HOMEASSISTANT_URL
    if [[ -n "${HOMEASSISTANT_URL:-}" ]]; then
        if [[ "$HOMEASSISTANT_URL" =~ ^https?:// ]]; then
            log_pass "HOMEASSISTANT_URL is valid: $HOMEASSISTANT_URL"
        else
            log_fail "HOMEASSISTANT_URL is not valid HTTP(S) URL: $HOMEASSISTANT_URL"
        fi
    fi
    
    # Validate REDIS_CONNECTION_STRING
    if [[ -n "${REDIS_CONNECTION_STRING:-}" ]]; then
        if [[ "$REDIS_CONNECTION_STRING" =~ ^redis:// ]]; then
            log_pass "REDIS_CONNECTION_STRING is valid: $REDIS_CONNECTION_STRING"
        else
            log_fail "REDIS_CONNECTION_STRING is not valid Redis URL: $REDIS_CONNECTION_STRING"
        fi
    fi
}

validate_connectivity() {
    log_info "Validating network connectivity..."
    
    # Test Home Assistant connectivity
    if [[ -n "${HOMEASSISTANT_URL:-}" ]]; then
        log_verbose "Testing Home Assistant connectivity..."
        if curl -sf --connect-timeout 5 "$HOMEASSISTANT_URL/api/" &>/dev/null; then
            log_pass "Can reach Home Assistant API"
        else
            log_fail "Cannot reach Home Assistant API at $HOMEASSISTANT_URL"
        fi
    fi
    
    # Test Redis connectivity
    if [[ -n "${REDIS_CONNECTION_STRING:-}" ]]; then
        log_verbose "Testing Redis connectivity..."
        if command -v redis-cli &>/dev/null; then
            # Parse Redis connection string
            local redis_host="localhost"
            local redis_port="6379"
            if [[ "$REDIS_CONNECTION_STRING" =~ redis://([^:]+):([0-9]+) ]]; then
                redis_host="${BASH_REMATCH[1]}"
                redis_port="${BASH_REMATCH[2]}"
            fi
            
            if redis-cli -h "$redis_host" -p "$redis_port" ping &>/dev/null; then
                log_pass "Can reach Redis at $redis_host:$redis_port"
            else
                log_fail "Cannot reach Redis at $redis_host:$redis_port"
            fi
        else
            log_warn "redis-cli not installed; skipping Redis connectivity check"
        fi
    fi
}

validate_docker_setup() {
    log_info "Validating Docker setup..."
    
    # Check if Docker is installed
    if ! command -v docker &>/dev/null; then
        log_fail "Docker is not installed"
        return 1
    fi
    log_pass "Docker is installed"
    
    # Check if Docker daemon is running
    if ! docker info &>/dev/null; then
        log_fail "Docker daemon is not running"
        return 1
    fi
    log_pass "Docker daemon is running"
    
    # Check Docker version
    local docker_version=$(docker --version | grep -oP '\d+\.\d+' | head -1)
    if [[ -n "$docker_version" ]]; then
        log_pass "Docker version: $docker_version"
    fi
    
    # Check for docker-compose
    if command -v docker-compose &>/dev/null; then
        local compose_version=$(docker-compose --version | grep -oP '\d+\.\d+' | head -1)
        log_pass "Docker Compose version: $compose_version"
    else
        log_warn "docker-compose not found; Docker CLI compose should be available"
    fi
    
    # Check .dockerignore
    if [[ -f ".dockerignore" ]]; then
        log_pass ".dockerignore exists"
    else
        log_warn ".dockerignore not found; consider creating one"
    fi
    
    # Check for docker-compose.yml
    local compose_file="infra/docker/docker-compose.yml"
    if [[ -f "$compose_file" ]]; then
        log_pass "docker-compose.yml found at $compose_file"
        
        # Validate YAML syntax
        if command -v docker &>/dev/null; then
            if docker run --rm -v "$PWD:/data" sdesbure/yamllint "/data/$compose_file" &>/dev/null 2>&1; then
                log_pass "docker-compose.yml syntax is valid"
            else
                log_fail "docker-compose.yml has syntax errors"
            fi
        fi
    else
        log_warn "docker-compose.yml not found at $compose_file"
    fi
}

validate_kubernetes_setup() {
    log_info "Validating Kubernetes setup..."
    
    # Check if kubectl is installed
    if ! command -v kubectl &>/dev/null; then
        log_fail "kubectl is not installed"
        return 1
    fi
    log_pass "kubectl is installed"
    
    # Check kubectl version
    local kubectl_version=$(kubectl version --client --short 2>/dev/null | grep -oP 'v\d+\.\d+' | head -1)
    if [[ -n "$kubectl_version" ]]; then
        log_pass "kubectl version: $kubectl_version"
    fi
    
    # Check kubeconfig
    if kubectl cluster-info &>/dev/null; then
        log_pass "kubectl can connect to cluster"
        
        # Get cluster info
        local cluster_version=$(kubectl version --short 2>/dev/null | grep Server || echo "unknown")
        log_verbose "Cluster info: $cluster_version"
    else
        log_fail "kubectl cannot connect to cluster; check KUBECONFIG"
    fi
    
    # Check for Helm
    if command -v helm &>/dev/null; then
        local helm_version=$(helm version --short | grep -oP 'v\d+\.\d+' | head -1)
        log_pass "Helm version: $helm_version"
    else
        log_warn "Helm not installed; required for production K8s deployments"
    fi
    
    # Check for values.yaml
    if [[ -f "kubernetes/helm/values.yaml" ]]; then
        log_pass "Helm values.yaml found"
    else
        log_warn "Helm values.yaml not found at kubernetes/helm/values.yaml"
    fi
    
    # Check for manifests
    if ls kubernetes/manifests/*.yaml 2>/dev/null | grep -q .; then
        log_pass "Kubernetes manifests found"
        
        # Count manifests
        local manifest_count=$(ls kubernetes/manifests/*.yaml 2>/dev/null | wc -l)
        log_verbose "Found $manifest_count manifest files"
    else
        log_warn "No Kubernetes manifests found in kubernetes/manifests/"
    fi
}

validate_systemd_setup() {
    log_info "Validating systemd setup..."
    
    # Check if systemctl is available
    if ! command -v systemctl &>/dev/null; then
        log_warn "systemctl not available; might not be on systemd system"
        return 0
    fi
    log_pass "systemctl is available"
    
    # Check for service file
    if [[ -f "systemd/lucia.service" ]]; then
        log_pass "Service file exists at systemd/lucia.service"
        
        # Validate service file syntax
        if systemd-analyze verify systemd/lucia.service 2>/dev/null; then
            log_pass "Service file syntax is valid"
        else
            log_fail "Service file has syntax errors"
        fi
    else
        log_warn "Service file not found at systemd/lucia.service"
    fi
    
    # Check for environment file
    if [[ -f "systemd/lucia.env" ]]; then
        log_pass "Environment file exists"
    else
        log_warn "Environment file not found at systemd/lucia.env"
    fi
    
    # Check if lucia service is installed
    if systemctl list-unit-files lucia.service 2>/dev/null | grep -q lucia; then
        log_pass "lucia service is installed"
        
        # Check if it's enabled
        if systemctl is-enabled lucia 2>/dev/null; then
            log_pass "lucia service is enabled"
        else
            log_warn "lucia service is not enabled; won't start on boot"
        fi
    else
        log_warn "lucia service is not installed"
    fi
}

validate_certificates() {
    log_info "Validating certificates..."
    
    if [[ -n "${ENABLE_HTTPS:-false}" ]] && [[ "${ENABLE_HTTPS}" == "true" ]]; then
        log_verbose "HTTPS is enabled; checking certificates..."
        
        local cert_path="${CERTIFICATE_PATH:-}"
        local key_path="${CERTIFICATE_KEY_PATH:-}"
        
        if [[ -z "$cert_path" ]]; then
            log_fail "CERTIFICATE_PATH not set but ENABLE_HTTPS is true"
        elif [[ ! -f "$cert_path" ]]; then
            log_fail "Certificate file not found: $cert_path"
        else
            log_pass "Certificate file found: $cert_path"
            
            # Check certificate validity
            if command -v openssl &>/dev/null; then
                if openssl x509 -in "$cert_path" -noout 2>/dev/null; then
                    log_pass "Certificate is valid X.509"
                    
                    # Check expiration
                    local expiration=$(openssl x509 -in "$cert_path" -noout -enddate 2>/dev/null | cut -d= -f2)
                    log_verbose "Certificate expires: $expiration"
                else
                    log_fail "Certificate is not valid X.509"
                fi
            fi
        fi
        
        if [[ -z "$key_path" ]]; then
            log_fail "CERTIFICATE_KEY_PATH not set but ENABLE_HTTPS is true"
        elif [[ ! -f "$key_path" ]]; then
            log_fail "Key file not found: $key_path"
        else
            log_pass "Key file found: $key_path"
            
            # Check key permissions
            local key_perms=$(stat -c '%a' "$key_path" 2>/dev/null || stat -f '%A' "$key_path" 2>/dev/null || echo "unknown")
            if [[ "$key_perms" == "600" ]]; then
                log_pass "Key file has correct permissions (600)"
            else
                log_warn "Key file permissions are $key_perms; should be 600"
            fi
        fi
    else
        log_verbose "HTTPS is disabled; skipping certificate validation"
    fi
}

validate_permissions() {
    log_info "Validating file permissions..."
    
    # Check .env file permissions if it exists
    if [[ -f "$CONFIG_FILE" ]]; then
        local env_perms=$(stat -c '%a' "$CONFIG_FILE" 2>/dev/null || stat -f '%A' "$CONFIG_FILE" 2>/dev/null || echo "unknown")
        if [[ "$env_perms" == "600" ]] || [[ "$env_perms" == "640" ]]; then
            log_pass ".env file permissions are secure ($env_perms)"
        else
            log_warn ".env file is world-readable ($env_perms); should be 600 or 640"
            if [[ "$AUTO_FIX" == true ]]; then
                chmod 600 "$CONFIG_FILE"
                log_pass "Fixed .env permissions"
            fi
        fi
    fi
    
    # Check systemd environment file
    if [[ -f "systemd/lucia.env" ]]; then
        local systemd_env_perms=$(stat -c '%a' "systemd/lucia.env" 2>/dev/null || stat -f '%A' "systemd/lucia.env" 2>/dev/null || echo "unknown")
        if [[ "$systemd_env_perms" == "600" ]]; then
            log_pass "systemd environment file permissions are secure (600)"
        else
            log_warn "systemd environment file is readable by others ($systemd_env_perms); should be 600"
            if [[ "$AUTO_FIX" == true ]]; then
                chmod 600 "systemd/lucia.env"
                log_pass "Fixed systemd environment file permissions"
            fi
        fi
    fi
}

validate_directories() {
    log_info "Validating directory structure..."
    
    local required_dirs=(
        "docker"
        "kubernetes"
        "systemd"
        "scripts"
    )
    
    for dir in "${required_dirs[@]}"; do
        if [[ -d "$dir" ]]; then
            log_pass "Directory exists: $dir"
        else
            log_warn "Directory not found: $dir"
            if [[ "$AUTO_FIX" == true ]]; then
                mkdir -p "$dir"
                log_pass "Created directory: $dir"
            fi
        fi
    done
}

validate_dependencies() {
    log_info "Validating required dependencies..."
    
    local required_commands=(
        "curl"
        "jq"
    )
    
    for cmd in "${required_commands[@]}"; do
        if command -v "$cmd" &>/dev/null; then
            log_pass "Found required command: $cmd"
        else
            log_warn "Missing required command: $cmd"
        fi
    done
}

# Print summary
print_summary() {
    echo ""
    echo -e "${BLUE}═══════════════════════════════════════${NC}"
    echo -e "${BLUE}Validation Summary${NC}"
    echo -e "${BLUE}═══════════════════════════════════════${NC}"
    
    echo -e "${GREEN}Passed:${NC}   $VALIDATION_PASSED"
    echo -e "${YELLOW}Warnings:${NC} $VALIDATION_WARNINGS"
    echo -e "${RED}Failed:${NC}   $VALIDATION_FAILED"
    
    echo ""
    
    if [[ $VALIDATION_FAILED -eq 0 ]]; then
        echo -e "${GREEN}✓ All critical validations passed${NC}"
        if [[ $VALIDATION_WARNINGS -gt 0 ]]; then
            echo -e "${YELLOW}⚠ $VALIDATION_WARNINGS warning(s) found - review above${NC}"
        fi
        return 0
    else
        echo -e "${RED}✗ $VALIDATION_FAILED critical validation(s) failed${NC}"
        echo -e "${RED}Address failures above before deploying${NC}"
        return 1
    fi
}

# Main
main() {
    load_config
    
    echo -e "${BLUE}╔═══════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║   Lucia Deployment Validator         ║${NC}"
    echo -e "${BLUE}╚═══════════════════════════════════════╝${NC}"
    echo ""
    
    # Run validation checks
    validate_required_env
    validate_urls
    validate_connectivity
    validate_certificates
    validate_permissions
    validate_directories
    validate_dependencies
    
    # Method-specific validations
    if [[ -z "$METHOD_FILTER" ]] || [[ "$METHOD_FILTER" == "docker" ]]; then
        echo ""
        validate_docker_setup
    fi
    
    if [[ -z "$METHOD_FILTER" ]] || [[ "$METHOD_FILTER" == "k8s" ]] || [[ "$METHOD_FILTER" == "kubernetes" ]]; then
        echo ""
        validate_kubernetes_setup
    fi
    
    if [[ -z "$METHOD_FILTER" ]] || [[ "$METHOD_FILTER" == "systemd" ]]; then
        echo ""
        validate_systemd_setup
    fi
    
    # Print and exit
    print_summary
}

main "$@"
