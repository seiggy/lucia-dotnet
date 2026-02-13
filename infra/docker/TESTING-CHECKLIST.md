# Docker MVP Manual Testing Checklist

> Last Updated: 2025-10-24  
> Status: Ready for Testing  
> Version: 1.0.0

## Pre-Testing Checklist

- [ ] Docker Desktop/Engine installed and running
- [ ] docker-compose installed and working
- [ ] Git repository cloned
- [ ] At least 2GB free RAM available
- [ ] At least 10GB free disk space
- [ ] Network access to LLM provider (OpenAI, Azure, etc.)
- [ ] (Optional) Home Assistant instance available for integration testing

## Setup Verification

### Environment Configuration

- [ ] `.env.example` exists in repository root
- [ ] `.env` file created from `.env.example`
- [ ] Required variables populated in `.env`:
  - [ ] `HOMEASSISTANT_URL` is valid
  - [ ] `HOMEASSISTANT_ACCESS_TOKEN` is valid
  - [ ] `ConnectionStrings__chat-model` is correctly formatted
  - [ ] `REDIS_CONNECTION_STRING` is set (defaults to redis://redis:6379)
- [ ] No secrets accidentally committed to git
- [ ] `.env` is in `.gitignore`

### Docker Configuration

- [ ] `docker-compose.yml` exists in repository root
- [ ] Docker build context includes all required files
- [ ] `Dockerfile.agenthost` exists in `infra/docker/`
- [ ] `Dockerfile.redis` exists in `infra/docker/`
- [ ] `redis.conf` exists in `infra/docker/`
- [ ] `.dockerignore` exists and excludes unnecessary files

### Health Check Validation

- [ ] Health check scripts executable:

  ```bash
  ls -la infra/scripts/health-check.sh
  ls -la infra/scripts/validate-deployment.sh
  ```

- [ ] Configuration validation passes:

  ```bash
  ./infra/scripts/validate-deployment.sh
  ```

---

## Deployment Testing

### T1: Service Startup

**Objective**: Verify all services start successfully

- [ ] Run `docker-compose up -d`
- [ ] Wait 30 seconds for startup
- [ ] Run `docker-compose ps`:
  - [ ] lucia: Status = Up, Health = healthy
  - [ ] redis: Status = Up, Health = healthy
- [ ] No error messages in logs:

  ```bash
  docker-compose logs --tail=50
  ```

- [ ] All services have correct ports:
  - [ ] Lucia: 127.0.0.1:5000
  - [ ] Redis: 127.0.0.1:6379

**Pass Criteria**: All services running with "healthy" status

---

### T2: Health Endpoints

**Objective**: Verify health check endpoints respond correctly

- [ ] Lucia health endpoint:

  ```bash
  curl http://localhost:5000/health
  ```

  - [ ] HTTP 200 response
  - [ ] JSON response with status field
  - [ ] All components report healthy
  
- [ ] Redis connectivity:

  ```bash
  docker-compose exec redis redis-cli PING
  ```

  - [ ] Responds with "PONG"
  
- [ ] Redis data accessible:

  ```bash
  docker-compose exec redis redis-cli DBSIZE
  ```

  - [ ] Returns integer without error

**Pass Criteria**: All health checks return success

---

### T3: Agent Registry API

**Objective**: Verify agent discovery and registration works

- [ ] List agents endpoint:

  ```bash
  curl http://localhost:5000/api/agents
  ```

  - [ ] HTTP 200 response
  - [ ] Returns JSON array of agents
  - [ ] At least one agent listed (e.g., LightAgent, MusicAgent)
  
- [ ] Get specific agent:

  ```bash
  curl http://localhost:5000/api/agents/light-agent
  ```

  - [ ] HTTP 200 response
  - [ ] Returns agent definition
  - [ ] Includes capabilities and skills
  
- [ ] Agent health check:

  ```bash
  curl http://localhost:5000/api/agents/light-agent/health
  ```

  - [ ] HTTP 200 response
  - [ ] Status field indicates healthy

**Pass Criteria**: Agent registry endpoints respond correctly

---

### T4: Chat API (Without Home Assistant)

**Objective**: Verify basic chat functionality

- [ ] Send simple greeting:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message": "Hello", "sessionId": "test-001"}'
  ```

  - [ ] HTTP 200 response
  - [ ] Response includes message content
  - [ ] Response includes session ID
  - [ ] Response includes timestamp
  
- [ ] Send follow-up question:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message": "What did I just say?", "sessionId": "test-001"}'
  ```

  - [ ] HTTP 200 response
  - [ ] Agent references previous message (conversation memory works)
  
- [ ] Send time-related query:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message": "What time is it?", "sessionId": "test-002"}'
  ```

  - [ ] HTTP 200 response
  - [ ] Response includes time information

**Pass Criteria**: Chat API responses correctly and maintains context

---

### T5: Session Persistence (Redis)

**Objective**: Verify session data persists in Redis

- [ ] Create multiple messages in same session:

  ```bash
  for i in {1..3}; do
    curl -X POST http://localhost:5000/api/chat \
      -H "Content-Type: application/json" \
      -d "{\"message\": \"Message $i\", \"sessionId\": \"persist-test\"}"
    sleep 1
  done
  ```

  - [ ] All requests successful (HTTP 200)
  
- [ ] Verify Redis contains session data:

  ```bash
  docker-compose exec redis redis-cli KEYS "lucia:*persist*"
  ```
  
  - [ ] Returns one or more keys
  - [ ] Key structure indicates session storage
  
- [ ] Inspect session data:

  ```bash
  docker-compose exec redis redis-cli GET "lucia:session:persist-test"
  ```

  - [ ] Returns data (not empty)
  - [ ] Contains conversation history

**Pass Criteria**: Session data persists in Redis across requests

---

### T6: Home Assistant Integration (If Available)

**Objective**: Verify Home Assistant integration

- [ ] Verify Home Assistant URL is accessible:

  ```bash
  curl $HOMEASSISTANT_URL
  ```

  - [ ] Returns HTTP 200 (or redirect to login)
  
- [ ] Test Home Assistant API connectivity:

  ```bash
  docker-compose exec lucia curl http://homeassistant:8123 \
    -H "Authorization: Bearer YOUR_TOKEN"
  ```

  - [ ] No connection refused errors
  
- [ ] Request Home Assistant device list via chat:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message": "What devices do we have?", "sessionId": "ha-test"}'
  ```

  - [ ] HTTP 200 response
  - [ ] Response mentions devices or device count
  - [ ] No authentication errors in logs
  
- [ ] Check Lucia logs for successful HA connection:

  ```bash
  docker-compose logs lucia | grep -i "homeassistant\|connected"
  ```

  - [ ] Shows successful connection
  - [ ] No repeated connection errors

**Pass Criteria**: Home Assistant integration connects and responds

---

### T7: Error Handling and Resilience

**Objective**: Verify graceful error handling

- [ ] Test with invalid chat request:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{}'  # Missing required fields
  ```

  - [ ] HTTP 400 (Bad Request) response
  - [ ] Returns error message, not 500 error
  
- [ ] Test with very long message:

  ```bash
  LONG_MSG=$(printf 'a%.0s' {1..10000})
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d "{\"message\": \"$LONG_MSG\", \"sessionId\": \"long-test\"}"
  ```

  - [ ] Handles gracefully (either 400 or processes)
  - [ ] Service doesn't crash
  
- [ ] Verify service recovery after error:

  ```bash
  curl http://localhost:5000/health
  ```

  - [ ] Still responds with status healthy
  - [ ] No lingering connection issues

**Pass Criteria**: Errors handled gracefully without crashes

---

### T8: Performance and Resource Usage

**Objective**: Verify acceptable performance and resource usage

- [ ] Check resource usage before load:

  ```bash
  docker stats --no-stream
  ```

  - [ ] Lucia using <500MB RAM
  - [ ] Redis using <100MB RAM
  - [ ] CPU usage <10%
  
- [ ] Send 10 sequential requests:

  ```bash
  for i in {1..10}; do
    time curl -X POST http://localhost:5000/api/chat \
      -H "Content-Type: application/json" \
      -d "{\"message\": \"Test $i\", \"sessionId\": \"perf-test\"}"
  done
  ```

  - [ ] Average response time <2 seconds (OpenAI), <1 second (Ollama)
  - [ ] No timeout errors
  - [ ] All requests succeed
  
- [ ] Check resource usage after load:

  ```bash
  docker stats --no-stream
  ```

  - [ ] Lucia using <1GB RAM
  - [ ] CPU usage returned to idle after requests complete
  - [ ] No memory leaks evident

**Pass Criteria**: Performance meets MVP requirements, resources reasonable

---

### T9: Logging and Observability

**Objective**: Verify logging is working

- [ ] View Lucia logs:

  ```bash
  docker-compose logs lucia --tail=50
  ```

  - [ ] Contains startup messages
  - [ ] No ERROR or CRITICAL messages
  - [ ] Timestamps present
  
- [ ] View Redis logs:

  ```bash
  docker-compose logs redis --tail=20
  ```

  - [ ] Shows Redis startup
  - [ ] No error messages
  
- [ ] Check structured logging format:

  ```bash
  docker-compose logs lucia --tail=10 | grep -i json
  ```

  - [ ] Or: Logs contain structured fields (timestamp, level, message)
  
- [ ] Verify log levels in .env:

  ```bash
  grep LUCIA_LOG_LEVEL .env
  ```

  - [ ] Set to appropriate level (Information for prod, Debug for dev)

**Pass Criteria**: Logging configured and working properly

---

### T10: Data Persistence

**Objective**: Verify Redis persistence across restart

- [ ] Create session data:

  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message": "Persistent test", "sessionId": "persist-001"}'
  ```

  - [ ] HTTP 200 response
  
- [ ] Verify data in Redis:

  ```bash
  docker-compose exec redis redis-cli DBSIZE
  ```

  - [ ] Shows database size > 0
  
- [ ] Restart Redis:

  ```bash
  docker-compose restart redis
  ```

  - [ ] Wait for restart (5-10 seconds)
  
- [ ] Verify data persists:

  ```bash
  docker-compose exec redis redis-cli DBSIZE
  ```

  - [ ] Database size same as before restart
  
- [ ] Verify specific session data:

  ```bash
  docker-compose exec redis redis-cli GET "lucia:session:persist-001"
  ```

  - [ ] Returns original data (not empty)

**Pass Criteria**: Data persists through service restart

---

### T11: Configuration Validation

**Objective**: Verify configuration is validated on startup

- [ ] Break configuration intentionally:

  ```bash
  # Edit .env temporarily
  echo "HOMEASSISTANT_URL=not-a-url" >> .env
  
  # Try to start service
  docker-compose restart lucia
  sleep 5
  
  # Check if it logs validation error
  docker-compose logs lucia | grep -i "validation\|error"
  ```

  - [ ] Shows validation error in logs
  - [ ] Service doesn't crash (reports error gracefully)
  
- [ ] Fix configuration:

  ```bash
  # Restore .env from backup or re-create
  git checkout .env
  docker-compose restart lucia
  sleep 10
  ```

  - [ ] Service restarts successfully
  - [ ] Health endpoint responds

**Pass Criteria**: Configuration validation prevents startup with invalid config

---

### T12: Cleanup and Shutdown

**Objective**: Verify graceful shutdown

- [ ] Shutdown services:

  ```bash
  docker-compose down
  ```

  - [ ] All containers stop
  - [ ] No error messages
  - [ ] Completes in <30 seconds
  
- [ ] Verify volumes remain:

  ```bash
  docker volume ls | grep lucia
  ```

  - [ ] redis-data volume exists (without -v flag)
  
- [ ] Verify data persists after shutdown:

  ```bash
  docker-compose up -d
  sleep 10
  docker-compose exec redis redis-cli DBSIZE
  ```

  - [ ] Database size > 0 (data from before shutdown)

**Pass Criteria**: Graceful shutdown, data persistence maintained

---

## Sign-Off

**Tester Name**: ___________________  
**Date**: ___________________  
**Overall Status**: ☐ PASS  ☐ FAIL  ☐ CONDITIONAL

**Failed Tests**:

```

```

**Notes and Issues**:

```

```

**Performance Observations**:

```

```

**Recommendations for Production**:

```

```

---

## Next Steps

- [ ] Document all issues found
- [ ] Fix critical bugs before production deployment
- [ ] Run Docker MVP verification script
- [ ] Deploy to staging environment
- [ ] Proceed with Kubernetes deployment (if needed)
