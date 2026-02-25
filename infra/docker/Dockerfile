# Lucia AgentHost - Multi-stage Docker Build
# Purpose: Create optimized production image for lucia.AgentHost service
# 
# Stages:
#   - copilot-cli: Download Copilot CLI binary (optional, resilient)
#   - node-build: Build the React SPA dashboard with Node.js
#   - base: ASP.NET 10.0 runtime base image with non-root user
#   - build: Build the application with .NET SDK
#   - publish: Publish optimized release build
#   - final: Production image with health checks and security hardening
#
# Build args:
#   INCLUDE_COPILOT_CLI - Include Copilot CLI binary (default: true)
#   COPILOT_CLI_VERSION - Copilot CLI version to download (default: 0.0.411)
#   BUILD_CONFIGURATION - Debug or Release (default: Release)
#   BUILD_TARGET_FRAMEWORK - Target framework version (default: net10.0)

# ============================================================================
# Stage 0a: Download Copilot CLI binary (resilient, downloaded once)
# ============================================================================
FROM alpine:3.21 AS copilot-cli

ARG INCLUDE_COPILOT_CLI=true
ARG COPILOT_CLI_VERSION=0.0.411
ARG TARGETARCH

RUN if [ "$INCLUDE_COPILOT_CLI" = "true" ]; then \
      apk add --no-cache curl && \
      PLATFORM=$(case "$TARGETARCH" in amd64) echo "linux-x64";; arm64) echo "linux-arm64";; *) echo "linux-x64";; esac) && \
      RID=$(case "$TARGETARCH" in amd64) echo "linux-x64";; arm64) echo "linux-arm64";; *) echo "linux-x64";; esac) && \
      mkdir -p /copilot-out/runtimes/${RID}/native && \
      echo "Downloading Copilot CLI ${COPILOT_CLI_VERSION} for ${PLATFORM}..." && \
      curl -fSL --retry 5 --retry-delay 10 --retry-max-time 300 \
        "https://registry.npmjs.org/@github/copilot-${PLATFORM}/-/copilot-${PLATFORM}-${COPILOT_CLI_VERSION}.tgz" \
        -o /tmp/copilot.tgz && \
      tar -xzf /tmp/copilot.tgz --strip-components=1 -C /copilot-out/runtimes/${RID}/native && \
      chmod +x /copilot-out/runtimes/${RID}/native/copilot && \
      rm /tmp/copilot.tgz; \
    else \
      mkdir -p /copilot-out; \
    fi

# ============================================================================
# Stage 0b: Node.js Build Stage (React SPA Dashboard)
# ============================================================================
FROM node:22-alpine AS node-build

WORKDIR /app

# Copy package files first for dependency caching
COPY lucia-dashboard/package*.json ./

# Install dependencies (clean install for reproducible builds)
RUN npm ci

# Copy the rest of the dashboard source and build
COPY lucia-dashboard/ ./
RUN npm run build

# ============================================================================
# Stage 1: Base Runtime Image
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Set working directory
WORKDIR /app

# Create non-root user for security
# UID 1100 for non-privileged access
RUN useradd -m -u 1100 appuser

# Expose ports for ASP.NET Core
# 8080: HTTP (user-facing)
# 8081: HTTPS (user-facing, with certificate)
EXPOSE 8080 8081

# ============================================================================
# Stage 2: Build Stage
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set build configuration (can be overridden via --build-arg)
ARG BUILD_CONFIGURATION=Release
ARG BUILD_TARGET_FRAMEWORK=net10.0

# Set working directory
WORKDIR /src

# Copy MSBuild infrastructure files required before restore
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]

# Copy project files(maintaining directory structure)
COPY ["lucia.AgentHost/lucia.AgentHost.csproj", "lucia.AgentHost/"]
COPY ["lucia.Agents/lucia.Agents.csproj", "lucia.Agents/"]
COPY ["lucia.HomeAssistant/lucia.HomeAssistant.csproj", "lucia.HomeAssistant/"]
COPY ["lucia.MusicAgent/lucia.MusicAgent.csproj", "lucia.MusicAgent/"]
COPY ["lucia.ServiceDefaults/lucia.ServiceDefaults.csproj", "lucia.ServiceDefaults/"]
COPY ["lucia.TimerAgent/lucia.TimerAgent.csproj", "lucia.TimerAgent/"]

# Restore dependencies (skip Copilot CLI download — handled in copilot-cli stage)
# This layer is cached unless .csproj files change
RUN dotnet restore "./lucia.AgentHost/lucia.AgentHost.csproj" \
    --runtime linux-x64 \
    /p:PublishReadyToRun=true \
    /p:CopilotSkipCliDownload=true

# Copy entire source tree
COPY . .

# Navigate to project directory and build
WORKDIR "/src"

RUN dotnet build "./lucia.AgentHost/lucia.AgentHost.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build \
    --runtime linux-x64 \
    --framework $BUILD_TARGET_FRAMEWORK \
    --no-restore \
    /p:CopilotSkipCliDownload=true

# ============================================================================
# Stage 3: Publish Stage
# ============================================================================
FROM build AS publish

# Build configuration (matches build stage)
ARG BUILD_CONFIGURATION=Release
ARG BUILD_TARGET_FRAMEWORK=net10.0

# Publish to /app/publish directory
# We tried running this with no-build, but it seems to cause issues with missing files. Will fix later.
RUN dotnet publish "./lucia.AgentHost/lucia.AgentHost.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    --framework $BUILD_TARGET_FRAMEWORK \
    --runtime linux-x64 \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false \
    /p:PublishReadyToRun=true \
    /p:CopilotSkipCliDownload=true

# ============================================================================
# Stage 4: Final Production Image
# ============================================================================
FROM base AS final

# Set metadata labels
LABEL maintainer="Lucia Project"
LABEL description="Lucia Agent Host - Multi-agent orchestration service"
LABEL version="1.0.0"

# Set environment variables for .NET runtime
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8080;https://+:8081 \
    COMPlus_EnableDiagnostics=0

# Copy published application from publish stage
COPY --from=publish --chown=appuser:appuser /app/publish .

# Copy Copilot CLI binary (if INCLUDE_COPILOT_CLI=true)
COPY --from=copilot-cli --chown=appuser:appuser /copilot-out/ ./

# Copy built SPA dashboard into wwwroot for static file serving
COPY --from=node-build --chown=appuser:appuser /app/dist ./wwwroot/

# Switch to non-root user for runtime security
USER appuser

# Health check for container orchestration
# Tests HTTP endpoint to verify service is responding
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Application entry point
# Runs the .NET application
ENTRYPOINT ["dotnet", "lucia.AgentHost.dll"]

# Build arguments documentation:
# BUILD_CONFIGURATION - "Debug" or "Release" (default: Release)
# BUILD_TARGET_FRAMEWORK - Target framework version (default: net10.0)
# INCLUDE_COPILOT_CLI - Include Copilot CLI binary (default: true, set false for smaller image)
# COPILOT_CLI_VERSION - Copilot CLI version (default: 0.0.411)
#
# Usage examples:
#   # Standard production build
#   docker build -t lucia:latest -f infra/docker/Dockerfile .
#
#   # Build without Copilot CLI (smaller image)
#   docker build --build-arg INCLUDE_COPILOT_CLI=false -t lucia:latest -f infra/docker/Dockerfile .
#
#   # Debug build for troubleshooting
#   docker build --build-arg BUILD_CONFIGURATION=Debug -t lucia:debug -f infra/docker/Dockerfile .
