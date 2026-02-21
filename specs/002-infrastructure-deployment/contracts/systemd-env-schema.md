# systemd Environment File Schema

This schema defines the structure and validation rules for the systemd environment file (`lucia.env`) used in Linux service deployments.

## File Location

**Standard Path:** `/etc/lucia/lucia.env`  
**Alternative Path:** `~/.config/lucia/lucia.env` (user-specific)

## Environment File Format

```ini
# Environment Variable Schema for systemd deployment
# Format: KEY=VALUE (no spaces around =, no quotes unless value contains spaces)
```

## Required Variables

### 1. Home Assistant Configuration

```ini
HomeAssistant__BaseUrl=<REQUIRED>
  Type: URL
  Format: http(s)://hostname:port
  Example: http://192.168.1.100:8123
  Validation: Must be valid HTTP/HTTPS URL
  Description: Base URL of your Home Assistant instance

HomeAssistant__AccessToken=<REQUIRED>
  Type: String
  MinLength: 32
  Example: eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiI...
  Validation: Must be non-empty, at least 32 characters
  Description: Long-lived access token from Home Assistant
  Sensitive: YES
```

### 2. OpenAI / LLM Configuration

```ini
OpenAI__ApiKey=<REQUIRED>
  Type: String
  MinLength: 20
  Example: sk-proj-abcd1234... OR "ollama" for local
  Validation: Must be non-empty, at least 20 characters
  Description: API key for OpenAI or compatible provider
  Sensitive: YES

OpenAI__BaseUrl=<OPTIONAL>
  Type: URL
  Default: https://api.openai.com/v1
  Example: http://localhost:11434/v1 (Ollama)
  Validation: Must be valid HTTP/HTTPS URL if provided
  Description: Base URL for OpenAI-compatible API endpoint

OpenAI__ModelId=<REQUIRED>
  Type: String
  Example: gpt-4o, llama3.2, mistral
  Validation: Must be non-empty string
  Description: Model identifier for chat completion

OpenAI__EmbeddingModelId=<REQUIRED>
  Type: String
  Example: text-embedding-3-small, mxbai-embed-large
  Validation: Must be non-empty string
  Description: Model identifier for text embeddings
```

### 3. Redis Configuration

```ini
Redis__ConnectionString=<OPTIONAL>
  Type: String
  Default: localhost:6379
  Example: localhost:6379, redis-server:6379
  Format: hostname:port
  Validation: Must match hostname:port pattern
  Description: Redis server connection string

Redis__Password=<OPTIONAL>
  Type: String
  Default: (empty - no authentication)
  Example: mysecretpassword
  Validation: Any string
  Description: Redis authentication password if required
  Sensitive: YES
```

## Optional Variables

### 4. Logging Configuration

```ini
Logging__LogLevel__Default=<OPTIONAL>
  Type: Enum
  Default: Information
  Allowed: Trace, Debug, Information, Warning, Error, Critical
  Example: Information
  Validation: Must be one of allowed values (case-sensitive)
  Description: Default log level for application

Logging__LogLevel__Microsoft__AspNetCore=<OPTIONAL>
  Type: Enum
  Default: Warning
  Example: Warning
  Description: Log level for ASP.NET Core framework logs

Logging__LogLevel__Microsoft__Agents=<OPTIONAL>
  Type: Enum
  Default: Information
  Example: Information
  Description: Log level for Agent Framework logs
```

### 5. ASP.NET Core Configuration

```ini
ASPNETCORE_ENVIRONMENT=<OPTIONAL>
  Type: Enum
  Default: Production
  Allowed: Development, Staging, Production
  Example: Production
  Validation: Must be one of allowed values
  Description: Application environment

ASPNETCORE_URLS=<OPTIONAL>
  Type: String
  Default: http://+:8080
  Example: http://0.0.0.0:7235
  Format: http(s)://hostname:port or http://+:port
  Validation: Must be valid URL format
  Description: URLs the application listens on
```

## Complete Example File

### Production Configuration (Cloud LLM)

```ini
# /etc/lucia/lucia.env

# Home Assistant
HomeAssistant__BaseUrl=http://192.168.1.100:8123
HomeAssistant__AccessToken=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiI5ZjE...

# OpenAI Configuration
OpenAI__ApiKey=sk-proj-abc123xyz789...
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__ModelId=gpt-4o
OpenAI__EmbeddingModelId=text-embedding-3-small

# Redis
Redis__ConnectionString=localhost:6379

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft__AspNetCore=Warning

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
```

### Development Configuration (Local LLM with Ollama)

```ini
# ~/.config/lucia/lucia.env

# Home Assistant
HomeAssistant__BaseUrl=http://localhost:8123
HomeAssistant__AccessToken=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...

# Ollama Configuration (Local)
OpenAI__ApiKey=ollama
OpenAI__BaseUrl=http://localhost:11434/v1
OpenAI__ModelId=llama3.2:latest
OpenAI__EmbeddingModelId=mxbai-embed-large

# Redis
Redis__ConnectionString=localhost:6379

# Logging
Logging__LogLevel__Default=Debug
Logging__LogLevel__Microsoft__AspNetCore=Information
Logging__LogLevel__Microsoft__Agents=Debug

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:7235
```

## Validation Rules

### Format Rules
1. **No spaces around `=`**: `KEY=VALUE` not `KEY = VALUE`
2. **No quotes unless needed**: Use quotes only if value contains spaces
3. **Comments start with `#`**: Inline comments not supported
4. **One variable per line**: No multi-line values

### Security Rules
1. **File permissions**: Must be `600` (owner read/write only) for files containing secrets
2. **Owner**: Should be `root` or the service user (e.g., `lucia`)
3. **Location**: Store in `/etc/lucia/` or user config directory

### Required Variables Check
```bash
# Validation script checks these variables are present and non-empty:
required_vars=(
  "HomeAssistant__BaseUrl"
  "HomeAssistant__AccessToken"
  "OpenAI__ApiKey"
  "OpenAI__ModelId"
  "OpenAI__EmbeddingModelId"
)
```

## systemd Service File Integration

The environment file is loaded by systemd using the `EnvironmentFile` directive:

```ini
[Service]
EnvironmentFile=/etc/lucia/lucia.env
```

Variables are then available to the application process as environment variables.

## Error Messages

### Missing Required Variable
```
Error: Missing required environment variable 'HomeAssistant__BaseUrl'
Check your environment file: /etc/lucia/lucia.env
```

### Invalid URL Format
```
Error: Invalid URL format for 'HomeAssistant__BaseUrl'
Current value: 'homeassistant:8123'
Expected format: 'http://homeassistant:8123' or 'https://homeassistant:8123'
```

### Invalid Log Level
```
Error: Invalid log level 'Info' for 'Logging__LogLevel__Default'
Allowed values: Trace, Debug, Information, Warning, Error, Critical
Note: Values are case-sensitive
```

### Permission Error
```
Error: Environment file '/etc/lucia/lucia.env' has incorrect permissions: 644
File contains sensitive credentials and should be readable only by owner
Run: sudo chmod 600 /etc/lucia/lucia.env
```

## Security Best Practices

### 1. File Permissions
```bash
# Set correct ownership and permissions
sudo chown root:root /etc/lucia/lucia.env
sudo chmod 600 /etc/lucia/lucia.env
```

### 2. Validate Before Starting Service
```bash
# Check file exists and has content
test -s /etc/lucia/lucia.env || echo "Environment file is empty or missing"

# Check required variables are set
source /etc/lucia/lucia.env
: "${HomeAssistant__BaseUrl:?Missing HomeAssistant__BaseUrl}"
: "${HomeAssistant__AccessToken:?Missing HomeAssistant__AccessToken}"
: "${OpenAI__ApiKey:?Missing OpenAI__ApiKey}"
```

### 3. Backup Configuration
```bash
# Backup environment file (excluding sensitive values)
cp /etc/lucia/lucia.env /etc/lucia/lucia.env.backup
# Or use provided backup script
/opt/lucia/scripts/backup-config.sh
```

## Testing Configuration

### Manual Validation
```bash
# Load environment file
source /etc/lucia/lucia.env

# Test Home Assistant connectivity
curl -H "Authorization: Bearer $HomeAssistant__AccessToken" \
     "${HomeAssistant__BaseUrl}/api/"

# Test Redis connectivity
redis-cli -h localhost -p 6379 ping

# Test LLM endpoint (if using Ollama)
curl "${OpenAI__BaseUrl}/models"
```

### Automated Validation
```bash
# Run validation script
/opt/lucia/scripts/validate-deployment.sh systemd
```

## Migration from Other Deployment Methods

### From Docker Compose (.env)
```bash
# Docker .env uses same format, just copy and adjust paths:
cp docker/.env /etc/lucia/lucia.env

# Update Redis connection string (Docker uses service name 'redis')
sed -i 's/Redis__ConnectionString=redis:6379/Redis__ConnectionString=localhost:6379/' \
  /etc/lucia/lucia.env
```

### From Kubernetes (ConfigMap/Secret)
```bash
# Extract values from Kubernetes and format as KEY=VALUE
kubectl get secret lucia-secrets -o jsonpath='{.data.homeassistant-token}' | base64 -d \
  > temp_token

echo "HomeAssistant__BaseUrl=$(kubectl get configmap lucia-config -o jsonpath='{.data.homeassistant-url}')" \
  > /etc/lucia/lucia.env
echo "HomeAssistant__AccessToken=$(cat temp_token)" \
  >> /etc/lucia/lucia.env
rm temp_token
```
