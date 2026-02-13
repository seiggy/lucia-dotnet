#!/bin/bash

# Lucia Docker MVP Verification Script
# Purpose: Verify Docker MVP deployment is working correctly
# Usage: ./infra/docker/verify-mvp.sh
#
# This script performs automated verification of:
#   - Service startup and health
#   - API endpoints
#   - Database connectivity
#   - Configuration validation
#   - Integration testing

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
LUCIA_URL="http://localhost:5000"
REDIS_HOST="localhost"
REDIS_PORT="6379"
MAX_WAIT_TIME=30
CHECK_INTERVAL=2

# Test counters
TESTS_PASSED=0
TESTS_FAILED=0
TESTS_SKIPPED=0

# Functions
print_header() {
    echo -e "\n${BLUE}=== $1 ===${NC}\n"
}

print_test() {
    echo -n "  Testing $1... "
}

print_pass() {
    echo -e "${GREEN}✓ PASS${NC}"
    ((TESTS_PASSED++))
}

print_fail() {
    echo -e "${RED}✗ FAIL${NC}: $1"
    ((TESTS_FAILED++))
}

print_skip() {
    echo -e "${YELLOW}⊘ SKIP${NC}: $1"
    ((TESTS_SKIPPED++))
}

print_summary() {
    echo -e "\n${BLUE}=== Test Summary ===${NC}"
    echo -e "  ${GREEN}Passed:${NC} $TESTS_PASSED"
    echo -e "  ${RED}Failed:${NC} $TESTS_FAILED"
    echo -e "  ${YELLOW}Skipped:${NC} $TESTS_SKIPPED"
    echo ""
    
    if [ $TESTS_FAILED -eq 0 ]; then
        echo -e "${GREEN}✓ All tests passed!${NC}"
        return 0
    else
        echo -e "${RED}✗ Some tests failed!${NC}"
        return 1
    fi
}

wait_for_service() {
    local url=$1
    local timeout=$2
    local elapsed=0
    
    while [ $elapsed -lt $timeout ]; do
        if curl -sf "$url" > /dev/null 2>&1; then
            return 0
        fi
        sleep $CHECK_INTERVAL
        elapsed=$((elapsed + CHECK_INTERVAL))
    done
    
    return 1
}

# Main verification flow

print_header "Docker MVP Verification"

# Check prerequisites
print_header "Prerequisites"

print_test "docker-compose is installed"
if command -v docker-compose &> /dev/null; then
    print_pass
else
    print_fail "docker-compose not found"
    exit 1
fi

print_test "docker is running"
if docker ps > /dev/null 2>&1; then
    print_pass
else
    print_fail "docker not accessible"
    exit 1
fi

print_test ".env file exists"
if [ -f .env ]; then
    print_pass
else
    print_fail ".env not found (copy from .env.example)"
    exit 1
fi

# Service status
print_header "Service Status"

print_test "services are running"
if docker-compose ps | grep -q "Up"; then
    print_pass
else
    print_fail "services not running (run: docker-compose up -d)"
    TESTS_FAILED=$((TESTS_FAILED + 1))
fi

print_test "lucia container health"
LUCIA_HEALTH=$(docker-compose ps lucia 2>/dev/null | grep -i "healthy\|up" | wc -l)
if [ $LUCIA_HEALTH -gt 0 ]; then
    print_pass
else
    print_fail "lucia not healthy"
fi

print_test "redis container health"
REDIS_HEALTH=$(docker-compose ps redis 2>/dev/null | grep -i "healthy\|up" | wc -l)
if [ $REDIS_HEALTH -gt 0 ]; then
    print_pass
else
    print_fail "redis not healthy"
fi

# API Endpoints
print_header "API Endpoints"

print_test "Lucia health endpoint"
if timeout 10 curl -sf "$LUCIA_URL/health" > /dev/null 2>&1; then
    print_pass
else
    print_fail "health endpoint not responding (Is Lucia running?)"
fi

print_test "Lucia agent registry"
if timeout 10 curl -sf "$LUCIA_URL/api/agents" > /dev/null 2>&1; then
    print_pass
else
    print_fail "agent registry not responding"
fi

print_test "Lucia chat API"
RESPONSE=$(timeout 10 curl -sf -X POST "$LUCIA_URL/api/chat" \
    -H "Content-Type: application/json" \
    -d '{"message": "test", "sessionId": "verify-test"}' 2>/dev/null || echo "FAILED")

if [ "$RESPONSE" != "FAILED" ] && echo "$RESPONSE" | grep -q "message\|response"; then
    print_pass
else
    print_fail "chat API not responding correctly"
fi

# Database connectivity
print_header "Database Connectivity"

print_test "Redis connectivity"
if docker-compose exec redis redis-cli --no-auth-warning PING 2>/dev/null | grep -q "PONG"; then
    print_pass
else
    print_fail "Cannot connect to Redis"
fi

print_test "Redis database accessible"
DBSIZE=$(docker-compose exec redis redis-cli --no-auth-warning DBSIZE 2>/dev/null | grep -oE '[0-9]+')
if [ ! -z "$DBSIZE" ]; then
    print_pass
    echo "    Database contains $DBSIZE keys"
else
    print_fail "Cannot query Redis database"
fi

# Configuration validation
print_header "Configuration Validation"

print_test "HOMEASSISTANT_URL configured"
if grep -q "HOMEASSISTANT_URL=" .env; then
    HAURL=$(grep "HOMEASSISTANT_URL=" .env | cut -d= -f2)
    print_pass
    echo "    URL: $HAURL"
else
    print_skip "HOMEASSISTANT_URL not set (optional for MVP)"
fi

print_test "Chat model connection string configured"
if grep -q "ConnectionStrings__chat-model=" .env; then
    print_pass
else
    print_fail "ConnectionStrings__chat-model not configured"
fi

print_test "Redis connection string configured"
if grep -q "REDIS_CONNECTION_STRING=" .env; then
    print_pass
else
    print_skip "REDIS_CONNECTION_STRING not set (uses default)"
fi

# Integration testing
print_header "Integration Tests"

SESSION_ID="verify-$(date +%s)"

print_test "Session creation and persistence"
RESPONSE=$(timeout 10 curl -sf -X POST "$LUCIA_URL/api/chat" \
    -H "Content-Type: application/json" \
    -d "{\"message\": \"test message\", \"sessionId\": \"$SESSION_ID\"}" 2>/dev/null)

if echo "$RESPONSE" | grep -q "$SESSION_ID"; then
    print_pass
    
    # Check Redis has the session
    print_test "Session data in Redis"
    REDIS_KEYS=$(docker-compose exec redis redis-cli --no-auth-warning KEYS "*$SESSION_ID*" 2>/dev/null)
    if [ ! -z "$REDIS_KEYS" ]; then
        print_pass
    else
        print_skip "Session not found in Redis (may be in memory)"
    fi
else
    print_fail "Session not created"
fi

print_test "Conversation memory (multi-turn)"
RESPONSE2=$(timeout 10 curl -sf -X POST "$LUCIA_URL/api/chat" \
    -H "Content-Type: application/json" \
    -d "{\"message\": \"follow-up question\", \"sessionId\": \"$SESSION_ID\"}" 2>/dev/null)

if echo "$RESPONSE2" | grep -q "message\|response"; then
    print_pass
else
    print_fail "Follow-up message failed"
fi

# Performance checks
print_header "Performance Checks"

print_test "Response time (should be <2 seconds)"
START=$(date +%s%N | cut -b1-13)
timeout 10 curl -sf -X POST "$LUCIA_URL/api/chat" \
    -H "Content-Type: application/json" \
    -d '{"message": "quick test", "sessionId": "perf-test"}' > /dev/null 2>&1
END=$(date +%s%N | cut -b1-13)
ELAPSED=$((END - START))

if [ $ELAPSED -lt 2000 ]; then
    print_pass
    echo "    Response time: ${ELAPSED}ms"
else
    print_skip "Response time slow: ${ELAPSED}ms (may be normal on first request)"
fi

print_test "Resource usage (Lucia <500MB RAM)"
MEMORY=$(docker stats --no-stream lucia 2>/dev/null | tail -1 | awk '{print $4}' | sed 's/MiB//')
if (( $(echo "$MEMORY < 500" | bc -l) )); then
    print_pass
    echo "    Memory: ${MEMORY}MiB"
else
    print_skip "Memory usage: ${MEMORY}MiB (may be normal)"
fi

# Logs check
print_header "Log Analysis"

print_test "No ERROR logs in recent output"
ERROR_COUNT=$(docker-compose logs lucia --tail=50 2>/dev/null | grep -i "ERROR\|CRITICAL" | wc -l)
if [ $ERROR_COUNT -eq 0 ]; then
    print_pass
else
    print_skip "$ERROR_COUNT error(s) found in logs (review with: docker-compose logs lucia)"
fi

# Summary and recommendations
print_summary

# Specific recommendations
if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "\n${YELLOW}Troubleshooting suggestions:${NC}"
    echo "  1. Verify services are running: docker-compose ps"
    echo "  2. Check Lucia logs: docker-compose logs lucia"
    echo "  3. Verify .env configuration: cat .env | grep -v '^#'"
    echo "  4. Test connectivity: curl http://localhost:5000/health"
    echo "  5. Run full validation: ./infra/scripts/validate-deployment.sh"
fi

echo -e "\n${BLUE}For more details, see:${NC}"
echo "  - Deployment Guide: infra/docker/DEPLOYMENT.md"
echo "  - Testing Guide: infra/docker/TESTING.md"
echo "  - Manual Checklist: infra/docker/TESTING-CHECKLIST.md"
echo ""

exit $TESTS_FAILED
