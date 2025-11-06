# Lucia AgentHost - Multi-stage Docker Build
# Purpose: Create optimized production image for lucia.AgentHost service
# 
# Stages:
#   - base: ASP.NET 10.0 runtime base image with non-root user
#   - build: Build the application with .NET SDK
#   - publish: Publish optimized release build
#   - final: Production image with health checks and security hardening

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

# Copy project files (maintaining directory structure)
COPY ["lucia.AgentHost/lucia.AgentHost.csproj", "lucia.AgentHost/"]
COPY ["lucia.Agents/lucia.Agents.csproj", "lucia.Agents/"]
COPY ["lucia.HomeAssistant/lucia.HomeAssistant.csproj", "lucia.HomeAssistant/"]
COPY ["lucia.HomeAssistant.SourceGenerator/lucia.HomeAssistant.SourceGenerator.csproj", "lucia.HomeAssistant.SourceGenerator/"]
COPY ["lucia.ServiceDefaults/lucia.ServiceDefaults.csproj", "lucia.ServiceDefaults/"]

# Restore dependencies
# This layer is cached unless .csproj files change
RUN dotnet restore "./lucia.AgentHost/lucia.AgentHost.csproj" \
    --runtime linux-x64 \
    /p:PublishReadyToRun=true

# Copy entire source tree
COPY . .

# Navigate to project directory and build
WORKDIR "/src"

RUN dotnet build "./lucia.AgentHost/lucia.AgentHost.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build \
    --runtime linux-x64 \
    --framework $BUILD_TARGET_FRAMEWORK \
    --no-restore

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
    /p:PublishReadyToRun=true

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
#
# Usage examples:
#   # Standard production build
#   docker build -t lucia:latest -f infra/docker/Dockerfile .
#
#   # Debug build for troubleshooting
#   docker build --build-arg BUILD_CONFIGURATION=Debug -t lucia:debug -f infra/docker/Dockerfile .
#
#   # Specific framework (if supporting multiple)
#   docker build --build-arg BUILD_TARGET_FRAMEWORK=net10.0 -t lucia:net10 -f infra/docker/Dockerfile .
