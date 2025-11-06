# Docker Deployment for Lucia Agent Host

> Last Updated: 2025-10-24  
> Status: MVP Complete  
> Version: 1.0.0

## Overview

This directory contains Docker configurations for deploying Lucia Agent Host in containerized environments.

## Directory Structure

```
infra/docker/
├── Dockerfile.agenthost      # Multi-stage Dockerfile for Lucia application
├── DEPLOYMENT.md             # Complete deployment guide
├── TESTING.md                # Testing guide and procedures
├── TESTING-CHECKLIST.md      # Manual testing checklist
├── verify-mvp.sh             # Automated verification script
└── README.md                 # This file
```

See also:

- `docker-compose.yml` - Docker Compose configuration (in project root)
- `.env.example` - Environment variables template (in project root)
- `redis:8.2-alpine` - Official Redis image used (no custom Dockerfile needed)

## Quick Start

### 1. Setup (5 minutes)

```bash
# Clone repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet

# Copy environment template
cp .env.example .env

# Edit configuration
nano .env
# Required: HOMEASSISTANT_URL, HOMEASSISTANT_ACCESS_TOKEN, ConnectionStrings__chat-model
```

### 2. Deploy (2 minutes)

```bash
# Start services
docker-compose up -d

# Wait for startup
sleep 30

# Verify
docker-compose ps
curl http://localhost:5000/health
```

### 3. Test (5 minutes)

```bash
# Run automated verification
./infra/docker/verify-mvp.sh

# Or manual testing
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello", "sessionId": "test-001"}'
```

## Key Files

### Dockerfile.agenthost

Production-ready multi-stage Dockerfile for lucia.AgentHost service.

**Features:**

- ✅ Multi-stage builds (minimal final image)
- ✅ Non-root user (security)
- ✅ Health checks
- ✅ Build arguments for flexibility
- ✅ Optimized caching layers

**Build:**

```bash
docker build -t lucia-agenthost:latest -f infra/docker/Dockerfile.agenthost .
```

**Stages:**

1. `base` - Runtime base image (ASP.NET Core)
2. `build` - Build stage with SDK
3. `publish` - Publish optimized binaries
4. `final` - Production image

### Redis Service

Uses official **redis:8.2-alpine** image with command-line configuration for persistence.

**Features:**

- ✅ AOF (Append-Only File) persistence enabled
- ✅ Memory limits (256MB default)
- ✅ Automatic eviction policy
- ✅ Health checks
- ✅ Minimal footprint (alpine base)
- ✅ No custom Dockerfile needed (uses official image)

**Configuration via docker-compose.yml:**

```yaml
redis:
  image: redis:8.2-alpine
  command: >
    redis-server
    --appendonly yes
    --maxmemory 256mb
    --maxmemory-policy allkeys-lru
```

## Docker Compose

### Services

**lucia**

- Image: `lucia-agenthost:latest` (built from Dockerfile.agenthost)
- Port: `127.0.0.1:5000` (HTTP API)
- Health: Checked via `/health` endpoint
- Restart: Unless stopped
- Resource Limits: CPU=2, Memory=1GB

**redis**

- Image: `redis:7-alpine`
- Port: `127.0.0.1:6379` (Redis protocol)
- Health: Checked via `PING` command
- Restart: Unless stopped
- Resource Limits: CPU=1, Memory=512MB
- Volume: `redis-data` for persistence

### Networks

`lucia-network` - Bridge network for service-to-service communication

### Volumes

`redis-data` - Persistent Redis data store

## Configuration

### Environment Variables

See `.env.example` for all variables. Essential variables:

```env
# Home Assistant
HOMEASSISTANT_URL=http://homeassistant:8123
HOMEASSISTANT_ACCESS_TOKEN=eyJ...

# LLM Provider
ConnectionStrings__chat-model=Endpoint=...;AccessKey=...;Model=...;Provider=openai

# Redis
REDIS_CONNECTION_STRING=redis://redis:6379
```

### LLM Providers

#### OpenAI (Recommended for MVP)

```env
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
```

#### Ollama (Local LLM)

```env
ConnectionStrings__chat-model=Endpoint=http://ollama:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama
```

#### Azure OpenAI (With Embeddings)

```env
ConnectionStrings__chat-model=Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=gpt-4-deployment;Provider=azureopenai
```

See [Configuration Reference](../docs/configuration-reference.md) for complete details.

## Common Operations

### Startup

```bash
# Start all services in background
docker-compose up -d

# Start and show logs
docker-compose up

# Start specific service
docker-compose up -d lucia
```

### View Logs

```bash
# All services
docker-compose logs

# Lucia only
docker-compose logs lucia

# Follow real-time
docker-compose logs -f lucia

# Last 100 lines
docker-compose logs --tail=100 lucia
```

### Health Checks

```bash
# Service status
docker-compose ps

# Lucia API health
curl http://localhost:5000/health

# Redis health
docker-compose exec redis redis-cli PING
```

### Restart Services

```bash
# Restart Lucia only
docker-compose restart lucia

# Restart all
docker-compose restart

# Full cycle
docker-compose down
docker-compose up -d
```

### Shutdown

```bash
# Stop services (keep volumes)
docker-compose stop

# Shutdown and remove containers
docker-compose down

# Shutdown and remove volumes (destructive!)
docker-compose down -v
```

### Backup/Restore

```bash
# Backup Redis data
docker run --rm -v lucia-redis-data:/data -v ./backup:/backup \
  redis:7-alpine tar czf /backup/redis-backup.tar.gz -C / data

# Restore Redis data
docker volume rm lucia-redis-data
docker run --rm -v lucia-redis-data:/data -v ./backup:/backup \
  redis:7-alpine tar xzf /backup/redis-backup.tar.gz -C /
```

## Troubleshooting

### Services won't start

```bash
# Check logs
docker-compose logs lucia

# Validate configuration
./infra/scripts/validate-deployment.sh

# Check ports not in use
netstat -an | grep 5000
```

### High memory usage

```bash
# Monitor
docker stats

# Reduce limits in docker-compose.yml
# Restart: docker-compose down && docker-compose up -d
```

### Home Assistant connection fails

```bash
# Test connection
curl http://192.168.1.100:8123

# Check token
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://192.168.1.100:8123/api/

# Update .env and restart
```

### LLM API errors

```bash
# Test OpenAI
curl -H "Authorization: Bearer sk-proj-YOUR_KEY" \
  https://api.openai.com/v1/models

# For Ollama
curl http://localhost:11434/api/tags
```

See [DEPLOYMENT.md](DEPLOYMENT.md) for comprehensive troubleshooting.

## Testing

### Automated Verification

```bash
./infra/docker/verify-mvp.sh
```

Checks:

- ✅ Services running
- ✅ Health endpoints responding
- ✅ API endpoints working
- ✅ Database connectivity
- ✅ Configuration validation
- ✅ Integration tests
- ✅ Performance checks

### Manual Testing

See [TESTING.md](TESTING.md) for:

- Core functionality tests
- Integration tests
- Performance testing
- Debugging techniques

### Testing Checklist

See [TESTING-CHECKLIST.md](TESTING-CHECKLIST.md) for:

- Pre-testing prerequisites
- 12 comprehensive test scenarios
- Sign-off documentation

## Deployment Scenarios

### Development (Local Machine)

```bash
docker-compose up
# Test and iterate
docker-compose down -v  # Clean up
```

### Home Lab (Always-On)

```bash
# Edit docker-compose.yml to use persistent volumes
docker-compose up -d
# Monitor: docker stats
# Backup regularly: tar czf backup-$(date +%Y%m%d).tar.gz /data/lucia
```

### Staging (Pre-Production)

```bash
docker-compose -f docker-compose.yml -f docker-compose.staging.yml up -d
# Run full test suite
./infra/docker/verify-mvp.sh
```

## Performance Tuning

### CPU Usage High

- Reduce `LLM_MAX_TOKENS` in .env
- Reduce `CHAT_MODEL_MAX_CONCURRENT_REQUESTS`
- Use smaller LLM model (Ollama: phi3:mini)

### Memory Usage High

- Reduce Redis `maxmemory` in redis.conf
- Reduce Lucia container memory limit in docker-compose.yml
- Reduce conversation history buffer size

### Response Time Slow

- Check LLM provider response times
- Reduce `LLM_TEMPERATURE` for faster responses
- Increase allocated memory to Redis

## Security Considerations

### Production Checklist

- [ ] `.env` not committed to git
- [ ] Ports restricted to localhost only
- [ ] HTTPS enabled (`ENABLE_HTTPS=true`)
- [ ] Credentials rotated regularly
- [ ] Network segmentation (not exposed to internet)
- [ ] Regular backups of Redis data
- [ ] Log aggregation and monitoring enabled

### Network Security

```bash
# Current: localhost only (safe)
ports:
  - "127.0.0.1:5000:8080"

# DO NOT change to:
# ports:
#   - "0.0.0.0:5000:8080"  # Exposes to network!

# For remote access use:
# - nginx reverse proxy with authentication
# - VPN to Docker host
# - Cloudflare Tunnel
```

## Next Steps

1. **Deploy** - Follow [DEPLOYMENT.md](DEPLOYMENT.md)
2. **Test** - Use [TESTING.md](TESTING.md) or [TESTING-CHECKLIST.md](TESTING-CHECKLIST.md)
3. **Verify** - Run `./verify-mvp.sh`
4. **Monitor** - Setup OpenTelemetry integration
5. **Scale** - Consider Kubernetes for HA

## Documentation

- [Deployment Guide](DEPLOYMENT.md) - Comprehensive deployment walkthrough
- [Testing Guide](TESTING.md) - Testing procedures and debugging
- [Testing Checklist](TESTING-CHECKLIST.md) - Manual test scenarios
- [Verification Script](verify-mvp.sh) - Automated verification

## Support

For issues:

1. Check logs: `docker-compose logs lucia`
2. Run verification: `./infra/docker/verify-mvp.sh`
3. See [DEPLOYMENT.md](DEPLOYMENT.md) troubleshooting section
4. Check [Configuration Reference](../docs/configuration-reference.md)
