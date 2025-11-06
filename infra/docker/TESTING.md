# Local Testing Guide

> Last Updated: 2025-10-24  
> Status: Complete  
> Version: 1.0.0

## Overview

This guide covers testing Lucia locally using Docker Compose before deploying to production. Includes:

- Setting up a local test environment
- Testing core functionality
- Validating integrations
- Performance profiling
- Debugging techniques

## Prerequisites

- Docker Desktop or Docker Engine + docker-compose
- 2GB available RAM
- Curl or Postman for API testing
- Optional: Home Assistant running locally or on network

## Local Test Environment Setup

### 1. Quick Local Setup (No Home Assistant)

Test Lucia without Home Assistant integration:

```bash
# Navigate to project root
cd lucia-dotnet

# Start services
docker-compose up -d

# Verify startup
docker-compose ps
docker-compose logs lucia | tail -20

# Check health
curl http://localhost:5000/health
```

### 2. Full Local Setup (With Home Assistant)

Test complete integration locally:

```bash
# Create docker-compose.local.yml for extended setup
cat > docker-compose.local.yml << 'EOF'
version: '3.9'
services:
  homeassistant:
    image: homeassistant/home-assistant:latest
    container_name: lucia-ha-test
    networks:
      - lucia-network
    ports:
      - "127.0.0.1:8123:8123"
    environment:
      - TZ=UTC
    volumes:
      - ha-test-data:/config
    restart: unless-stopped

volumes:
  ha-test-data:

networks:
  lucia-network:
    external: true
EOF

# Start with Home Assistant
docker-compose -f docker-compose.yml -f docker-compose.local.yml up -d

# Wait for HA to start (first boot takes 2-3 minutes)
sleep 180

# Access Home Assistant
open http://localhost:8123  # or navigate in browser
```

## Testing Core Functionality

### Test 1: Service Health

Verify all services are running and healthy:

```bash
# Check container status
docker-compose ps
# Expected: lucia, redis, homeassistant (if testing integration)

# Check Lucia health endpoint
curl http://localhost:5000/health

# Expected response:
# {"status":"Healthy","timestamp":"2025-10-24T10:00:00Z","services":{"redis":"Connected","homeAssistant":"Connected"}}

# Check Redis connectivity
docker-compose exec redis redis-cli PING
# Expected: PONG

# Check Redis data
docker-compose exec redis redis-cli KEYS "lucia:*"
# Expected: List of session/task keys or empty initially
```

### Test 2: Agent Registry API

Test agent discovery and registration:

```bash
# List available agents
curl http://localhost:5000/api/agents
# Expected: JSON array of agent definitions

# Get agent details
curl http://localhost:5000/api/agents/light-agent
# Expected: Agent definition with capabilities and skills

# Check agent health
curl http://localhost:5000/api/agents/light-agent/health
# Expected: {"status":"Healthy","lastCheck":"2025-10-24T10:00:00Z"}
```

### Test 3: Chat API (Basic)

Test conversation endpoint without integration:

```bash
# Send simple message
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Hello, what time is it?",
    "sessionId": "test-session-001"
  }'

# Expected response:
# {"response":"The current time is...","sessionId":"test-session-001","timestamp":"2025-10-24T10:00:00Z"}

# Test with conversation memory
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "What did I just ask?",
    "sessionId": "test-session-001"
  }'

# Expected: Agent references previous message
```

### Test 4: Chat API (With Home Assistant)

Test with Home Assistant integration:

```bash
# First, setup Home Assistant with some test devices
# (via Home Assistant UI or automation)

# Test device discovery
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "What devices do we have?",
    "sessionId": "test-ha-001"
  }'

# Expected: Agent lists devices from Home Assistant

# Test light control
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Turn on the living room lights",
    "sessionId": "test-ha-001"
  }'

# Expected: Agent processes request and returns action taken

# Verify action executed
curl http://localhost:8123/api/states/light.living_room \
  -H "Authorization: Bearer YOUR_HA_TOKEN"
# Expected: state should be "on"
```

### Test 5: Intent Recognition

Test AI intent processing:

```bash
# Ambiguous request - should be disambiguated
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Adjust the temperature",
    "sessionId": "test-intent-001"
  }'

# Expected: Agent asks for clarification or uses context

# Context-aware request
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "It is now 22 degrees. Set it to 20.",
    "sessionId": "test-intent-001"
  }'

# Expected: Agent understands context and sets temperature to 20°C
```

### Test 6: Multi-Agent Orchestration

Test agents working together:

```bash
# Complex request requiring multiple agents
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "When I leave home, turn off all lights and lock doors",
    "sessionId": "test-multi-001"
  }'

# Expected: 
# - LightAgent handles lights
# - SecurityAgent handles locks
# - Response shows both actions coordinated

# Check orchestration in logs
docker-compose logs lucia | grep -i "orchestration"
```

### Test 7: Error Handling

Test resilience and error handling:

```bash
# Test with invalid Home Assistant token
# Edit .env: HOMEASSISTANT_ACCESS_TOKEN=invalid-token
docker-compose restart lucia

# Try to use Home Assistant integration
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "List devices", "sessionId": "test-error-001"}'

# Expected: Graceful error response, not crash
# Logs should show authentication error
docker-compose logs lucia | grep -i "authentication"

# Fix token and restart
docker-compose restart lucia
```

### Test 8: Session Persistence

Test conversation memory across requests:

```bash
# Create session
SESSION_ID="persistence-test-$(date +%s)"

# Message 1
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d "{
    \"message\": \"My name is Alice\",
    \"sessionId\": \"$SESSION_ID\"
  }"

# Message 2 - reference previous context
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d "{
    \"message\": \"What is my name?\",
    \"sessionId\": \"$SESSION_ID\"
  }"

# Expected: Agent correctly responds "Your name is Alice"

# Verify Redis stores session
docker-compose exec redis redis-cli KEYS "*$SESSION_ID*"
```

## Integration Testing

### Integration Test 1: Home Assistant Device Control

End-to-end test of device control:

```bash
# Setup: Create a test automation in Home Assistant first
# Trigger: Manual toggle of a test light

# Test natural language control
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Toggle the test light",
    "sessionId": "int-test-001"
  }'

# Verify in Home Assistant
curl http://localhost:8123/api/states/light.test_light \
  -H "Authorization: Bearer YOUR_HA_TOKEN"

# Check Lucia logs for action execution
docker-compose logs lucia | grep -i "test.light"
```

### Integration Test 2: Semantic Search

Test device discovery using embeddings:

```bash
# Create devices with similar names in Home Assistant
# Example:
#   light.living_room_lamp
#   light.living_room_ceiling
#   light.living_room_fan

# Test semantic matching
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Turn on the living room lights",
    "sessionId": "semantic-test-001"
  }'

# Expected: Agent finds and controls light devices despite name variations

# Test with Azure OpenAI (embeddings support)
# Note: OpenAI/Ollama may not support semantic search yet
```

### Integration Test 3: Task Persistence

Test task state recovery after restart:

```bash
# Create long-running automation
curl -X POST http://localhost:5000/api/automation \
  -H "Content-Type: application/json" \
  -d '{
    "name": "test-automation",
    "trigger": "time",
    "triggerTime": "2025-10-24T15:00:00Z",
    "action": "turn_on",
    "actionTarget": "light.living_room"
  }'

# Get task ID from response
TASK_ID=$(curl -s http://localhost:5000/api/automations | jq -r '.[] | select(.name=="test-automation") | .taskId')

# Verify in Redis
docker-compose exec redis redis-cli GET "lucia:task:$TASK_ID"

# Restart Lucia
docker-compose restart lucia
sleep 10

# Verify task still exists
docker-compose exec redis redis-cli GET "lucia:task:$TASK_ID"
# Expected: Task data unchanged

# Verify automation continues
docker-compose logs lucia | grep -i "test-automation"
```

## Performance Testing

### Load Test 1: Concurrent Requests

Test API under load:

```bash
# Using Apache Bench (ab)
ab -n 100 -c 10 http://localhost:5000/health

# Using wrk (if installed)
wrk -t4 -c100 -d30s http://localhost:5000/health

# Using hey (Go HTTP benchmarking tool)
hey -n 100 -c 10 http://localhost:5000/health

# Monitor resource usage during test
docker stats --no-stream
```

### Load Test 2: LLM Request Latency

Measure response times for LLM calls:

```bash
# Simple chat request
time curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello", "sessionId": "perf-test-001"}'

# Expected timing:
# - OpenAI: 500ms - 2000ms (network latency + LLM processing)
# - Ollama: 100ms - 1000ms (depending on model and hardware)
# - Azure: 600ms - 2500ms (depends on deployment)
```

### Load Test 3: Redis Performance

Test Redis throughput:

```bash
# Redis benchmark tool
docker-compose exec redis redis-benchmark -n 100000 -c 50

# Expected output shows operations/second and latency stats
# For Lucia MVP, target: >10000 ops/sec
```

## Debugging

### Debug 1: Enable Verbose Logging

```bash
# Edit .env
LUCIA_LOG_LEVEL=Debug
LUCIA_STRUCTURED_LOGGING=true

# Restart and view detailed logs
docker-compose restart lucia
docker-compose logs -f lucia
```

### Debug 2: Inspect Redis Data

```bash
# Connect to Redis CLI
docker-compose exec redis redis-cli

# Check keys
KEYS lucia:*

# Inspect session data
GET lucia:session:test-session-001

# Check task queue
LRANGE lucia:tasks 0 -1

# Monitor in real-time
MONITOR
```

### Debug 3: Docker Container Inspection

```bash
# Open shell in container
docker-compose exec lucia sh

# Inside container:
# Check environment
env | grep -i lucia

# View logs
tail -100 /var/log/lucia/lucia.log

# Test connectivity
curl http://redis:6379  # Should fail (Redis protocol, not HTTP)
redis-cli -h redis PING  # Should work (Redis CLI)
```

### Debug 4: Network Debugging

```bash
# Check Docker network
docker network inspect lucia-network

# Test connectivity between services
docker-compose exec lucia ping redis
docker-compose exec lucia curl http://localhost:8080/health

# Check exposed ports
docker ps --format "table {{.Names}}\t{{.Ports}}"
```

### Debug 5: View Container Filesystem

```bash
# Copy files from container
docker cp lucia-agenthost:/app ./lucia-app-copy

# Inspect published application
ls -la ./lucia-app-copy/

# Check configuration files
cat ./lucia-app-copy/appsettings.json
```

## Cleanup and Reset

### Cleanup Single Service

```bash
# Stop Lucia only (keep Redis)
docker-compose stop lucia

# Remove Lucia container
docker-compose rm lucia

# Rebuild and start
docker-compose up -d lucia
```

### Full System Reset

```bash
# Stop all services
docker-compose down

# Remove all volumes (deletes Redis data!)
docker-compose down -v

# Remove images
docker-compose down --rmi all

# Start fresh
docker-compose up -d
```

### Prune All Docker Resources

```bash
# Remove dangling images and containers
docker system prune -a

# Remove all volumes (destructive!)
docker volume prune

# Full cleanup
docker system prune -a --volumes
```

## Continuous Testing

### Automated Test Script

Create `test-lucia.sh`:

```bash
#!/bin/bash
set -e

echo "🧪 Testing Lucia Deployment"

# Start services
echo "📦 Starting services..."
docker-compose up -d
sleep 10

# Test health
echo "❤️  Testing health endpoint..."
curl -f http://localhost:5000/health || exit 1

# Test Redis
echo "💾 Testing Redis..."
docker-compose exec redis redis-cli PING | grep PONG || exit 1

# Test agent registry
echo "🤖 Testing agent registry..."
curl -f http://localhost:5000/api/agents || exit 1

# Test chat API
echo "💬 Testing chat API..."
curl -f -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello", "sessionId": "test"}' || exit 1

echo "✅ All tests passed!"

# Cleanup
docker-compose down
```

Run tests:

```bash
chmod +x test-lucia.sh
./test-lucia.sh
```

## Next Steps

- Deploy to production using Docker Compose deployment guide
- Monitor using OpenTelemetry integration
- Performance tune for your use case
- Scale to Kubernetes for HA
