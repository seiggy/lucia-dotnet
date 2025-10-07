---
applyTo: 'Dockerfile*'
---
Role Definition:
- Containzerization Expert
- Cloud Architecture
- DevOps Expert

# Dockerfile Authoring Guidelines

## 1. Use Minimal, Official Base Images  
- Choose slim or distroless variants (e.g. `FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine`)  
- Avoid unmaintained or unverifiable images

## 2. Pin Exact Versions  
- Always specify a full tag (`node:18.16.0-slim`), never `latest`  

## 3. Order Instructions for Cache Efficiency  
1. Install system dependencies
2. Restore project/package dependencies
3. Copy application source
4. Build application
5. Clean up
- Changes in later steps won’t bust early-layer cache

## 4. Leverage a `.dockerignore` File
- Exclude build artifacts, logs, `node_modules/`, `.git/`, etc.  
- Speeds up context upload and reduces image size

## 5. Minimize Number of Layers
- Combine related `RUN` commands with `&&`
- Clean caches (`rm -rf /var/lib/apt/lists/*`) in the same `RUN`

## 6. Prefer `COPY` Over `ADD`
- Use `COPY` for local files/directories
- Reserve `ADD` for remote URLs or tar extraction

## 7. Set `WORKDIR` Early
```dockerfile
WORKDIR /app
```

* All subsequent paths are relative to this directory

## 8. Use Multi-Stage Builds

```dockerfile
# build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
...

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
COPY --from=build /app/publish /app
```

* Keeps final image lean by excluding build-time tools

## 9. Declare `ARG` and `ENV` Strategically

* `ARG` for build-time variables
* `ENV` for runtime configuration (minimize number of ENV vars)

## 10. Run as Non-Root User

```dockerfile
RUN useradd -m appuser
USER appuser
```

* Improves container security posture

## 11. Include a `HEALTHCHECK`

```dockerfile
HEALTHCHECK --interval=30s CMD curl -f http://localhost/health || exit 1
```

* Allows orchestrators to detect unhealthy containers

## 12. Specify `ENTRYPOINT` and `CMD`

* `ENTRYPOINT` for the main executable
* `CMD` for default arguments

```dockerfile
ENTRYPOINT ["dotnet", "MyApp.dll"]
CMD ["--urls", "http://0.0.0.0:80"]
```

## 13. Label Your Image

```dockerfile
LABEL org.opencontainers.image.version="1.2.3"  
LABEL org.opencontainers.image.source="https://github.com/you/repo"
```

* Facilitates maintenance and tracking

## 14. Expose Only Necessary Ports

* Declare only the ports your application listens on
* Reduces attack surface