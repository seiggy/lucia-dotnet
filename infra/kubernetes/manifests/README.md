# Lucia Kubernetes Raw Manifests

Alternative Kubernetes deployment using raw YAML manifests and Kustomize. This approach provides maximum transparency and is ideal for GitOps workflows.

## Quick Start (3 minutes)

```bash
# Apply all manifests
kubectl apply -k .

# Verify deployment
kubectl get all -n lucia
kubectl get pods -n lucia -w

# Check pod logs
kubectl logs -n lucia -l app.kubernetes.io/name=lucia -f

# Access application
kubectl port-forward -n lucia svc/lucia 8080:80 &
curl http://localhost:8080/health
```

## File Structure

| File | Purpose | Description |
|------|---------|-------------|
| `00-namespace.yaml` | Namespace | Creates the `lucia` namespace for all resources |
| `01-configmap.yaml` | Configuration | Non-sensitive configuration (endpoints, settings) |
| `02-secret.yaml` | Secrets | Sensitive data (API keys, tokens) |
| `03-redis.yaml` | Database | Redis StatefulSet and Service for persistence |
| `04-deployment.yaml` | Application | Lucia app Deployment and Service |
| `05-ingress.yaml` | Routing | Ingress configuration for external access |
| `06-rbac.yaml` | Access Control | ServiceAccount, Role, RoleBinding, PDB |
| `07-hpa.yaml` | Auto-scaling | Horizontal Pod Autoscaler configuration |
| `kustomization.yaml` | Orchestration | Kustomize configuration file |

## Applying Manifests

### Option 1: Apply All at Once (Recommended)

```bash
# Using kustomize (includes all resources and common labels)
kubectl apply -k .

# Using kubectl apply (applies files in order)
kubectl apply -f 00-namespace.yaml
kubectl apply -f 01-configmap.yaml
kubectl apply -f 02-secret.yaml
kubectl apply -f 03-redis.yaml
kubectl apply -f 04-deployment.yaml
kubectl apply -f 05-ingress.yaml
kubectl apply -f 06-rbac.yaml
kubectl apply -f 07-hpa.yaml
```

### Option 2: Apply with Custom Values

```bash
# Override replica count
kubectl apply -k . && kubectl patch deployment lucia -n lucia -p '{"spec":{"replicas":3}}'

# Override image tag
kubectl set image deployment/lucia lucia=seiggy/lucia-agenthost:v1.1.0 -n lucia
```

## Configuration

### ConfigMap (01-configmap.yaml)

Non-sensitive configuration data:

```yaml
# Edit the ConfigMap
kubectl edit configmap lucia -n lucia

# Key configuration sections:
# - Redis connection (host, port, database)
# - LLM provider settings (endpoints, model names)
# - Home Assistant integration
# - Feature flags
# - Logging levels
```

**Update steps:**
1. Edit the ConfigMap
2. Restart deployment: `kubectl rollout restart deployment/lucia -n lucia`

### Secret (02-secret.yaml)

Sensitive data (base64 encoded):

```yaml
# Generate base64 values:
echo -n "value" | base64

# Edit the Secret:
kubectl edit secret lucia -n lucia

# Contains:
# - LLM provider API keys
# - Home Assistant API token
```

**Security Note**: Consider using:
- **Sealed Secrets** for GitOps-safe secret management
- **External Secrets Operator** for integration with external secret stores
- **RBAC** to limit access to secrets

## Deployment Details

### Lucia Application (04-deployment.yaml)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lucia
  namespace: lucia
spec:
  replicas: 2  # Change this to scale
  template:
    spec:
      containers:
      - name: lucia
        image: docker.io/seiggy/lucia-agenthost:latest  # Update tag to deploy new version
        resources:
          requests:
            cpu: 250m
            memory: 256Mi
          limits:
            cpu: 1000m
            memory: 512Mi
```

**Scaling:**
```bash
# Manual scaling
kubectl scale deployment lucia -n lucia --replicas=5

# Or patch the deployment
kubectl patch deployment lucia -n lucia -p '{"spec":{"replicas":5}}'
```

### Redis StatefulSet (03-redis.yaml)

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: lucia-redis
  namespace: lucia
spec:
  replicas: 1
  volumeClaimTemplates:
  - metadata:
      name: redis-data
    spec:
      accessModes: ["ReadWriteOnce"]
      storageClassName: standard  # Change to your storage class
      resources:
        requests:
          storage: 2Gi  # Adjust based on needs
```

**Storage:**
```bash
# List available storage classes
kubectl get storageclass

# Update storage class in 03-redis.yaml
# storageClassName: fast-ssd

# Update size requirements
# storage: 10Gi  # For larger deployments
```

### Service and Ingress (04-deployment.yaml, 05-ingress.yaml)

```bash
# Access via port-forward
kubectl port-forward -n lucia svc/lucia 8080:80

# Or configure ingress for external access
# Edit 05-ingress.yaml to set your domain names
# Update these values:
# - lucia.local → your-domain.com
# - cert-manager issuer configuration
```

### Horizontal Pod Autoscaler (07-hpa.yaml)

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: lucia
spec:
  minReplicas: 2
  maxReplicas: 5
  targetCPUUtilizationPercentage: 70  # Scale up if CPU > 70%
  targetMemoryUtilizationPercentage: 80  # Scale up if memory > 80%
```

**Modify autoscaling:**
```bash
# Edit HPA settings
kubectl edit hpa lucia -n lucia

# Or delete to disable autoscaling
kubectl delete hpa lucia -n lucia
```

## Common Operations

### View Resources

```bash
# All resources in namespace
kubectl get all -n lucia

# Specific resource types
kubectl get deployment -n lucia
kubectl get pods -n lucia
kubectl get svc -n lucia
kubectl get configmap -n lucia
kubectl get secret -n lucia
kubectl get hpa -n lucia

# Detailed view
kubectl describe deployment lucia -n lucia
kubectl describe pod lucia-5d4b9c7f8-abc12 -n lucia
```

### View Logs

```bash
# Application logs
kubectl logs -n lucia -l app.kubernetes.io/name=lucia -f

# Specific pod
kubectl logs -n lucia lucia-5d4b9c7f8-abc12

# Redis logs
kubectl logs -n lucia lucia-redis-0 -f

# Last 100 lines
kubectl logs -n lucia lucia-5d4b9c7f8-abc12 --tail=100
```

### Update Configuration

```bash
# Edit ConfigMap
kubectl edit configmap lucia -n lucia

# Verify changes
kubectl get configmap lucia -n lucia -o yaml

# Restart deployment to apply
kubectl rollout restart deployment/lucia -n lucia

# Watch rollout
kubectl rollout status deployment/lucia -n lucia
```

### Scale Deployment

```bash
# Scale manually
kubectl scale deployment lucia -n lucia --replicas=3

# Check current replicas
kubectl get deployment lucia -n lucia -o jsonpath='{.spec.replicas}'

# Monitor scaling
kubectl get pods -n lucia -w
```

### Update Image

```bash
# Update to new version
kubectl set image deployment/lucia lucia=seiggy/lucia-agenthost:v1.1.0 -n lucia

# Check rollout
kubectl rollout status deployment/lucia -n lucia

# Rollback if needed
kubectl rollout undo deployment/lucia -n lucia
```

## Customization with Kustomize

### Modifying with Kustomize Overlays

Create environment-specific overlays:

```bash
# Directory structure
manifests/
├── kustomization.yaml      # Base kustomization
├── *.yaml                  # Base manifests
└── overlays/
    ├── dev/
    │   └── kustomization.yaml
    ├── staging/
    │   └── kustomization.yaml
    └── production/
        └── kustomization.yaml
```

**Example: production/kustomization.yaml**
```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization

bases:
  - ../../

replicas:
  - name: lucia
    count: 3

patchesJson6902:
  - target:
      group: apps
      version: v1
      kind: Deployment
      name: lucia
    patch: |-
      - op: replace
        path: /spec/template/spec/containers/0/resources/limits/cpu
        value: 2000m
```

**Apply overlay:**
```bash
kubectl apply -k overlays/production/
```

### Patching Resources

Add custom patches to `kustomization.yaml`:

```yaml
patches:
  - target:
      kind: Deployment
      name: lucia
    patch: |-
      - op: add
        path: /spec/template/spec/containers/0/env/-
        value:
          name: NEW_VAR
          value: "value"
```

## Troubleshooting

### Pod Not Starting

```bash
# Check pod status
kubectl describe pod lucia-5d4b9c7f8-abc12 -n lucia

# Check events
kubectl get events -n lucia --sort-by='.lastTimestamp'

# Check logs
kubectl logs -n lucia lucia-5d4b9c7f8-abc12
```

### Redis Connection Failed

```bash
# Test Redis connectivity
kubectl exec -it deployment/lucia -n lucia -- redis-cli -h lucia-redis ping

# Check Redis pod
kubectl get pod -n lucia -l app.kubernetes.io/component=redis
kubectl logs -n lucia lucia-redis-0

# Verify DNS
kubectl exec -it deployment/lucia -n lucia -- nslookup lucia-redis
```

### ConfigMap/Secret Changes Not Visible

```bash
# Restart deployment
kubectl rollout restart deployment/lucia -n lucia

# Watch restart
kubectl get pods -n lucia -w

# Verify configuration
kubectl describe pod lucia-5d4b9c7f8-abc12 -n lucia
```

### Ingress Not Working

```bash
# Check ingress resource
kubectl get ingress -n lucia
kubectl describe ingress lucia -n lucia

# Check ingress controller
kubectl get pods -n ingress-nginx

# Test connectivity
curl http://lucia.local/ -v
```

## Security Best Practices

### 1. Secret Management

**Current**: Base64 encoded secrets in ConfigMap (not for production)

**Recommended**: Use Sealed Secrets

```bash
# Install Sealed Secrets controller
kubectl apply -f https://github.com/bitnami-labs/sealed-secrets/releases/download/v0.24.0/controller.yaml

# Seal your secret
echo -n "my-secret" | kubectl create secret generic lucy-secret --dry-run=client --from-file=- -o yaml | kubeseal -f - > sealed-secret.yaml

# Apply sealed secret
kubectl apply -f sealed-secret.yaml
```

### 2. RBAC (Role-Based Access Control)

ServiceAccount `lucia` has minimal permissions:
- Read ConfigMaps
- Read Secrets
- Read Pods
- Read Services

No administrative permissions granted.

### 3. Pod Security

- **Non-root user**: Runs as UID 1000
- **Read-only root filesystem**: Except /tmp
- **No privilege escalation**: `allowPrivilegeEscalation: false`
- **Dropped capabilities**: All Linux capabilities dropped

### 4. Network Policies

```bash
# Enable network policies in 07-hpa.yaml or create separately
# Restrict traffic to/from pods
```

## Cleanup

```bash
# Delete all resources
kubectl delete -k .

# Or delete namespace (removes all)
kubectl delete namespace lucia

# Delete persistent volumes
kubectl delete pvc -n lucia --all
```

## Advanced Topics

### External Redis

Modify `03-redis.yaml` to point to external Redis:

```yaml
# Skip creating StatefulSet, use external endpoint
# Update ConfigMap with external host
Redis__Host: "redis-external.example.com"
Redis__Port: "6379"
```

### Custom Storage Classes

```bash
# Check available storage classes
kubectl get storageclass

# Update 03-redis.yaml
storageClassName: your-storage-class
```

### Monitoring with Prometheus

Pods are already annotated for Prometheus scraping:
```yaml
prometheus.io/scrape: "true"
prometheus.io/port: "8080"
prometheus.io/path: "/metrics"
```

### Multi-Environment Deployments

Use Kustomize overlays for dev/staging/production.

## Support and Documentation

- [Kubernetes Manifests Docs](README.md)
- [Lucia Kubernetes Guide](../README.md)
- [Helm Chart Alternative](../helm/)
- [Docker Deployment](../../docker/)

## License

See LICENSE file in repository root.
