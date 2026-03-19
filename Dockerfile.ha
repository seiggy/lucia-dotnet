# =============================================================================
# Lucia Home Assistant Add-on — Mono-Container Dockerfile
# Uses InMemory cache + SQLite storage (no Redis/MongoDB required)
# =============================================================================

# Build stage
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

# Restore
RUN dotnet restore lucia.AgentHost/lucia.AgentHost.csproj

# Copy source
COPY lucia.AgentHost/ lucia.AgentHost/
COPY lucia.Agents/ lucia.Agents/
COPY lucia.Data/ lucia.Data/
COPY lucia.HomeAssistant/ lucia.HomeAssistant/
COPY lucia.ServiceDefaults/ lucia.ServiceDefaults/
COPY lucia.Wyoming/ lucia.Wyoming/
COPY lucia.TimerAgent/ lucia.TimerAgent/
COPY lucia.MusicAgent/ lucia.MusicAgent/

# Publish
RUN dotnet publish lucia.AgentHost/lucia.AgentHost.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Create non-root user
RUN useradd -m appuser

# Create data directories
RUN mkdir -p /data /app/models /app/plugins && \
    chown -R appuser:appuser /data /app/models /app/plugins

# Copy published app
COPY --from=build /app/publish .

# Configure for lightweight mode
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
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8099/health || exit 1

USER appuser

LABEL org.opencontainers.image.title="Lucia Home Assistant Agent" \
      org.opencontainers.image.description="Privacy-first multi-agent assistant for Home Assistant" \
      org.opencontainers.image.source="https://github.com/seiggy/lucia-dotnet"

ENTRYPOINT ["dotnet", "lucia.AgentHost.dll"]
