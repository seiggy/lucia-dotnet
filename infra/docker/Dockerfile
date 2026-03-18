# Lucia AgentHost - Multi-stage Docker Build
# Purpose: Create optimized production image for lucia.AgentHost service
# 
# Stages:
#   - assets: Pre-built OCI image with binary downloads (Copilot CLI, etc.)
#   - node-build: Build the React SPA dashboard with Node.js
#   - base: ASP.NET 10.0 runtime base image with non-root user
#   - build: Build the application with .NET SDK
#   - publish: Publish optimized release build
#   - final: Production image with health checks and security hardening
#
# Build args:
#   ASSET_IMAGE - Pre-built asset image ref (default: ghcr.io/seiggy/lucia-dotnet/build-assets:latest)
#   BUILD_CONFIGURATION - Debug or Release (default: Release)
#   BUILD_TARGET_FRAMEWORK - Target framework version (default: net10.0)

# ============================================================================
# Stage 0a: Pre-built binary assets (Copilot CLI, models, etc.)
# See Dockerfile.assets and asset.lock for what's included.
# ============================================================================
ARG ASSET_IMAGE=ghcr.io/seiggy/lucia-dotnet/build-assets:latest
ARG TARGETARCH
ARG TARGETOS
FROM ${ASSET_IMAGE} AS assets

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
COPY ["lucia.Wyoming/lucia.Wyoming.csproj", "lucia.Wyoming/"]

# Restore dependencies (skip Copilot CLI download — handled in copilot-cli stage)
# This layer is cached unless .csproj files change

RUN RID=$(case "$TARGETARCH" in\
      amd64) echo "linux-x64" ;; \
      arm64) echo "linux-arm64" ;; \
    esac) && \
    dotnet restore "./lucia.AgentHost/lucia.AgentHost.csproj" \
      --runtime $RID \
      /p:PublishReadyToRun=true \
      /p:CopilotSkipCliDownload=true

# Copy entire source tree
COPY . .

# Navigate to project directory and build
WORKDIR "/src"

RUN RID=$(case "$TARGETARCH" in\
      amd64) echo "linux-x64" ;; \
      arm64) echo "linux-arm64" ;; \
    esac) && \
    dotnet build "./lucia.AgentHost/lucia.AgentHost.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build \
    --runtime $RID \
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
RUN RID=$(case "$TARGETARCH" in\
      amd64) echo "linux-x64" ;; \
      arm64) echo "linux-arm64" ;; \
    esac) && \
    dotnet publish "./lucia.AgentHost/lucia.AgentHost.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    --framework $BUILD_TARGET_FRAMEWORK \
    --runtime $RID \
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

# Copy Copilot CLI binary from pre-built asset image
COPY --from=assets --chown=appuser:appuser /assets/copilot/ ./

# Copy built SPA dashboard into wwwroot for static file serving
COPY --from=node-build --chown=appuser:appuser /app/dist ./wwwroot/

# Copy official plugins (MetaMCP, SearXNG) so env-based tools are registered at startup
COPY --from=build --chown=appuser:appuser /src/plugins ./plugins

# PluginDirectory default is {AppContext.BaseDirectory}/plugins = /app/plugins
ENV PluginDirectory=/app/plugins

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
# ASSET_IMAGE - Pre-built asset image with binary downloads (default: ghcr.io/seiggy/lucia-dotnet/build-assets:latest)
# BUILD_CONFIGURATION - "Debug" or "Release" (default: Release)
# BUILD_TARGET_FRAMEWORK - Target framework version (default: net10.0)
#
# Usage examples:
#   # Standard production build (uses pre-built assets from ghcr.io)
#   docker build -t lucia:latest -f infra/docker/Dockerfile .
#
#   # Build with custom asset image
#   docker build --build-arg ASSET_IMAGE=ghcr.io/seiggy/lucia-dotnet/build-assets:sha-abc123 -t lucia:latest -f infra/docker/Dockerfile .
#
#   # Debug build for troubleshooting
#   docker build --build-arg BUILD_CONFIGURATION=Debug -t lucia:debug -f infra/docker/Dockerfile .
