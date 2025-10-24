# Docker Deployment Guide

> Last Updated: 2025-10-24  
> Status: Production Ready for Single-Node Deployments  
> Version: 1.0.0

## Overview

This guide covers deploying Lucia Agent Host using Docker Compose for single-node deployments. Ideal for:

- **Development environments** - Test locally before production
- **Home labs** - Small-scale smart home automation
- **Testing** - CI/CD pipelines and feature validation
- **Small production** - Single Home Assistant instance with low-traffic automation

**For high-availability production, see [Kubernetes Deployment Guide](../kubernetes/README.md)**

## Prerequisites

### Required
- Docker Engine 20.10+ ([Install](https://docs.docker.com/get-docker/))
- Docker Compose 2.0+ ([Install](https://docs.docker.com/compose/install/))
- 2GB available RAM (minimum 1GB for each service)
- 10GB free disk space (for logs and Redis persistence)

### Recommended
- docker-compose CLI for easier management
- curl or Postman for API testing
- Home Assistant 2024.10+ for best compatibility

### Optional
- Ollama for local LLM ([Install](https://ollama.ai/download)) if using local models
- Docker Desktop for GUI management
- ngrok or Cloudflare Tunnel for external access

## Quick Start (5 minutes)

### 1. Clone and Setup

```bash
# Clone the repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet

# Copy environment template
cp .env.example .env

# Edit configuration
nano .env
# Set required values:
#   - HOMEASSISTANT_URL: your Home Assistant URL
#   - HOMEASSISTANT_ACCESS_TOKEN: your HA access token
#   - ConnectionStrings__chat-model: your LLM provider configuration
```

### 2. Configure Environment

Edit `.env` with your values:

```bash
# Home Assistant
HOMEASSISTANT_URL=http://192.168.1.100:8123
HOMEASSISTANT_ACCESS_TOKEN=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

# OpenAI (recommended for MVP)
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
```

### 3. Start Services

```bash
# Start all services in background
docker-compose up -d

# View logs
docker-compose logs -f lucia

# Wait for startup (typically 10-30 seconds)
sleep 30

# Check health
curl http://localhost:5000/health
```

### 4. Verify Deployment

```bash
# List running containers
docker-compose ps

# Check service health
docker-compose exec lucia curl http://localhost:8080/health
docker-compose exec redis redis-cli PING

# View Redis data
docker-compose exec redis redis-cli --raw KEYS "lucia:*"
```

### 5. Test Agent API

```bash
# List available agents
curl http://localhost:5000/api/agents

# Send test message to Lucia
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Turn on the living room lights", "sessionId": "test-123"}'
```

## Configuration Guide

### Essential Variables

See [Configuration Reference](../configuration-reference.md) for all variables.

```env
# Home Assistant
HOMEASSISTANT_URL=http://homeassistant:8123
HOMEASSISTANT_ACCESS_TOKEN=<your-token>

# LLM Provider (choose one)
ConnectionStrings__chat-model=Endpoint=...;AccessKey=...;Model=...;Provider=openai

# Redis
REDIS_CONNECTION_STRING=redis://redis:6379
```

### LLM Provider Selection

#### OpenAI (Recommended for MVP)
- ✅ Easiest to setup
- ✅ Best model quality
- ✅ Extensive testing
- ❌ Costs per API call
- ❌ Requires internet

```env
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
```

**Setup:**
1. Create account at https://platform.openai.com
2. Generate API key at https://platform.openai.com/api-keys
3. Add credits ($5-$20 recommended for testing)
4. Copy key to `.env`

#### Ollama (Local Models - No Cost)
- ✅ Free and local
- ✅ No API calls
- ✅ Privacy-first
- ❌ Slower responses (depends on hardware)
- ❌ Requires GPU for performance

```env
ConnectionStrings__chat-model=Endpoint=http://ollama:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama
```

**Setup:**
1. Install Ollama from https://ollama.ai/download
2. Pull model: `ollama pull llama3.2`
3. Start Ollama: `ollama serve`
4. Update docker-compose to include Ollama service or use local network

#### Azure OpenAI (Production)
- ✅ Embeddings support (semantic search)
- ✅ Enterprise features
- ✅ Azure integration
- ❌ More complex setup
- ❌ Azure subscription required

```env
ConnectionStrings__chat-model=Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=gpt-4-deployment;Provider=azureopenai
```

**Setup:**
1. Create Azure account
2. Deploy Azure OpenAI service
3. Create deployments for models
4. Get endpoint and key from Azure Portal

### Advanced Configuration

#### Enable HTTPS (Production)

```bash
# Generate self-signed certificate
openssl req -x509 -newkey rsa:4096 -keyout ./certs/key.pem -out ./certs/cert.pem -days 365 -nodes

# Update .env
ENABLE_HTTPS=true
CERTIFICATE_PATH=/etc/lucia/certs/cert.pem
CERTIFICATE_KEY_PATH=/etc/lucia/certs/key.pem

# Restart services
docker-compose down
docker-compose up -d
```

#### Increase Resource Limits

Edit `docker-compose.yml` to increase resources:

```yaml
services:
  lucia:
    deploy:
      resources:
        limits:
          cpus: '4'
          memory: 2G
        reservations:
          cpus: '2'
          memory: 1G
```

#### Persistent Redis Data

Change Redis volume driver from tmpfs to local:

```yaml
volumes:
  redis-data:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/lucia/redis
```

## Deployment Scenarios

### Scenario 1: Development (Laptop/Desktop)

Perfect for testing features before production deployment.

```bash
# Setup
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet
cp .env.example .env
# Edit .env with test values

# Start
docker-compose up

# Test
curl http://localhost:5000/health

# Cleanup (removes all data)
docker-compose down -v
```

**Resources:** 1GB RAM minimum  
**Storage:** 5GB minimum  
**Time to running:** 5-10 minutes

### Scenario 2: Home Lab (Always-On Server)

Single Home Assistant instance with persistent data.

```bash
# Setup with persistent volumes
# Edit docker-compose.yml to use local driver for redis-data volume

docker-compose up -d

# Verify startup
docker-compose logs -f lucia

# Monitor
docker stats

# Backup Redis data
docker run --rm -v lucia-redis-data:/data -v /backup:/backup \
  redis:7-alpine cp -r /data/* /backup/
```

**Resources:** 2GB RAM, 4GB disk for Redis snapshots  
**Time to running:** 10-15 minutes  
**Data persistence:** Yes (survives container restart)

### Scenario 3: Staging Environment

Test production configuration before deploying to production.

```bash
# Create separate .env.staging
cp .env.example .env.staging
# Configure for staging environment

# Run with staging config
docker-compose -f docker-compose.yml \
  -f docker-compose.staging.yml up -d

# Test full automation flows
curl -X POST http://localhost:5000/api/automation \
  -H "Content-Type: application/json" \
  -d @tests/automation-scenario-1.json

# Validate logs
docker-compose logs lucia | grep ERROR
```

## Operational Tasks

### View Logs

```bash
# All services
docker-compose logs

# Lucia only
docker-compose logs lucia

# Follow real-time (tail -f equivalent)
docker-compose logs -f lucia

# Last 100 lines
docker-compose logs --tail=100 lucia

# Since specific time
docker-compose logs --since 2024-10-24T10:00:00 lucia
```

### Health Checks

```bash
# Check service status
docker-compose ps

# Lucia health endpoint
curl http://localhost:5000/health

# Redis connectivity
docker-compose exec redis redis-cli PING

# Full diagnostics
./infra/scripts/health-check.sh
```

### Restart Services

```bash
# Restart Lucia (keeps Redis running)
docker-compose restart lucia

# Restart everything
docker-compose restart

# Full cycle (down/up)
docker-compose down
docker-compose up -d
```

### Backup and Restore

#### Backup Redis Data

```bash
# Export Redis data
docker-compose exec redis redis-cli --rdb /data/dump-backup.rdb

# Copy to host
docker cp lucia-redis:/data/dump-backup.rdb ./backups/

# Or backup entire volume
docker run --rm -v lucia-redis-data:/data -v /backups:/backups \
  redis:7-alpine tar czf /backups/redis-$(date +%Y%m%d).tar.gz -C / data
```

#### Restore Redis Data

```bash
# Stop services
docker-compose down

# Clear existing data
docker volume rm lucia-redis-data

# Restore from backup
docker run --rm -v lucia-redis-data:/data -v /backups:/backups \
  redis:7-alpine tar xzf /backups/redis-20241024.tar.gz -C /

# Restart
docker-compose up -d
```

### Update Application

```bash
# Pull latest code
git pull origin main

# Rebuild image
docker-compose build --no-cache lucia

# Restart with new image
docker-compose down
docker-compose up -d
```

## Troubleshooting

### Services Won't Start

```bash
# Check logs
docker-compose logs lucia

# Verify configuration
./infra/scripts/validate-deployment.sh

# Common issues:
# - Missing .env file: cp .env.example .env
# - Invalid configuration values in .env
# - Port already in use: docker-compose up -p 5010:8080
```

### High Memory Usage

```bash
# Check memory
docker stats

# Reduce LLM max tokens
# Edit .env: LLM_MAX_TOKENS=500

# Limit container memory
# Edit docker-compose.yml resources section

# Restart
docker-compose restart lucia
```

### Redis Persistence Issues

```bash
# Check Redis logs
docker-compose logs redis

# Verify Redis data
docker-compose exec redis redis-cli DBSIZE

# Clear Redis (destructive)
docker-compose exec redis redis-cli FLUSHALL

# Restart Redis
docker-compose restart redis
```

### Home Assistant Connection Fails

```bash
# Verify HA URL
curl http://192.168.1.100:8123

# Test token
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://192.168.1.100:8123/api/

# Update .env with correct URL and token
# Restart: docker-compose restart lucia
```

### LLM API Errors

```bash
# Test OpenAI connection
curl -H "Authorization: Bearer sk-proj-YOUR_KEY" \
  https://api.openai.com/v1/models

# Check token validity and quotas
# Visit: https://platform.openai.com/account/billing/overview

# For Ollama, verify it's running
curl http://localhost:11434/api/tags
```

## Performance Tuning

### CPU Usage High

```bash
# Reduce LLM max tokens in .env
LLM_MAX_TOKENS=500

# Reduce agent concurrency
CHAT_MODEL_MAX_CONCURRENT_REQUESTS=2

# Reduce retry count
AGENT_MAX_RETRIES=2
```

### Response Time Slow

```bash
# Check LLM provider response times
# Edit .env: LLM_TEMPERATURE=0.3  # More deterministic

# For Ollama, use smaller model
Model=phi3:mini  # Smaller and faster than llama3.2

# Increase allocated memory
# docker-compose.yml: memory: 2G
```

### Redis Performance

```bash
# Monitor Redis stats
docker-compose exec redis redis-cli INFO stats

# Clear old data
docker-compose exec redis redis-cli EVAL "return redis.call('eval', \"return redis.call('del', unpack(redis.call('keys', 'lucia:*:expired'))))\", 0)"

# Resize maxmemory in redis.conf if needed
```

## Security Considerations

### Never commit .env

```bash
# Verify .env is ignored
git status  # Should NOT show .env

# If accidentally committed, remove:
git rm --cached .env
echo ".env" >> .gitignore
git commit -m "Remove .env from version control"
```

### Restrict Port Access

```bash
# Current: 127.0.0.1:5000 (localhost only, safe)
# DO NOT change to 0.0.0.0:5000 without authentication

# For remote access, use:
# - nginx reverse proxy with authentication
# - VPN to Docker host
# - Cloudflare Tunnel
```

### Rotate Credentials Regularly

```bash
# Every 90 days:
# 1. Generate new Home Assistant token
# 2. Generate new OpenAI API key
# 3. Update .env
# 4. Restart: docker-compose restart lucia
```

## Next Steps

1. **Monitor in Production** - Set up log aggregation and alerts
2. **Scale Horizontally** - Deploy multiple instances with load balancer
3. **Upgrade to Kubernetes** - For HA and advanced features
4. **Add Monitoring** - Integrate OpenTelemetry for tracing and metrics

See [Kubernetes Deployment Guide](../kubernetes/README.md) for HA setup.

