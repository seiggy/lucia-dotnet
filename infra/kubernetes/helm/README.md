# Lucia Helm Chart

A production-ready Kubernetes Helm chart for deploying Lucia AI Agent Host with Redis persistence and configurable LLM providers.

## Overview

This Helm chart deploys Lucia as a scalable, resilient Kubernetes application with:

- **Multi-replica deployment** with rolling updates for zero-downtime deployments
- **Redis StatefulSet** for task persistence and session state
- **Ingress support** with TLS termination and cert-manager integration
- **Horizontal Pod Autoscaling** based on CPU and memory metrics
- **Pod Disruption Budgets** for high availability
- **Security hardening** with non-root user execution and restricted capabilities
- **Comprehensive monitoring** with Prometheus pod annotations
- **ConfigMap and Secret** management for configuration
- **Health checks** with liveness and readiness probes
- **Resource limits and requests** for efficient cluster utilization

## Prerequisites

- Kubernetes 1.24 or later
- Helm 3.x
- kubectl configured with cluster access
- (Optional) NGINX Ingress Controller for ingress support
- (Optional) cert-manager for automated TLS certificate management
- (Optional) Prometheus for monitoring

## Quick Start (5 minutes)

### 1. Add Helm Repository

```bash
# Clone the lucia-dotnet repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet/infra/kubernetes/helm
```

### 2. Create Namespace

```bash
kubectl create namespace lucia
```

### 3. Install Chart with Minimal Configuration

```bash
# Production deployment with OpenAI
helm install lucia . \
  --namespace lucia \
  --set llm.chatModel.endpoint="https://api.openai.com/v1" \
  --set llm.chatModel.apiKey="sk-..." \
  --set llm.embeddingModel.endpoint="https://api.openai.com/v1" \
  --set llm.embeddingModel.apiKey="sk-..."

# OR Development deployment with Ollama
helm install lucia . \
  --namespace lucia \
  -f values.dev.yaml \
  --set llm.chatModel.endpoint="http://ollama:11434" \
  --set llm.embeddingModel.endpoint="http://ollama:11434"
```

### 4. Verify Installation

```bash
# Wait for pods to be ready
kubectl wait --for=condition=ready pod -l app.kubernetes.io/name=lucia -n lucia --timeout=300s

# Get the application URL
kubectl port-forward -n lucia svc/lucia 8080:80 &
curl http://localhost:8080/health
```

## Configuration

### Basic Installation Scenarios

#### Scenario 1: Home Lab with Local Storage

```bash
helm install lucia . \
  --namespace lucia \
  -f values.dev.yaml \
  --set redis.storage.storageClassName=local-path \
  --set lucia.autoscaling.enabled=false
```

#### Scenario 2: Production with Azure OpenAI

```bash
helm install lucia . \
  --namespace lucia \
  --set global.environment=production \
  --set lucia.replicaCount=3 \
  --set llm.provider=azureopenai \
  --set llm.chatModel.endpoint="https://<instance>.openai.azure.com/" \
  --set llm.chatModel.apiKey="<api-key>" \
  --set llm.embeddingModel.endpoint="https://<instance>.openai.azure.com/" \
  --set llm.embeddingModel.apiKey="<api-key>" \
  --set lucia.ingress.hosts[0].host="lucia.example.com" \
  --set lucia.ingress.tls[0].secretName="lucia-tls" \
  --set lucia.ingress.tls[0].hosts[0]="lucia.example.com"
```

#### Scenario 3: Development with Ollama

```bash
helm install lucia . \
  --namespace lucia \
  -f values.dev.yaml \
  --set llm.chatModel.endpoint="http://ollama.default.svc.cluster.local:11434" \
  --set llm.embeddingModel.endpoint="http://ollama.default.svc.cluster.local:11434"
```

### Common Configuration Options

#### Setting LLM Provider

**OpenAI:**

```bash
--set llm.provider=openai \
--set llm.chatModel.endpoint="https://api.openai.com/v1" \
--set llm.chatModel.model="gpt-4o-mini" \
--set llm.chatModel.apiKey="sk-..."
```

**Azure OpenAI:**

```bash
--set llm.provider=azureopenai \
--set llm.chatModel.endpoint="https://<instance>.openai.azure.com/" \
--set llm.chatModel.apiKey="<api-key>" \
--set llm.chatModel.model="gpt-4o-mini"
```

**Ollama (Local):**

```bash
--set llm.provider=ollama \
--set llm.chatModel.endpoint="http://ollama:11434" \
--set llm.chatModel.model="llama3.2:3b"
```

#### Enabling Ingress with TLS

```bash
--set lucia.ingress.enabled=true \
--set lucia.ingress.className=nginx \
--set lucia.ingress.hosts[0].host="lucia.example.com" \
--set lucia.ingress.annotations."cert-manager\.io/cluster-issuer"=letsencrypt-prod \
--set lucia.ingress.tls[0].secretName=lucia-tls \
--set lucia.ingress.tls[0].hosts[0]=lucia.example.com
```

#### Configuring Resource Limits

```bash
--set lucia.resources.requests.cpu=500m \
--set lucia.resources.requests.memory=256Mi \
--set lucia.resources.limits.cpu=2000m \
--set lucia.resources.limits.memory=1Gi
```

#### Disabling Autoscaling

```bash
--set lucia.autoscaling.enabled=false \
--set lucia.replicaCount=2
```

#### Configuring Redis

```bash
--set redis.replicaCount=1 \
--set redis.storage.size=5Gi \
--set redis.storage.storageClassName=fast-ssd
```

## Using values-override.yaml

For complex deployments, create a custom values file:

```yaml
# values-production.yaml
global:
  environment: production

lucia:
  replicaCount: 3
  image:
    tag: "1.0.0"
  resources:
    requests:
      cpu: 500m
      memory: 512Mi
    limits:
      cpu: 2000m
      memory: 1Gi
  autoscaling:
    enabled: true
    minReplicas: 3
    maxReplicas: 10
    targetCPUUtilizationPercentage: 70
  ingress:
    enabled: true
    className: nginx
    hosts:
      - host: lucia.example.com
        paths:
          - path: /
            pathType: Prefix
    tls:
      - secretName: lucia-tls
        hosts:
          - lucia.example.com

redis:
  replicaCount: 1
  storage:
    size: 10Gi
    storageClassName: fast-ssd

llm:
  provider: azureopenai
  chatModel:
    endpoint: "https://myinstance.openai.azure.com/"
    model: "gpt-4o"
```

Then install with:

```bash
helm install lucia . \
  --namespace lucia \
  -f values.yaml \
  -f values-production.yaml \
  --set-string llm.chatModel.apiKey=$OPENAI_API_KEY \
  --set-string homeAssistant.apiToken=$HA_TOKEN
```

## Updating Configuration

### Update ConfigMap and Secrets

```bash
# Edit configuration
kubectl edit configmap lucia -n lucia
kubectl edit secret lucia -n lucia

# Trigger rolling restart
kubectl rollout restart deployment/lucia -n lucia
```

### Upgrade Helm Release

```bash
# Dry-run to preview changes
helm upgrade lucia . --namespace lucia --dry-run

# Apply upgrade
helm upgrade lucia . --namespace lucia -f values.yaml

# Check rollout status
kubectl rollout status deployment/lucia -n lucia
```

## Verification

### Health Checks

```bash
# Check deployment status
kubectl get deployment -n lucia
kubectl get pods -n lucia
kubectl describe pod <pod-name> -n lucia

# Check service connectivity
kubectl get svc -n lucia
kubectl port-forward -n lucia svc/lucia 8080:80
curl http://localhost:8080/health

# Check ingress
kubectl get ingress -n lucia
```

### Logs and Debugging

```bash
# View logs
kubectl logs -n lucia -l app.kubernetes.io/name=lucia -f

# Check specific pod
kubectl logs -n lucia <pod-name>

# Get pod events
kubectl describe pod -n lucia <pod-name>

# Shell access (for debugging)
kubectl exec -it -n lucia <pod-name> -- /bin/sh
```

### Redis Status

```bash
# Check Redis StatefulSet
kubectl get statefulset -n lucia

# Redis logs
kubectl logs -n lucia lucia-redis-0

# Connect to Redis
kubectl exec -it -n lucia lucia-redis-0 -- redis-cli
```

## Troubleshooting

### Pods Not Starting

```bash
# Check pod events
kubectl describe pod -n lucia <pod-name>

# Check logs
kubectl logs -n lucia <pod-name>

# Verify resources available
kubectl top nodes
kubectl top pod -n lucia
```

### Redis Connection Failed

```bash
# Verify Redis is running
kubectl get statefulset -n lucia lucia-redis

# Test Redis connectivity
kubectl exec -it -n lucia <lucia-pod> -- redis-cli -h lucia-redis ping

# Check Redis logs
kubectl logs -n lucia lucia-redis-0
```

### ConfigMap/Secret Not Applied

```bash
# Verify ConfigMap and Secret exist
kubectl get configmap,secret -n lucia

# View ConfigMap contents
kubectl get configmap lucia -n lucia -o yaml

# Restart pods to apply changes
kubectl rollout restart deployment/lucia -n lucia
```

### Ingress Not Working

```bash
# Verify Ingress Controller is installed
kubectl get ingressclass

# Check Ingress resource
kubectl get ingress -n lucia
kubectl describe ingress lucia -n lucia

# Verify DNS resolution
nslookup lucia.example.com
```

## Advanced Topics

### Custom Storage Class

```bash
# List available storage classes
kubectl get storageclass

# Use specific storage class
helm install lucia . \
  --set redis.storage.storageClassName=fast-ssd
```

### Pod Disruption Budgets

```bash
# Check PDB status
kubectl get pdb -n lucia

# View PDB details
kubectl describe pdb lucia -n lucia
```

### Horizontal Pod Autoscaling

```bash
# Check HPA status
kubectl get hpa -n lucia
kubectl describe hpa lucia -n lucia

# View current metrics
kubectl top pod -n lucia
```

### Node Affinity and Tolerations

```bash
# Assign pods to specific nodes
helm install lucia . \
  --set nodeSelector.kubernetes\.io/hostname=home-server

# Allow pods on tainted nodes
helm install lucia . \
  --set tolerations[0].key=edge \
  --set tolerations[0].operator=Equal \
  --set tolerations[0].value=true \
  --set tolerations[0].effect=NoSchedule
```

## Uninstall

```bash
# Remove Helm release
helm uninstall lucia --namespace lucia

# Delete persistent volumes (if desired)
kubectl delete pvc -n lucia

# Delete namespace
kubectl delete namespace lucia
```

## Support and Documentation

- **GitHub Repository**: <https://github.com/seiggy/lucia-dotnet>
- **Issues**: <https://github.com/seiggy/lucia-dotnet/issues>
- **Documentation**: See `/infra/kubernetes/README.md` for additional guides

## License

See LICENSE file in the repository root.
