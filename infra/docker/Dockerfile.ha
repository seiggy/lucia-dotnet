# =============================================================================
# Lucia Home Assistant Add-on — Mono-Container Dockerfile (CPU-only)
# Uses InMemory cache + SQLite storage (no Redis/MongoDB required)
# No CUDA/GPU dependencies — clean CPU-only ONNX Runtime
# =============================================================================

# Dashboard build stage
FROM node:22-slim AS dashboard
WORKDIR /dashboard
COPY lucia-dashboard/package.json lucia-dashboard/package-lock.json* ./
RUN npm ci --ignore-scripts
COPY lucia-dashboard/ .
RUN npm run build

# .NET build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY Directory.Build.props Directory.Packages.props global.json lucia-dotnet.slnx ./
COPY lucia.AgentHost/lucia.AgentHost.csproj lucia.AgentHost/
COPY lucia.Agents/lucia.Agents.csproj lucia.Agents/
COPY lucia.Data/lucia.Data.csproj lucia.Data/
COPY lucia.HomeAssistant/lucia.HomeAssistant.csproj lucia.HomeAssistant/
COPY lucia.ServiceDefaults/lucia.ServiceDefaults.csproj lucia.ServiceDefaults/
COPY lucia.Wyoming/lucia.Wyoming.csproj lucia.Wyoming/
COPY lucia.TimerAgent/lucia.TimerAgent.csproj lucia.TimerAgent/
COPY lucia.MusicAgent/lucia.MusicAgent.csproj lucia.MusicAgent/

# Restore for linux-x64 only
RUN dotnet restore lucia.AgentHost/lucia.AgentHost.csproj --runtime linux-x64

# Copy source
COPY lucia.AgentHost/ lucia.AgentHost/
COPY lucia.Agents/ lucia.Agents/
COPY lucia.Data/ lucia.Data/
COPY lucia.HomeAssistant/ lucia.HomeAssistant/
COPY lucia.ServiceDefaults/ lucia.ServiceDefaults/
COPY lucia.Wyoming/ lucia.Wyoming/
COPY lucia.TimerAgent/ lucia.TimerAgent/
COPY lucia.MusicAgent/ lucia.MusicAgent/

# Publish for linux-x64 (self-contained false — use shared runtime)
RUN dotnet publish lucia.AgentHost/lucia.AgentHost.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false

# Remove CUDA/TensorRT provider libs — the main libonnxruntime.so is compiled
# with CUDA support baked in but these provider shims trigger native crashes
# when CUDA drivers are absent. Removing them is defense-in-depth alongside
# the OnnxProviderDetector verification that falls back to CPU.
RUN rm -f /app/publish/runtimes/*/native/*cuda* \
          /app/publish/runtimes/*/native/*tensorrt* \
          /app/publish/runtimes/*/native/*nvinfer* \
    2>/dev/null; true

# Remove non-linux runtimes to slim the image
RUN rm -rf /app/publish/runtimes/win-* \
           /app/publish/runtimes/osx-* \
           /app/publish/runtimes/linux-arm64 \
    2>/dev/null; true

# Runtime stage — slim aspnet image, no GPU libs
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install curl for health check
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m appuser

# Copy published app (linux-x64 only, no CUDA)
COPY --from=build /app/publish .

# Copy dashboard assets to wwwroot for SPA serving
COPY --from=dashboard /dashboard/dist ./wwwroot/

# Create data directories with open permissions for volume mounts
RUN mkdir -p /data /app/models /app/plugins && \
    chown -R appuser:appuser /data /app /app/models /app/plugins && \
    chmod 777 /data /app/models /app/plugins

# Configure for lightweight mode (CPU-only, InMemory cache, SQLite store)
ENV DataProvider__Cache=InMemory \
    DataProvider__Store=SQLite \
    DataProvider__SqlitePath=/data/lucia.db \
    Deployment__Mode=standalone \
    ASPNETCORE_URLS=http://+:8099 \
    DOTNET_ENVIRONMENT=Production

# Expose HTTP API and Wyoming voice protocol ports
EXPOSE 8099
EXPOSE 10400/tcp

# Volume for persistent data (SQLite DB, voice models, plugins)
VOLUME ["/data", "/app/models", "/app/plugins"]

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -sf http://localhost:8099/health || exit 1

USER appuser

LABEL org.opencontainers.image.title="Lucia Home Assistant Agent" \
      org.opencontainers.image.description="Privacy-first multi-agent assistant for Home Assistant (CPU-only)" \
      org.opencontainers.image.source="https://github.com/seiggy/lucia-dotnet"

ENTRYPOINT ["dotnet", "lucia.AgentHost.dll"]
