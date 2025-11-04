# Quickstart: Deploying Lucia

**Feature**: Infrastructure Deployment Utilities and Documentation  
**Date**: 2025-10-24  
**Audience**: End users looking to deploy Lucia

This guide provides quick instructions to get Lucia up and running using your preferred deployment method.

---

## Prerequisites

Before deploying Lucia, ensure you have:

✅ **Home Assistant Instance** - Running and accessible (version 2024.1+)  
✅ **Home Assistant Access Token** - Long-lived access token from your profile  
✅ **LLM Provider** - Either:
   - OpenAI API key, OR
   - Azure OpenAI endpoint + key, OR
   - Local LLM (Ollama, LM Studio) running

**Hardware Requirements:**
- **CPU**: 2 cores minimum (4 cores recommended)
- **RAM**: 2GB minimum (4GB recommended)
- **Disk**: 5GB free space minimum
- **Network**: Access to Home Assistant instance

---

## Choose Your Deployment Method

### 🐳 Option 1: Docker Compose (Recommended for Most Users)

**Best for**: Home servers, NAS devices, quick testing  
**Time to deploy**: ~15 minutes  
**Complexity**: ⭐⭐☆☆☆ (Easy)

[Jump to Docker Instructions](#docker-compose-deployment)

---

### ☸️ Option 2: Kubernetes

**Best for**: Existing K8s clusters, production deployments, high availability  
**Time to deploy**: ~20 minutes  
**Complexity**: ⭐⭐⭐⭐☆ (Advanced)

[Jump to Kubernetes Instructions](#kubernetes-deployment)

---

### 🐧 Option 3: Linux systemd Service

**Best for**: Linux servers without containers, traditional deployments  
**Time to deploy**: ~25 minutes  
**Complexity**: ⭐⭐⭐☆☆ (Intermediate)

[Jump to systemd Instructions](#linux-systemd-deployment)

---

## Docker Compose Deployment

### Step 1: Clone Repository

```bash
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet/infra/docker
```

### Step 2: Create Configuration File

```bash
# Copy example environment file
cp .env.example .env

# Edit with your favorite editor
nano .env
```

**Required Configuration:**
```ini
# Home Assistant
HomeAssistant__BaseUrl=http://YOUR_HA_IP:8123
HomeAssistant__AccessToken=YOUR_LONG_LIVED_TOKEN

# Chat Model (unified connection string format)
# Format: Endpoint=<url>;AccessKey=<key>;Model=<model>;Provider=<provider>
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY_HERE;Model=gpt-4o;Provider=openai

# Redis (defaults are fine for most users)
Redis__ConnectionString=redis:6379
```

**For Local LLM (Ollama):**
```ini
# Chat Model with Ollama
ConnectionStrings__chat-model=Endpoint=http://host.docker.internal:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama

# Redis
Redis__ConnectionString=redis:6379
```

**For Azure OpenAI** (includes embedding support):
```ini
# Chat Model with Azure OpenAI - EMBEDDINGS SUPPORTED
ConnectionStrings__chat-model=Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=YOUR_DEPLOYMENT_NAME;Provider=azureopenai

# Redis
Redis__ConnectionString=redis:6379
```

### Step 3: Start Services

```bash
# Pull or build images and start services
docker compose up -d

# Watch logs to verify startup
docker compose logs -f lucia
```

**Expected Output:**
```
lucia-1  | info: Microsoft.Hosting.Lifetime[0]
lucia-1  |       Now listening on: http://[::]:8080
lucia-1  | info: Microsoft.Hosting.Lifetime[0]
lucia-1  |       Application started. Press Ctrl+C to shut down.
```

### Step 4: Verify Health

```bash
# Check container status
docker compose ps

# Test health endpoint
curl http://localhost:7235/health

# Expected response: {"status":"Healthy"}
```

### Step 5: Configure Home Assistant Integration

1. In Home Assistant: **Settings → Devices & Services → Add Integration**
2. Search for "**Lucia**"
3. Enter configuration:
   - **Agent Repository URL**: `http://YOUR_SERVER_IP:7235`
   - **API Key**: (leave empty if not configured)
4. Click **Submit**
5. Select your preferred agent (e.g., `light-agent`, `music-agent`)

✅ **Done!** Lucia is now running and integrated with Home Assistant.

**Common Commands:**
```bash
# Stop services
docker compose down

# View logs
docker compose logs -f

# Restart services
docker compose restart

# Update to latest version
docker compose pull
docker compose up -d
```

---

## Kubernetes Deployment

### Step 1: Prepare Kubernetes Cluster

```bash
# Verify kubectl is configured
kubectl cluster-info

# Create namespace
kubectl create namespace lucia
```

### Step 2: Choose Deployment Method

#### Option A: Helm Chart (Recommended)

```bash
cd lucia-dotnet/infra/kubernetes/helm

# Create custom values file
cat > my-values.yaml <<EOF
config:
  homeAssistant:
    baseUrl: "http://YOUR_HA_IP:8123"
  openai:
    modelId: "gpt-4o"
    embeddingModelId: "text-embedding-3-small"

secrets:
  homeAssistant:
    accessToken: "YOUR_LONG_LIVED_TOKEN"
  openai:
    apiKey: "YOUR_OPENAI_KEY"

ingress:
  enabled: true
  className: "nginx"
  hosts:
    - host: lucia.your-domain.com
      paths:
        - path: /
          pathType: Prefix
EOF

# Install Helm chart
helm install lucia . -f my-values.yaml -n lucia
```

#### Option B: Raw Manifests

```bash
cd lucia-dotnet/infra/kubernetes/manifests

# Edit configuration
kubectl create configmap lucia-config \
  --from-literal=homeassistant-url="http://YOUR_HA_IP:8123" \
  --from-literal=openai-model="gpt-4o" \
  -n lucia

# Create secrets
kubectl create secret generic lucia-secrets \
  --from-literal=homeassistant-token="YOUR_TOKEN" \
  --from-literal=openai-key="YOUR_KEY" \
  -n lucia

# Apply manifests
kubectl apply -f namespace.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml
kubectl apply -f ingress.yaml
```

### Step 3: Verify Deployment

```bash
# Check pod status
kubectl get pods -n lucia

# Expected output:
# NAME                     READY   STATUS    RESTARTS   AGE
# lucia-xxxxxxxxxx-xxxxx   1/1     Running   0          2m
# redis-0                  1/1     Running   0          2m

# View logs
kubectl logs -f deployment/lucia -n lucia

# Test health endpoint
kubectl port-forward svc/lucia 7235:80 -n lucia
curl http://localhost:7235/health
```

### Step 4: Access via Ingress

```bash
# Get ingress address
kubectl get ingress -n lucia

# Test via ingress hostname
curl http://lucia.your-domain.com/health
```

✅ **Done!** Configure Home Assistant integration using your ingress URL.

**Common Commands:**
```bash
# Update deployment
helm upgrade lucia . -f my-values.yaml -n lucia

# View logs
kubectl logs -f deployment/lucia -n lucia

# Scale replicas
kubectl scale deployment lucia --replicas=2 -n lucia

# Delete deployment
helm uninstall lucia -n lucia
```

---

## Linux systemd Deployment

### Step 1: Install .NET Runtime

```bash
# Ubuntu/Debian
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# Verify installation
dotnet --version  # Should show 10.0.x
```

### Step 2: Install Redis

```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install redis-server

# Start and enable Redis
sudo systemctl start redis-server
sudo systemctl enable redis-server

# Verify Redis is running
redis-cli ping  # Should return "PONG"
```

### Step 3: Download and Install Lucia

```bash
# Create application directory
sudo mkdir -p /opt/lucia

# Download latest release
cd /opt/lucia
sudo wget https://github.com/seiggy/lucia-dotnet/releases/latest/download/lucia-linux-x64.tar.gz
sudo tar -xzf lucia-linux-x64.tar.gz
sudo rm lucia-linux-x64.tar.gz

# Set permissions
sudo chmod +x /opt/lucia/lucia.AgentHost
```

### Step 4: Create Configuration

```bash
# Create config directory
sudo mkdir -p /etc/lucia

# Create environment file
sudo nano /etc/lucia/lucia.env
```

**Environment File Content:**
```ini
# /etc/lucia/lucia.env

HomeAssistant__BaseUrl=http://YOUR_HA_IP:8123
HomeAssistant__AccessToken=YOUR_LONG_LIVED_TOKEN

OpenAI__ApiKey=YOUR_OPENAI_KEY
OpenAI__ModelId=gpt-4o
OpenAI__EmbeddingModelId=text-embedding-3-small

Redis__ConnectionString=localhost:6379

Logging__LogLevel__Default=Information
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
```

**Set secure permissions:**
```bash
sudo chmod 600 /etc/lucia/lucia.env
sudo chown root:root /etc/lucia/lucia.env
```

### Step 5: Create systemd Service

```bash
# Copy service file
sudo cp /opt/lucia/systemd/lucia.service /etc/systemd/system/

# Reload systemd
sudo systemctl daemon-reload

# Enable service to start on boot
sudo systemctl enable lucia

# Start service
sudo systemctl start lucia
```

### Step 6: Verify Service

```bash
# Check service status
sudo systemctl status lucia

# View logs
sudo journalctl -u lucia -f

# Test health endpoint
curl http://localhost:8080/health
```

✅ **Done!** Configure Home Assistant integration using `http://YOUR_SERVER_IP:8080`.

**Common Commands:**
```bash
# View service status
sudo systemctl status lucia

# Stop service
sudo systemctl stop lucia

# Restart service
sudo systemctl restart lucia

# View logs
sudo journalctl -u lucia -f

# Disable service
sudo systemctl disable lucia
```

---

## Next Steps

After deployment, complete these steps:

### 1. Configure Home Assistant Integration

Follow the integration setup in Home Assistant:
- Settings → Devices & Services → Add Integration → Lucia
- Enter your Lucia URL
- Select your preferred agent

### 2. Test Basic Functionality

Try these commands in Home Assistant:
- "Turn on the living room lights"
- "What's playing on my speakers?"
- "Set the temperature to 72 degrees"

### 3. Configure Additional Agents

Explore and enable additional agents in the Lucia configuration:
- Light Agent (already enabled)
- Music Agent (Music Assistant integration)
- Climate Agent (coming soon)
- Security Agent (planned)

### 4. Review Logs & Monitoring

Monitor Lucia's operation:
- Check logs for errors or warnings
- Review Home Assistant integration logs
- Monitor resource usage (CPU, RAM)

### 5. Backup Configuration

Create backups of your configuration:
```bash
# Docker
cp infra/docker/.env infra/docker/.env.backup

# Kubernetes
helm get values lucia -n lucia > my-values.backup.yaml

# systemd
sudo cp /etc/lucia/lucia.env /etc/lucia/lucia.env.backup
```

---

## Troubleshooting

### Lucia Won't Start

**Check logs for errors:**
```bash
# Docker
docker compose logs lucia

# Kubernetes
kubectl logs deployment/lucia -n lucia

# systemd
sudo journalctl -u lucia -n 50
```

**Common issues:**
- Missing environment variables (check configuration file)
- Home Assistant unreachable (verify URL and network)
- Redis connection failed (ensure Redis is running)

### Home Assistant Integration Not Working

1. Verify Lucia is reachable from Home Assistant:
   ```bash
   curl http://LUCIA_URL/health
   ```

2. Check Home Assistant logs:
   ```bash
   # In Home Assistant
   Settings → System → Logs → Filter: "lucia"
   ```

3. Verify agent catalog is accessible:
   ```bash
   curl http://LUCIA_URL/agents
   ```

### LLM Requests Failing

1. Test LLM endpoint directly:
   ```bash
   # OpenAI
   curl https://api.openai.com/v1/models \
     -H "Authorization: Bearer YOUR_KEY"
   
   # Ollama
   curl http://localhost:11434/api/tags
   ```

2. Check API key configuration
3. Verify model ID matches available models

---

## Getting Help

Need assistance? Check these resources:

- **Documentation**: [github.com/seiggy/lucia-dotnet/wiki](https://github.com/seiggy/lucia-dotnet/wiki)
- **Issues**: [github.com/seiggy/lucia-dotnet/issues](https://github.com/seiggy/lucia-dotnet/issues)
- **Discussions**: [github.com/seiggy/lucia-dotnet/discussions](https://github.com/seiggy/lucia-dotnet/discussions)
- **Home Assistant Community**: [community.home-assistant.io](https://community.home-assistant.io)

When reporting issues, include:
- Deployment method (Docker/Kubernetes/systemd)
- Lucia version
- Home Assistant version
- Relevant logs (sanitized of sensitive data)

---

**Deployment Complete!** 🎉

You're now running Lucia as your privacy-first AI assistant for Home Assistant.
