# Lucia systemd Deployment Guide

Deploy Lucia as a native Linux service using systemd on Ubuntu, Debian, RHEL, Fedora, or other systemd-based distributions.

## 📋 Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Installation Methods](#installation-methods)
  - [Automated Installation](#automated-installation)
  - [Manual Installation](#manual-installation)
- [Configuration](#configuration)
- [Service Management](#service-management)
- [Logging](#logging)
- [Troubleshooting](#troubleshooting)
- [Security](#security)
- [Updating](#updating)
- [Uninstalling](#uninstalling)

---

## Overview

The systemd deployment method runs Lucia as a native Linux service with:

- **Automatic startup** on boot
- **Crash recovery** with restart policies
- **Log management** via journald
- **Security hardening** with systemd sandboxing
- **Resource limits** to prevent system exhaustion
- **Dependency management** (waits for Redis and network)

**Deployment Time:** ~25 minutes for first-time setup

---

## Prerequisites

### Required Software

| Component | Minimum Version | Installation |
|-----------|-----------------|--------------|
| **systemd** | 240+ | Included in most modern Linux distros |
| **.NET Runtime** | 10.0 RC1+ | [Install Guide](https://learn.microsoft.com/en-us/dotnet/core/install/linux) |
| **Redis** | 7.0+ | `sudo apt install redis-server` (Ubuntu/Debian) |
| **curl** | Any | `sudo apt install curl` |

### System Requirements

- **CPU:** 2+ cores recommended
- **RAM:** 2GB minimum, 4GB recommended
- **Storage:** 500MB for application + logs
- **Network:** Access to Home Assistant instance (LAN or internet)

### Home Assistant Requirements

- **Version:** 2023.1 or later
- **Long-lived access token:** Created at `http://YOUR_HA_IP:8123/profile/security`

### LLM Provider

Choose one:

- **OpenAI** (cloud): API key from [OpenAI Platform](https://platform.openai.com/)
- **Azure OpenAI** (cloud): Azure subscription with OpenAI resource
- **Ollama** (local): [Installation guide](https://ollama.ai/download)
- **LM Studio** (local): [Download](https://lmstudio.ai/)

---

## Quick Start

For experienced users, here's the 5-step quick start:

```bash
# 1. Install prerequisites
sudo apt update
sudo apt install redis-server curl

# Install .NET 10 Runtime
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet

# 2. Download and extract Lucia release
wget https://github.com/seiggy/lucia-dotnet/releases/latest/download/lucia-linux-x64.tar.gz
tar -xzf lucia-linux-x64.tar.gz
cd lucia-linux-x64/infra/systemd

# 3. Run automated installation
sudo ./install.sh

# 4. Configure environment variables
sudo nano /etc/lucia/lucia.env
# Edit: HomeAssistant__BaseUrl, HomeAssistant__AccessToken, OpenAI__ApiKey, etc.

# 5. Start the service
sudo systemctl start redis
sudo systemctl enable redis
sudo systemctl enable --now lucia
sudo systemctl status lucia
```

**Expected deployment time:** 15-25 minutes (including configuration)

---

## Installation Methods

### Automated Installation

The automated installation script handles all setup steps.

#### 1. Prepare the System

```bash
# Update package lists
sudo apt update  # Ubuntu/Debian
sudo dnf update  # RHEL/Fedora

# Install Redis
sudo apt install redis-server  # Ubuntu/Debian
sudo dnf install redis          # RHEL/Fedora

# Start Redis
sudo systemctl start redis
sudo systemctl enable redis
```

#### 2. Install .NET 10 Runtime

**Ubuntu/Debian:**

```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet

# Add to PATH (if not already)
export PATH=$PATH:/usr/share/dotnet
echo 'export PATH=$PATH:/usr/share/dotnet' >> ~/.bashrc
```

**RHEL/Fedora:**

```bash
sudo dnf install dotnet-runtime-10.0
```

Verify installation:

```bash
dotnet --version
# Should output: 10.0.x
```

#### 3. Build and Publish Lucia

If installing from source:

```bash
# Clone repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet

# Publish application
dotnet publish lucia.AgentHost/lucia.AgentHost.csproj -c Release -r linux-x64 --self-contained false

# Navigate to systemd directory
cd infra/systemd
```

#### 4. Run Installation Script

```bash
sudo ./install.sh
```

The script will:

1. ✅ Check prerequisites
2. ✅ Create `/opt/lucia` directory
3. ✅ Copy application binaries
4. ✅ Create `/etc/lucia` configuration directory
5. ✅ Copy environment template to `/etc/lucia/lucia.env`
6. ✅ Install systemd service file
7. ✅ Create log directory `/var/log/lucia`
8. ✅ Set proper file permissions

---

### Manual Installation

If you prefer manual installation or need to customize the process:

#### 1. Create Directories

```bash
sudo mkdir -p /opt/lucia
sudo mkdir -p /etc/lucia
sudo mkdir -p /var/log/lucia
```

#### 2. Copy Application Files

```bash
# From published output directory
sudo cp -r lucia.AgentHost/bin/Release/net10.0/publish/* /opt/lucia/

# Verify
ls -la /opt/lucia/
# Should see: lucia.AgentHost.dll, appsettings.json, etc.
```

#### 3. Create Configuration File

```bash
sudo cp infra/systemd/lucia.env.example /etc/lucia/lucia.env
sudo chmod 600 /etc/lucia/lucia.env
```

#### 4. Install Service File

```bash
sudo cp infra/systemd/lucia.service /etc/systemd/system/
sudo chmod 644 /etc/systemd/system/lucia.service
sudo systemctl daemon-reload
```

---

## Configuration

### Edit Environment File

Open the configuration file:

```bash
sudo nano /etc/lucia/lucia.env
```

### Required Variables

Update these **required** variables:

```bash
# Home Assistant
HomeAssistant__BaseUrl=http://192.168.1.100:8123
HomeAssistant__AccessToken=eyJ0eXAiOiJKV1QiLCJhbGciOi...

# LLM Provider (OpenAI example)
OpenAI__ApiKey=sk-proj-abc123...
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__ModelId=gpt-4o
OpenAI__EmbeddingModelId=text-embedding-3-small

# Redis (default values usually work)
Redis__ConnectionString=localhost:6379
Redis__Password=
```

### LLM Provider Examples

**OpenAI (Cloud):**

```bash
OpenAI__ApiKey=sk-proj-YOUR_KEY
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__ModelId=gpt-4o
OpenAI__EmbeddingModelId=text-embedding-3-small
```

**Ollama (Local):**

```bash
# Install Ollama: curl -fsSL https://ollama.ai/install.sh | sh
# Pull models: ollama pull llama3.2 && ollama pull mxbai-embed-large

OpenAI__ApiKey=ollama
OpenAI__BaseUrl=http://localhost:11434/v1
OpenAI__ModelId=llama3.2
OpenAI__EmbeddingModelId=mxbai-embed-large
```

**Azure OpenAI:**

```bash
OpenAI__ApiKey=YOUR_AZURE_KEY
OpenAI__BaseUrl=https://your-resource.openai.azure.com/
OpenAI__ModelId=gpt-4
OpenAI__EmbeddingModelId=text-embedding-ada-002
```

### Security Best Practices

```bash
# Set restrictive permissions on config file
sudo chmod 600 /etc/lucia/lucia.env

# Verify permissions
ls -l /etc/lucia/lucia.env
# Should show: -rw------- (600)
```

---

## Service Management

### Enable Service on Boot

```bash
sudo systemctl enable lucia
```

### Start Service

```bash
sudo systemctl start lucia
```

### Stop Service

```bash
sudo systemctl stop lucia
```

### Restart Service

```bash
sudo systemctl restart lucia
```

### Check Service Status

```bash
sudo systemctl status lucia
```

**Expected output:**

```text
● lucia.service - Lucia AI-Powered Home Assistant Agent Orchestration
     Loaded: loaded (/etc/systemd/system/lucia.service; enabled; vendor preset: enabled)
     Active: active (running) since Fri 2025-10-31 12:00:00 UTC; 5min ago
   Main PID: 12345 (dotnet)
      Tasks: 25 (limit: 9525)
     Memory: 256.0M
        CPU: 5.123s
     CGroup: /system.slice/lucia.service
             └─12345 /usr/bin/dotnet /opt/lucia/lucia.AgentHost.dll
```

### Disable Service (Stop Auto-Start)

```bash
sudo systemctl disable lucia
```

---

## Logging

Lucia uses systemd's journald for log management.

### View Real-Time Logs

```bash
sudo journalctl -u lucia -f
```

### View Recent Logs (last 50 lines)

```bash
sudo journalctl -u lucia -n 50 --no-pager
```

### View Logs Since Boot

```bash
sudo journalctl -u lucia -b
```

### View Logs with Timestamps

```bash
sudo journalctl -u lucia -o short-precise
```

### Filter by Time Range

```bash
# Last hour
sudo journalctl -u lucia --since "1 hour ago"

# Specific date
sudo journalctl -u lucia --since "2025-10-31 12:00:00"

# Between dates
sudo journalctl -u lucia --since "2025-10-31 00:00:00" --until "2025-10-31 23:59:59"
```

### Export Logs to File

```bash
sudo journalctl -u lucia > lucia-logs.txt
```

### Log Levels

Adjust log verbosity by editing `/etc/lucia/lucia.env`:

```bash
# Production (default)
Logging__LogLevel__Default=Information

# Troubleshooting
Logging__LogLevel__Default=Debug

# Minimal logging
Logging__LogLevel__Default=Warning
```

After changing log level, restart the service:

```bash
sudo systemctl restart lucia
```

---

## Troubleshooting

### Service Fails to Start

**Check status and recent errors:**

```bash
sudo systemctl status lucia
sudo journalctl -u lucia -n 50 --no-pager
```

**Common issues:**

#### 1. Redis Not Running

**Error:** `Unable to connect to Redis`

**Fix:**

```bash
sudo systemctl start redis
sudo systemctl enable redis
systemctl status redis
```

#### 2. Missing .NET Runtime

**Error:** `dotnet: command not found`

**Fix:**

```bash
# Install .NET 10 Runtime
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore
```

#### 3. Configuration File Not Found

**Error:** `Failed to load configuration from /etc/lucia/lucia.env`

**Fix:**

```bash
# Create from template
sudo cp /opt/lucia/infra/systemd/lucia.env.example /etc/lucia/lucia.env
sudo chmod 600 /etc/lucia/lucia.env
sudo nano /etc/lucia/lucia.env
```

#### 4. Permission Denied

**Error:** `Permission denied accessing /etc/lucia/lucia.env`

**Fix:**

```bash
sudo chmod 600 /etc/lucia/lucia.env
sudo chown root:root /etc/lucia/lucia.env
```

#### 5. Home Assistant Connection Failed

**Error:** `HTTP 401 Unauthorized when connecting to Home Assistant`

**Fix:**

- Verify Home Assistant URL in lucia.env
- Regenerate long-lived access token at `http://YOUR_HA_IP:8123/profile/security`
- Test connection:

```bash
curl -H "Authorization: Bearer YOUR_TOKEN" http://YOUR_HA_IP:8123/api/
```

#### 6. OpenAI API Errors

**Error:** `HTTP 401 Unauthorized from OpenAI API`

**Fix:**

- Verify API key in lucia.env
- Check account status at [OpenAI Platform](https://platform.openai.com/)
- Test API key:

```bash
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"
```

### Service Crashes Repeatedly

**Check crash logs:**

```bash
sudo journalctl -u lucia --since "10 minutes ago" | grep -i "error\|exception\|fatal"
```

**Increase restart delay:**

Edit `/etc/systemd/system/lucia.service`:

```ini
[Service]
RestartSec=30s  # Increase from 10s to 30s
StartLimitBurst=5  # Allow more restart attempts
```

Reload and restart:

```bash
sudo systemctl daemon-reload
sudo systemctl restart lucia
```

### High Memory Usage

**Check resource usage:**

```bash
systemctl status lucia
```

**Adjust memory limits** in `/etc/systemd/system/lucia.service`:

```ini
[Service]
MemoryMax=1G  # Reduce from 2G to 1G if needed
```

Reload and restart:

```bash
sudo systemctl daemon-reload
sudo systemctl restart lucia
```

---

## Security

### File Permissions

Lucia uses secure defaults:

```bash
# Service file (readable by all, writable by root)
-rw-r--r-- root root /etc/systemd/system/lucia.service

# Configuration file (readable/writable by root only)
-rw------- root root /etc/lucia/lucia.env

# Application files (readable by all, writable by root)
drwxr-xr-x root root /opt/lucia/
```

### systemd Security Features

The service file includes security hardening:

- **DynamicUser=yes**: Automatic user isolation
- **ProtectSystem=strict**: Read-only system directories
- **ProtectHome=yes**: No access to user home directories
- **NoNewPrivileges=yes**: Prevents privilege escalation
- **PrivateTmp=yes**: Isolated /tmp directory
- **PrivateDevices=yes**: No access to hardware devices

### Firewall Configuration

Lucia doesn't require incoming connections by default. If exposing the API:

```bash
# Allow port 8080 (Ubuntu/Debian with UFW)
sudo ufw allow 8080/tcp

# RHEL/Fedora with firewalld
sudo firewall-cmd --permanent --add-port=8080/tcp
sudo firewall-cmd --reload
```

### SELinux (RHEL/Fedora)

If SELinux is enforcing, you may need to adjust policies:

```bash
# Check SELinux status
getenforce

# Allow systemd to manage Lucia
sudo setsebool -P httpd_can_network_connect 1
```

---

## Updating

### Manual Update

```bash
# 1. Stop the service
sudo systemctl stop lucia

# 2. Backup configuration
sudo cp /etc/lucia/lucia.env /etc/lucia/lucia.env.backup

# 3. Download and extract new release
wget https://github.com/seiggy/lucia-dotnet/releases/latest/download/lucia-linux-x64.tar.gz
tar -xzf lucia-linux-x64.tar.gz

# 4. Replace application files
sudo rm -rf /opt/lucia/*
sudo cp -r lucia-linux-x64/* /opt/lucia/

# 5. Reload systemd (if service file changed)
sudo systemctl daemon-reload

# 6. Start the service
sudo systemctl start lucia

# 7. Verify
sudo systemctl status lucia
```

### Configuration Preservation

Configuration files in `/etc/lucia/` are **preserved** during updates.

---

## Uninstalling

### Automated Uninstall

```bash
sudo /opt/lucia/infra/systemd/install.sh --uninstall
```

### Manual Uninstall

```bash
# 1. Stop and disable service
sudo systemctl stop lucia
sudo systemctl disable lucia

# 2. Remove service file
sudo rm /etc/systemd/system/lucia.service
sudo systemctl daemon-reload

# 3. Remove application files
sudo rm -rf /opt/lucia

# 4. Remove configuration (optional)
sudo rm -rf /etc/lucia

# 5. Remove logs (optional)
sudo rm -rf /var/log/lucia
```

---

## Additional Resources

- **GitHub Repository:** [https://github.com/seiggy/lucia-dotnet](https://github.com/seiggy/lucia-dotnet)
- **Documentation:** [../../docs](https://github.com/seiggy/lucia-dotnet/tree/main/infra/docs)
- **Issues & Support:** [https://github.com/seiggy/lucia-dotnet/issues](GitHub Issues)
- **Docker Deployment:** [../docker/README.md](../docker/README.md)
- **Kubernetes Deployment:** [../kubernetes/README.md](../kubernetes/README.md)

---

## Support

If you encounter issues not covered in this guide:

1. Check [GitHub Issues](https://github.com/seiggy/lucia-dotnet/issues)
2. Review [Troubleshooting Guide](../docs/troubleshooting.md)
3. Open a new issue with:
   - Output of `sudo systemctl status lucia`
   - Output of `sudo journalctl -u lucia -n 100`
   - Your Linux distribution and version
   - Configuration file (with sensitive values redacted)

---

**Last Updated:** 2025-10-31  
**Version:** 1.0.0
