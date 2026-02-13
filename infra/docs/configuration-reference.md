# Configuration Reference

Complete reference for all Lucia configuration options, environment variables, and settings across deployment methods.

---

## Overview

Lucia configuration is centralized around a set of core environment variables that work identically across all deployment methods:

- **Docker Compose**: Via `.env` file
- **Kubernetes**: Via ConfigMap and Secrets
- **systemd**: Via EnvironmentFile
- **CI/CD**: Via GitHub Secrets and Actions variables

This document provides the authoritative reference for all configuration options.

---

## Core Environment Variables

### Application Settings

#### `LUCIA_PORT`

- **Description**: HTTP port for the Lucia API server
- **Type**: `integer`
- **Default**: `5000`
- **Range**: `1-65535`
- **Example**: `LUCIA_PORT=5000`
- **Used by**: Docker (port mapping), Kubernetes (service port), systemd (ExecStart)
- **Impact**: Change if port already in use on host

#### `LUCIA_ENV`

- **Description**: Deployment environment name
- **Type**: `string` (enum)
- **Allowed values**: `development`, `staging`, `production`
- **Default**: `production`
- **Example**: `LUCIA_ENV=production`
- **Used by**: Logging level, error handling verbosity, feature flags
- **Impact**: `development` enables debug logs, `production` reduces verbosity

#### `LUCIA_LOG_LEVEL`

- **Description**: Minimum log level to capture
- **Type**: `string` (enum)
- **Allowed values**: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`
- **Default**: `Information`
- **Example**: `LUCIA_LOG_LEVEL=Information`
- **Used by**: All logging infrastructure
- **Impact**: Higher levels (Debug, Trace) significantly increase log volume

#### `LUCIA_ENABLE_TELEMETRY`

- **Description**: Enable OpenTelemetry instrumentation
- **Type**: `boolean`
- **Allowed values**: `true`, `false`
- **Default**: `true`
- **Example**: `LUCIA_ENABLE_TELEMETRY=true`
- **Used by**: Agent Framework components and application spans
- **Impact**: Disabling reduces overhead but loses observability

---

### Home Assistant Integration

#### `HOMEASSISTANT_URL`

- **Description**: URL to Home Assistant instance
- **Type**: `url`
- **Required**: `true`
- **Example**: `HOMEASSISTANT_URL=http://192.168.1.100:8123`
- **Validation**: Must be valid HTTP/HTTPS URL, reachable from Lucia container
- **Impact**: Core integration - app won't start without this

#### `HOMEASSISTANT_ACCESS_TOKEN`

- **Description**: Long-lived access token for Home Assistant API
- **Type**: `string` (secret)
- **Required**: `true`
- **Example**: `HOMEASSISTANT_ACCESS_TOKEN=eyJhbGciOiJIUzI1N...`
- **Security**: Store as Kubernetes Secret or Docker Secret, never in git
- **Generation**: Create in Home Assistant UI → Settings → Long-Lived Access Tokens
- **Impact**: Required for all Home Assistant API calls

#### `HOMEASSISTANT_CONNECTION_TIMEOUT`

- **Description**: Timeout for Home Assistant API connections
- **Type**: `integer` (seconds)
- **Default**: `30`
- **Range**: `5-300`
- **Example**: `HOMEASSISTANT_CONNECTION_TIMEOUT=30`
- **Impact**: Increase if network is slow or HA instance is remote

#### `HOMEASSISTANT_RETRY_COUNT`

- **Description**: Number of retries for failed Home Assistant API calls
- **Type**: `integer`
- **Default**: `3`
- **Range**: `0-10`
- **Example**: `HOMEASSISTANT_RETRY_COUNT=3`
- **Impact**: Higher values = more resilient but slower failure response

---

### Redis Configuration

#### `REDIS_CONNECTION_STRING`

- **Description**: Redis connection string for task persistence
- **Type**: `redis-connection-string`
- **Required**: `true`
- **Examples**:
  - Docker (same network): `redis://redis:6379`
  - Local: `redis://localhost:6379`
  - Remote: `redis://redis.example.com:6379`
  - With password: `redis://:password@redis:6379`
- **Format**: `redis://[user:password@]host[:port][/database]`
- **Impact**: All task state stored in Redis; unavailable = tasks reset

#### `REDIS_TIMEOUT`

- **Description**: Timeout for Redis operations
- **Type**: `integer` (milliseconds)
- **Default**: `5000`
- **Range**: `100-60000`
- **Example**: `REDIS_TIMEOUT=5000`
- **Impact**: Increase if Redis is slow or over network

#### `REDIS_KEY_PREFIX`

- **Description**: Prefix for all Redis keys to avoid collisions
- **Type**: `string`
- **Default**: `lucia:`
- **Example**: `REDIS_KEY_PREFIX=lucia-prod:`
- **Impact**: Useful if sharing Redis with other applications

#### `REDIS_PERSISTENCE_TTL`

- **Description**: Time-to-live for task persistence
- **Type**: `integer` (hours)
- **Default**: `24`
- **Range**: `1-720`
- **Example**: `REDIS_PERSISTENCE_TTL=24`
- **Impact**: Tasks older than TTL are automatically cleaned up

---

### LLM and Chat Model Configuration

Lucia uses a unified connection string format for chat model configuration, supporting multiple LLM providers through the `ConnectionStrings__chat-model` variable.

#### `ConnectionStrings__chat-model`

- **Description**: Connection string for chat model provider with unified format
- **Type**: `connection-string`
- **Required**: `true`
- **Format**: `Endpoint=<endpoint>;AccessKey=<key>;Model=<model_name>;Provider=<provider>`
- **Supported Providers**: `openai`, `azureopenai`, `ollama`, `azureinference`
- **Examples**:
  - **OpenAI**: `Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai`
  - **Azure OpenAI**: `Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=gpt-4-deployment-name;Provider=azureopenai`
  - **Ollama (Local)**: `Endpoint=http://localhost:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama`
  - **Azure AI Inference**: `Endpoint=https://YOUR_RESOURCE.inference.ai.azure.com/;AccessKey=YOUR_KEY;Model=model-name;Provider=azureinference`
- **Validation**: Parser validates format and provider compatibility
- **Impact**: Controls which LLM provider is used for chat completions

#### Embedding Model Support

**Current Status**: ✅ Azure OpenAI Only

Embeddings for semantic search are currently supported exclusively on Azure OpenAI deployments.

- **Supported on**: `azureopenai` provider only
- **Future Plans**: Support for embeddings on other providers (OpenAI, Ollama, etc.) is planned for future releases
- **Current Behavior**: If using non-Azure providers, semantic search functionality is limited
- **Azure Configuration**: Embeddings automatically configured for Azure OpenAI via `AddChatClient()` and `AddKeyedChatClient()` extension methods
- **Model Used**: `text-embedding-3-small` (configurable in code)
- **Supported Providers**: Azure OpenAI only (via `azureopenai` provider)
- **Future**: Embeddings for other providers planned for future releases

---

### Agent Configuration

#### `AGENT_REGISTRY_URL`

- **Description**: Internal URL for agent registry API
- **Type**: `url`
- **Default**: `http://localhost:5001` (Docker: `http://lucia-agent-registry:5001`)
- **Example**: `AGENT_REGISTRY_URL=http://lucia-agent-registry:5001`
- **Impact**: Must be reachable from agent containers

#### `AGENT_TIMEOUT`

- **Description**: Timeout for agent-to-agent communication
- **Type**: `integer` (seconds)
- **Default**: `30`
- **Range**: `5-300`
- **Example**: `AGENT_TIMEOUT=30`
- **Impact**: Must be larger than slowest agent operation

#### `AGENT_RETRY_POLICY`

- **Description**: Retry behavior for agent failures
- **Type**: `string` (enum)
- **Allowed values**: `none`, `exponential-backoff`, `fixed-interval`
- **Default**: `exponential-backoff`
- **Example**: `AGENT_RETRY_POLICY=exponential-backoff`
- **Impact**: `none` = fail fast, others = resilient but slower

---

### Security & HTTPS

#### `ENABLE_HTTPS`

- **Description**: Enable HTTPS/TLS for API endpoints
- **Type**: `boolean`
- **Allowed values**: `true`, `false`
- **Default**: `false`
- **Example**: `ENABLE_HTTPS=false` (Docker), `true` (production)
- **Impact**: Required for production deployments

#### `CERTIFICATE_PATH`

- **Description**: Path to TLS certificate file
- **Type**: `file-path`
- **Required if**: `ENABLE_HTTPS=true`
- **Example**: `/etc/lucia/certs/cert.pem`
- **Kubernetes**: Mount from Secret
- **Docker**: Mount from volume
- **systemd**: Path on filesystem
- **Impact**: Certificate must be valid and not expired

#### `CERTIFICATE_KEY_PATH`

- **Description**: Path to TLS private key file
- **Type**: `file-path`
- **Required if**: `ENABLE_HTTPS=true`
- **Example**: `/etc/lucia/certs/key.pem`
- **Security**: Restrict to Lucia process user only
- **Impact**: Private key compromise = security breach

#### `ALLOWED_ORIGINS`

- **Description**: CORS origins allowed to call Lucia API
- **Type**: `string` (comma-separated URLs)
- **Default**: `http://localhost:3000`
- **Example**: `ALLOWED_ORIGINS=http://localhost:3000,https://home.example.com`
- **Impact**: Protects API from unauthorized cross-origin calls

---

## Configuration Schema Files

Reference schema files for each deployment method:

### Docker Compose Schema

See: `contracts/docker-compose-schema.yml`

- Defines compose service configuration
- Volume mounts, port mappings, environment variables
- Health check definitions
- Resource limits and restart policies

### Kubernetes Values Schema

See: `contracts/kubernetes-values-schema.yml`

- Helm values for Kubernetes deployment
- Replica counts, resource requests/limits
- ConfigMap and Secret definitions
- Ingress and PersistentVolume configuration

### systemd Environment Schema

See: `contracts/systemd-env-schema.md`

- Environment file format
- Variable validation rules
- Expansion rules (allows referencing other variables)
- Security considerations

---

## Configuration Validation

### Validation Rules

All configuration is validated on startup. Invalid values cause the application to fail fast with clear error messages.

**Validation checks**:

1. Required variables present
2. Variable types match specification
3. URLs are valid and reachable
4. Numeric ranges within acceptable bounds
5. Secret values are not empty
6. File paths exist and are readable
7. Connection strings can establish connections

### Validation Errors

Example error messages:

```
ERROR: Configuration validation failed
  - HOMEASSISTANT_URL: Invalid URL format
  - REDIS_CONNECTION_STRING: Cannot connect to Redis
  - OPENAI_API_KEY: Empty or invalid API key
```

### Troubleshooting Configuration

**Application won't start**:

1. Check logs: `docker logs lucia` or `journalctl -u lucia`
2. Validate all required variables are set
3. Test connectivity: `curl $HOMEASSISTANT_URL`
4. Verify secrets aren't corrupted or empty

**Intermittent failures**:

1. Increase timeout values (connection, agent, redis)
2. Increase retry counts
3. Check network connectivity
4. Check Remote service (Home Assistant, Redis, LLM) health

---

## Configuration by Deployment Method

### Docker Compose

Create `.env` file in project root:

```bash
# Copy example
cp .env.example .env

# Edit with your values
nano .env
```

Variables in `.env` file override defaults in Dockerfile.

**Best practices**:

- Never commit `.env` to git (already in .gitignore)
- Use `.env.example` to document required variables
- Keep secrets in `.env` only for development
- In production, use Docker Secrets or volume mounts

### Kubernetes

Configuration via ConfigMap and Secrets:

```bash
# Create ConfigMap for non-secret config
kubectl create configmap lucia-config \
  --from-literal=LUCIA_ENV=production \
  --from-literal=LUCIA_LOG_LEVEL=Information

# Create Secret for sensitive data
kubectl create secret generic lucia-secrets \
  --from-literal=OPENAI_API_KEY=sk-... \
  --from-literal=HOMEASSISTANT_ACCESS_TOKEN=eyJ...
```

Pod references both ConfigMap and Secrets in spec.

**Best practices**:

- Store secrets in Kubernetes Secrets, not ConfigMaps
- Use Sealed Secrets or External Secrets operator for encryption
- Update ConfigMaps separately from pod to enable rolling updates
- Document ConfigMap structure in Helm values

### systemd

Configuration via EnvironmentFile:

```bash
sudo nano /etc/lucia/lucia.env

# Format:
# VARIABLE_NAME=value
# Values with spaces need quotes: "value with spaces"
# Supports variable expansion: $VARIABLE_NAME
```

Service file references environment file:

```ini
[Service]
EnvironmentFile=/etc/lucia/lucia.env
```

**Best practices**:

- Restrict file permissions: `chmod 600 /etc/lucia/lucia.env`
- Use separate files for secrets and non-secrets
- Back up environment file with application
- Document custom variables used

### CI/CD (GitHub Actions)

Configuration via Repository Secrets and Variables:

```yaml
# Repository Secrets (Settings → Secrets and variables → Actions)
- OPENAI_API_KEY
- HOMEASSISTANT_ACCESS_TOKEN
- AZURE_OPENAI_KEY

# Repository Variables (Settings → Secrets and variables → Variables)
- LUCIA_ENV = production
- LLM_PROVIDER = openai
- HOMEASSISTANT_URL = http://ha.example.com
```

Workflow accesses via `${{ secrets.VARIABLE_NAME }}` or `${{ vars.VARIABLE_NAME }}`.

**Best practices**:

- Use Secrets for sensitive data (API keys, tokens)
- Use Variables for non-sensitive config (URLs, environments)
- Never log secrets in workflow output
- Rotate secrets periodically

---

## Quick Start Configuration

### Minimal Configuration (Docker, Single Home Assistant)

```env
# Required
HOMEASSISTANT_URL=http://192.168.1.100:8123
HOMEASSISTANT_ACCESS_TOKEN=eyJhbGciOiJIUzI1...
REDIS_CONNECTION_STRING=redis://redis:6379
OPENAI_API_KEY=sk-...

# Optional (uses defaults)
LUCIA_PORT=5000
LUCIA_ENV=production
LLM_PROVIDER=openai
```

### Production Configuration (Kubernetes with Backups)

```yaml
# ConfigMap
LUCIA_ENV: production
LUCIA_LOG_LEVEL: Warning
LUCIA_ENABLE_TELEMETRY: "true"
REDIS_PERSISTENCE_TTL: "48"
AGENT_RETRY_POLICY: exponential-backoff

# Secrets
HOMEASSISTANT_ACCESS_TOKEN: <token>
OPENAI_API_KEY: <key>
CERTIFICATE_PATH: /etc/lucia/certs/cert.pem
CERTIFICATE_KEY_PATH: /etc/lucia/certs/key.pem
```

### Development Configuration (Local Testing)

```env
LUCIA_ENV=development
LUCIA_LOG_LEVEL=Debug
LUCIA_ENABLE_TELEMETRY=false
REDIS_CONNECTION_STRING=redis://localhost:6379
LLM_PROVIDER=ollama
OLLAMA_ENDPOINT=http://localhost:11434
```

---

## Configuration Best Practices

### Security

1. **Never commit secrets** to version control
2. **Use deployment method secrets**: Docker Secrets, K8s Secrets, encrypted EnvironmentFile
3. **Restrict file permissions**: 600 for files containing secrets
4. **Rotate credentials** regularly (API keys, access tokens)
5. **Use HTTPS in production** with valid certificates
6. **Enable RBAC** if using Kubernetes
7. **Audit configuration changes** in production

### Reliability

1. **Set realistic timeouts** for your network latency
2. **Enable retry policies** for external services
3. **Configure appropriate log levels**: Debug in dev, Warning in prod
4. **Monitor metrics** in production (OpenTelemetry)
5. **Document custom variables** for your environment
6. **Test configuration changes** before production

### Performance

1. **Adjust `LLM_MAX_TOKENS`** based on use case (lower = faster)
2. **Tune `AGENT_TIMEOUT`** for your home automation complexity
3. **Set `REDIS_PERSISTENCE_TTL`** based on retention needs
4. **Configure resource limits** in Kubernetes/Docker
5. **Monitor application metrics** to identify bottlenecks

---

## Troubleshooting Guide

### Variable Not Being Applied

**Problem**: Changed environment variable but application behavior unchanged

**Solutions**:

1. Verify variable is spelled correctly (case-sensitive)
2. Restart application: `docker restart lucia` or `sudo systemctl restart lucia`
3. Check logs for validation errors: `docker logs lucia` or `journalctl -u lucia`
4. Confirm variable reaches application: Add debug logging

### Connection Failures

**Problem**: Application can't connect to Home Assistant or Redis

**Solutions**:

1. Verify URL is correct: `curl $HOMEASSISTANT_URL`
2. Check network connectivity: `ping redis` (Docker) or `netstat -an | grep 6379` (systemd)
3. Verify credentials: Test token manually with curl
4. Check firewall rules if services on different hosts
5. Increase timeout if network is slow

### Performance Issues

**Problem**: Slow responses or high latency

**Solutions**:

1. Check `LLM_TEMPERATURE` (lower = faster)
2. Reduce `LLM_MAX_TOKENS` if responses truncated
3. Increase `AGENT_TIMEOUT` if agents timing out
4. Check resource usage: `docker stats` or `free -h`
5. Monitor OpenTelemetry traces for bottlenecks

---

**Last Updated**: 2025-10-24  
**Version**: 1.0.0  
**Maintained by**: Lucia DevOps Team
