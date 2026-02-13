# Kubernetes Deployment Guide for Lucia

This directory contains production-ready Kubernetes deployment configurations for Lucia AI Agent Host. Two deployment approaches are provided:

1. **Helm Charts** (`helm/`) - Recommended for production deployments with extensive customization
2. **Raw Kubernetes Manifests** (`manifests/`) - Alternative for simpler deployments or GitOps workflows

## Quick Start

### Option 1: Helm Chart (Recommended)

```bash
# Navigate to Helm directory
cd helm

# Install with default development configuration
helm install lucia . \
  --create-namespace \
  --namespace lucia \
  -f values.dev.yaml

# Verify installation
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=lucia -n lucia --timeout=300s
kubectl port-forward -n lucia svc/lucia 8080:80 &
curl http://localhost:8080/health
```

See `helm/README.md` for detailed Helm documentation and advanced configuration options.

### Option 2: Raw Kubernetes Manifests

```bash
# Navigate to manifests directory
cd manifests

# Create namespace and deploy all resources
kubectl apply -f kustomization.yaml

# Or apply files individually
kubectl apply -f 00-namespace.yaml
kubectl apply -f 01-configmap.yaml
kubectl apply -f 02-secret.yaml
kubectl apply -f 03-redis.yaml
kubectl apply -f 04-deployment.yaml
kubectl apply -f 05-ingress.yaml
kubectl apply -f 06-rbac.yaml
kubectl apply -f 07-hpa.yaml

# Verify installation
kubectl get all -n lucia
kubectl logs -n lucia -l app.kubernetes.io/name=lucia -f
```

## Directory Structure

```text
infra/kubernetes/
├── helm/                          # Helm chart for Lucia deployment
│   ├── Chart.yaml                 # Helm chart metadata
│   ├── values.yaml                # Default production values
│   ├── values.dev.yaml            # Development environment overrides
│   ├── templates/                 # Kubernetes resource templates
│   │   ├── _helpers.tpl           # Template functions
│   │   ├── deployment.yaml        # Lucia app deployment
│   │   ├── service.yaml           # Service for pod access
│   │   ├── ingress.yaml           # Ingress for external access
│   │   ├── configmap.yaml         # Configuration as code
│   │   ├── secret.yaml            # Secrets management
│   │   ├── redis-deployment.yaml  # Redis StatefulSet
│   │   ├── NOTES.txt              # Post-install instructions
│   └── README.md                  # Helm documentation
│
├── manifests/                     # Raw Kubernetes manifests (kustomize)
│   ├── 00-namespace.yaml          # Namespace creation
│   ├── 01-configmap.yaml          # Configuration as code
│   ├── 02-secret.yaml             # Secrets management
│   ├── 03-redis.yaml              # Redis StatefulSet + Service
│   ├── 04-deployment.yaml         # Lucia app Deployment + Service
│   ├── 05-ingress.yaml            # Ingress configuration
│   ├── 06-rbac.yaml               # ServiceAccount + RBAC + PDB
│   ├── 07-hpa.yaml                # Horizontal Pod Autoscaler
│   ├── kustomization.yaml         # Kustomize configuration
│   └── README.md                  # Manifests documentation
│
└── README.md                      # This file
```

## Deployment Methods Comparison

| Feature | Helm | Raw Manifests |
|---------|------|---------------|
| **Learning Curve** | Moderate | Low |
| **Flexibility** | Very High | High |
| **Customization** | Template-driven | Direct editing |
| **GitOps Ready** | Yes | Yes |
| **Package Management** | Yes | No |
| **Version Management** | Excellent | Basic |
| **Rollback Support** | Built-in | Manual |
| **Community Usage** | Very Common | Common |
| **Production Ready** | Yes | Yes |

**Recommendation**: Use Helm for production deployments. Use raw manifests for learning or simple deployments.

## Prerequisites

- **Kubernetes 1.24+** - Version check: `kubectl version --client`
- **kubectl** - Kubernetes CLI tool
- **Helm 3.x** (for Helm deployments) - Version check: `helm version`
- **Storage Class** - For persistent volumes (check with `kubectl get storageclass`)
- **Ingress Controller** (optional) - For external access (NGINX, Traefik, etc.)
- **cert-manager** (optional) - For automatic TLS certificate management

### Verify Prerequisites

```bash
# Check Kubernetes version
kubectl version --short

# Check storage classes available
kubectl get storageclass

# Check if ingress controller is installed
kubectl get ingressclass

# Check if cert-manager is installed (optional)
kubectl get namespace cert-manager
```

## Configuration Management

### Using ConfigMap and Secret

Both deployment methods use Kubernetes ConfigMap for non-sensitive configuration and Secret for sensitive data:

**ConfigMap** contains:

- Redis connection details
- LLM provider endpoints
- Home Assistant API endpoint
- Feature flags
- Logging configuration

**Secret** contains:

- LLM provider API keys
- Home Assistant API token
- TLS certificates (if applicable)

### Updating Configuration

#### With Helm

```bash
# Update values and upgrade
helm upgrade lucia ./helm \
  --set llm.chatModel.endpoint="https://new-endpoint.com" \
  --set llm.chatModel.apiKey="new-key"

# Restart pods to apply changes
kubectl rollout restart deployment/lucia -n lucia
```

#### With Raw Manifests

```bash
# Edit ConfigMap
kubectl edit configmap lucia -n lucia

# Edit Secret
kubectl edit secret lucia -n lucia

# Restart deployment
kubectl rollout restart deployment/lucia -n lucia
```

### Injecting Secrets Securely

Never commit API keys to git. Instead:

#### **Option 1: Use --set with environment variables**

```bash
helm install lucia ./helm \
  --set llm.chatModel.apiKey=$OPENAI_API_KEY \
  --set homeAssistant.apiToken=$HA_TOKEN
```

#### **Option 2: Use sealed-secrets (recommended for GitOps)**

```bash
# Install sealed-secrets controller
kubectl apply -f https://github.com/bitnami-labs/sealed-secrets/releases/download/v0.24.0/controller.yaml

# Seal your secret
echo -n "my-secret-value" | kubectl create secret generic mysecret --dry-run=client --from-file=- -o yaml | kubeseal -f - -w sealed-secret.yaml

# Apply sealed secret
kubectl apply -f sealed-secret.yaml
```

#### **Option 3: Use External Secrets Operator (for AWS Secrets Manager, Vault, etc.)**

```bash
# Install External Secrets Operator
helm repo add external-secrets https://charts.external-secrets.io
helm install external-secrets external-secrets/external-secrets -n external-secrets-system --create-namespace

# Create SecretStore pointing to your secret manager
# Then reference it in Lucia Secret
```

## Deployment Scenarios

### Scenario 1: Home Lab with Local Storage

**Requirements**: Single-node or small cluster, local storage

```bash
# Using Helm with dev values
helm install lucia ./helm \
  --namespace lucia \
  --create-namespace \
  -f values.dev.yaml \
  --set redis.storage.storageClassName=local-path \
  --set lucia.autoscaling.enabled=false \
  --set lucia.replicaCount=1
```

### Scenario 2: Production Home Server

**Requirements**: 3-node cluster, external LLM endpoint, persistent storage

```bash
helm install lucia ./helm \
  --namespace lucia \
  --create-namespace \
  --set global.environment=production \
  --set lucia.replicaCount=3 \
  --set llm.provider=openai \
  --set llm.chatModel.endpoint="https://api.openai.com/v1" \
  --set llm.chatModel.apiKey=$OPENAI_KEY \
  --set lucia.ingress.enabled=true \
  --set lucia.ingress.hosts[0].host="lucia.home.local"
```

### Scenario 3: Enterprise with Observability

**Requirements**: Kubernetes cluster with Prometheus/Grafana, Azure OpenAI, external Redis

```bash
helm install lucia ./helm \
  --namespace lucia \
  --create-namespace \
  -f values-enterprise.yaml \
  --set llm.provider=azureopenai \
  --set llm.chatModel.endpoint="https://myinstance.openai.azure.com/" \
  --set llm.chatModel.apiKey=$AZURE_OPENAI_KEY \
  --set redis.enabled=false \
  --set redis.externalHost="redis-cluster.redis" \
  --set observability.enabled=true
```

## Common Operations

### Health Checks

```bash
# Check pod status
kubectl get pods -n lucia

# Check pod details
kubectl describe pod lucia-0 -n lucia

# Check service
kubectl get svc -n lucia

# Check deployment status
kubectl rollout status deployment/lucia -n lucia

# Test health endpoint
kubectl exec -it deployment/lucia -n lucia -- curl http://localhost:8080/health
```

### Viewing Logs

```bash
# Latest logs
kubectl logs -n lucia -l app.kubernetes.io/name=lucia

# Follow logs
kubectl logs -n lucia -l app.kubernetes.io/name=lucia -f

# Specific pod
kubectl logs -n lucia lucia-5d4b9c7f8-abc12

# Last 100 lines
kubectl logs -n lucia -l app.kubernetes.io/name=lucia --tail=100

# Logs from Redis
kubectl logs -n lucia lucia-redis-0 -f
```

### Scaling Deployment

```bash
# Manual scaling (Helm)
kubectl patch deployment lucia -n lucia -p '{"spec":{"replicas":5}}'

# Or update Helm values
helm upgrade lucia ./helm -n lucia --set lucia.replicaCount=5

# Check HPA status
kubectl get hpa -n lucia
kubectl describe hpa lucia -n lucia
```

### Rolling Updates

```bash
# Update image tag
kubectl set image deployment/lucia lucia=seiggy/lucia-agenthost:v1.1.0 -n lucia

# Check rollout status
kubectl rollout status deployment/lucia -n lucia

# Rollback if needed
kubectl rollout undo deployment/lucia -n lucia
```

### Accessing the Application

```bash
# Port forward for local access
kubectl port-forward -n lucia svc/lucia 8080:80 &

# Access via ingress (if configured)
curl http://lucia.local/health

# Get NodePort
kubectl get svc lucia -n lucia -o jsonpath='{.spec.ports[0].nodePort}'
```

## Troubleshooting

### Pods Not Starting

```bash
# Check pod status
kubectl describe pod lucia-0 -n lucia

# Check events
kubectl get events -n lucia --sort-by='.lastTimestamp'

# Check resource availability
kubectl top nodes
kubectl top pod -n lucia
```

### Redis Connection Issues

```bash
# Check Redis status
kubectl get statefulset lucia-redis -n lucia

# Test Redis connectivity
kubectl exec -it deployment/lucia -n lucia -- redis-cli -h lucia-redis ping

# Check Redis logs
kubectl logs -n lucia lucia-redis-0

# Check DNS resolution
kubectl exec -it deployment/lucia -n lucia -- nslookup lucia-redis
```

### ConfigMap/Secret Not Applied

```bash
# Verify ConfigMap exists
kubectl get configmap lucia -n lucia
kubectl describe configmap lucia -n lucia

# View ConfigMap content
kubectl get configmap lucia -n lucia -o yaml

# Restart pods to apply changes
kubectl rollout restart deployment/lucia -n lucia

# Monitor restart
kubectl get pods -n lucia -w
```

### Ingress Not Working

```bash
# Check ingress resource
kubectl get ingress -n lucia
kubectl describe ingress lucia -n lucia

# Check ingress controller
kubectl get pods -n ingress-nginx

# Check DNS
nslookup lucia.local
```

## Monitoring and Observability

### Prometheus Monitoring

```bash
# Label pods for Prometheus scraping (already configured)
# Prometheus can discover targets via:
kubectl get pods -n lucia -o yaml | grep prometheus.io

# Metrics available at
# https://lucia.local:8080/metrics
```

### Logging with ELK Stack

```bash
# Ship logs to external ELK stack by adding sidecar
# See advanced examples in Helm values
```

### Resource Usage

```bash
# CPU and memory usage
kubectl top pod -n lucia

# Resource requests vs limits
kubectl describe pod -n lucia lucia-0
```

## Advanced Topics

### External Redis

```bash
# Disable embedded Redis
helm install lucia ./helm \
  --set redis.enabled=false \
  --set redis.externalHost="redis-cluster.redis.svc.cluster.local" \
  --set redis.service.port=6379
```

### Multiple Deployments

```bash
# Deploy to multiple environments
helm install lucia-prod ./helm \
  --namespace lucia-prod \
  -f values-production.yaml

helm install lucia-dev ./helm \
  --namespace lucia-dev \
  -f values.dev.yaml
```

### Custom Storage Classes

```bash
# Use specific storage class
helm install lucia ./helm \
  --set redis.storage.storageClassName=fast-ssd
```

### Network Policies

```bash
# Enable network policy
helm install lucia ./helm \
  --set networkPolicy.enabled=true
```

## Cleanup

### Uninstall Helm Release

```bash
# Remove release
helm uninstall lucia --namespace lucia

# Delete namespace (removes all resources)
kubectl delete namespace lucia

# Delete persistent volumes if desired
kubectl delete pvc -n lucia
```

### Remove Raw Manifests

```bash
# Delete all resources
kubectl delete -f kustomization.yaml

# Or delete selectively
kubectl delete namespace lucia
```

## Documentation Links

- [Kubernetes Documentation](https://kubernetes.io/docs/)
- [Helm Documentation](https://helm.sh/docs/)
- [Kustomize Documentation](https://kustomize.io/)
- [Lucia GitHub Repository](https://github.com/seiggy/lucia-dotnet)
- [Helm Chart README](helm/README.md)
- [Raw Manifests README](manifests/README.md)

## Support

For issues or questions:

- [GitHub Issues](https://github.com/seiggy/lucia-dotnet/issues)
- Documentation: See `/infra/docker/` for Docker deployment guide
- Helm Chart: See `helm/README.md` for detailed Helm documentation

## License

See LICENSE file in the repository root.
