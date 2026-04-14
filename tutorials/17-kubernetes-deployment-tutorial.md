# Deployment at Scale với Kubernetes và Helm: Vận hành hệ thống cho hàng tỉ người dùng

## 1. Từ Docker đến Kubernetes: tại sao cần Orchestrator

Docker giải quyết bài toán "chạy ứng dụng nhất quán mọi nơi". Nhưng khi có 50 service, mỗi service cần 5-10 instance, bạn sẽ thấy Docker Compose không đủ:

- Ai restart container khi nó crash?
- Làm sao auto-scale khi traffic tăng?
- Làm sao deploy update không gây downtime?
- Làm sao phân phối tải giữa các instance?
- Làm sao quản lý secret và config?
- Làm sao isolate network giữa các service?

**Kubernetes** (K8s) giải quyết tất cả những điều đó. Nó là production-grade container orchestrator với declarative configuration: bạn mô tả desired state, K8s tự làm cho hệ thống đạt được state đó.

---

## 2. Dockerfile tối ưu cho .NET

```dockerfile
# Multi-stage build để minimize image size
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files trước để cache layer restore
COPY ["src/OrderService/OrderService.csproj", "src/OrderService/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]

# Restore riêng để cache
RUN dotnet restore "src/OrderService/OrderService.csproj"

# Copy source và build
COPY . .
RUN dotnet publish "src/OrderService/OrderService.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime image - nhỏ hơn rất nhiều
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Security: không chạy với root
RUN addgroup --system appgroup && adduser --system appuser --ingroup appgroup
USER appuser

WORKDIR /app
COPY --from=build /app/publish .

# Health check trong container
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderService.dll"]
```

---

## 3. Kubernetes Deployment: triển khai service

### 3.1 Deployment cơ bản

```yaml
# order-service-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
  namespace: production
  labels:
    app: order-service
    version: "2.3.1"
spec:
  replicas: 3   # Tối thiểu 3 replicas cho HA
  
  selector:
    matchLabels:
      app: order-service
  
  template:
    metadata:
      labels:
        app: order-service
        version: "2.3.1"
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    
    spec:
      # Anti-affinity: tránh chạy nhiều replica trên cùng node
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                labelSelector:
                  matchLabels:
                    app: order-service
                topologyKey: kubernetes.io/hostname
      
      containers:
        - name: order-service
          image: myregistry.azurecr.io/order-service:2.3.1
          imagePullPolicy: Always
          
          ports:
            - containerPort: 8080
              protocol: TCP
          
          # Environment variables từ ConfigMap và Secret
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: "Production"
            - name: ASPNETCORE_URLS
              value: "http://+:8080"
            - name: ConnectionStrings__Postgres
              valueFrom:
                secretKeyRef:
                  name: order-service-secrets
                  key: database-connection-string
            - name: ConnectionStrings__Redis
              valueFrom:
                secretKeyRef:
                  name: order-service-secrets
                  key: redis-connection-string
          
          # Config từ ConfigMap
          envFrom:
            - configMapRef:
                name: order-service-config
          
          # Resource limits: bắt buộc trong production
          resources:
            requests:
              memory: "256Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
          
          # Health checks
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 30
            timeoutSeconds: 10
            failureThreshold: 3
          
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3
          
          # Startup probe: đợi app warm up
          startupProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 5
            failureThreshold: 30  # Cho phép 30*5 = 150 giây để start
          
          # Security context
          securityContext:
            runAsNonRoot: true
            runAsUser: 1000
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities:
              drop: ["ALL"]
  
  # Rolling update strategy
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1       # Cho phép thêm 1 pod trong khi update
      maxUnavailable: 0  # Không được mất pod nào trong quá trình update

---
# Service
apiVersion: v1
kind: Service
metadata:
  name: order-service
  namespace: production
spec:
  selector:
    app: order-service
  ports:
    - port: 80
      targetPort: 8080
      protocol: TCP
  type: ClusterIP  # Internal service
```

---

## 4. HorizontalPodAutoscaler: tự động scale

```yaml
# HPA dựa trên CPU và custom metrics
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
  namespace: production
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  
  minReplicas: 3
  maxReplicas: 50
  
  metrics:
    # Scale khi CPU > 60%
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 60
    
    # Scale khi Memory > 70%
    - type: Resource
      resource:
        name: memory
        target:
          type: Utilization
          averageUtilization: 70
    
    # Scale dựa trên custom metric: số request đang chờ
    - type: Pods
      pods:
        metric:
          name: http_requests_in_flight
        target:
          type: AverageValue
          averageValue: "100"
  
  behavior:
    scaleUp:
      stabilizationWindowSeconds: 60   # Đợi 60s trước khi scale up
      policies:
        - type: Percent
          value: 100   # Scale up tối đa 100% mỗi lần
          periodSeconds: 60
    scaleDown:
      stabilizationWindowSeconds: 300  # Đợi 5 phút trước khi scale down
      policies:
        - type: Percent
          value: 25    # Scale down tối đa 25% mỗi lần
          periodSeconds: 60
```

---

## 5. PodDisruptionBudget: bảo vệ khi maintenance

```yaml
# Đảm bảo luôn có ít nhất 2 pods trong khi node drain
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: order-service-pdb
  namespace: production
spec:
  selector:
    matchLabels:
      app: order-service
  minAvailable: 2  # Hoặc: maxUnavailable: 1
```

---

## 6. Helm Chart: package Kubernetes manifests

### 6.1 Chart structure

```text
order-service/
├── Chart.yaml           # Chart metadata
├── values.yaml          # Default values
├── values-staging.yaml  # Staging overrides
├── values-production.yaml # Production overrides
└── templates/
    ├── deployment.yaml
    ├── service.yaml
    ├── hpa.yaml
    ├── pdb.yaml
    ├── configmap.yaml
    ├── serviceaccount.yaml
    ├── ingress.yaml
    └── _helpers.tpl
```

### 6.2 values.yaml

```yaml
# values.yaml
replicaCount: 3

image:
  repository: myregistry.azurecr.io/order-service
  tag: "latest"
  pullPolicy: Always

service:
  type: ClusterIP
  port: 80
  targetPort: 8080

resources:
  requests:
    memory: "256Mi"
    cpu: "100m"
  limits:
    memory: "512Mi"
    cpu: "500m"

autoscaling:
  enabled: true
  minReplicas: 3
  maxReplicas: 50
  targetCPUUtilizationPercentage: 60
  targetMemoryUtilizationPercentage: 70

config:
  aspnetcoreEnvironment: "Production"
  logLevel: "Information"
  otlpEndpoint: "http://otel-collector:4317"

health:
  liveness:
    path: /health/live
    initialDelaySeconds: 30
  readiness:
    path: /health/ready
    initialDelaySeconds: 10

podDisruptionBudget:
  enabled: true
  minAvailable: 2
```

### 6.3 Deployment template

```yaml
# templates/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "order-service.fullname" . }}
  labels:
    {{- include "order-service.labels" . | nindent 4 }}
spec:
  {{- if not .Values.autoscaling.enabled }}
  replicas: {{ .Values.replicaCount }}
  {{- end }}
  selector:
    matchLabels:
      {{- include "order-service.selectorLabels" . | nindent 6 }}
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0
  template:
    metadata:
      labels:
        {{- include "order-service.selectorLabels" . | nindent 8 }}
        version: {{ .Chart.AppVersion | quote }}
    spec:
      containers:
        - name: {{ .Chart.Name }}
          image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
          imagePullPolicy: {{ .Values.image.pullPolicy }}
          ports:
            - containerPort: {{ .Values.service.targetPort }}
          resources:
            {{- toYaml .Values.resources | nindent 12 }}
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: {{ .Values.config.aspnetcoreEnvironment | quote }}
          livenessProbe:
            httpGet:
              path: {{ .Values.health.liveness.path }}
              port: {{ .Values.service.targetPort }}
            initialDelaySeconds: {{ .Values.health.liveness.initialDelaySeconds }}
          readinessProbe:
            httpGet:
              path: {{ .Values.health.readiness.path }}
              port: {{ .Values.service.targetPort }}
            initialDelaySeconds: {{ .Values.health.readiness.initialDelaySeconds }}
```

### 6.4 Deploy với Helm

```bash
# Install chart mới
helm install order-service ./order-service \
  --namespace production \
  --values values-production.yaml \
  --set image.tag=2.3.1

# Upgrade
helm upgrade order-service ./order-service \
  --namespace production \
  --values values-production.yaml \
  --set image.tag=2.4.0 \
  --atomic \     # Rollback tự động nếu upgrade fail
  --wait \       # Đợi deployment hoàn tất
  --timeout 5m

# Rollback về version trước
helm rollback order-service 1 --namespace production

# Check history
helm history order-service --namespace production
```

---

## 7. CI/CD Pipeline: tự động hóa deployment

```yaml
# .github/workflows/deploy.yml
name: Build and Deploy

on:
  push:
    branches: [main]
    tags: ['v*']

jobs:
  build:
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.meta.outputs.version }}
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Docker metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: myregistry.azurecr.io/order-service
          tags: |
            type=semver,pattern={{version}}
            type=sha,prefix=sha-
      
      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./src/OrderService/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
  
  deploy-staging:
    needs: build
    runs-on: ubuntu-latest
    environment: staging
    
    steps:
      - name: Deploy to staging
        run: |
          helm upgrade order-service ./helm/order-service \
            --namespace staging \
            --values ./helm/order-service/values-staging.yaml \
            --set image.tag=${{ needs.build.outputs.image-tag }} \
            --atomic --wait --timeout 5m
      
      - name: Run smoke tests
        run: |
          ./scripts/smoke-tests.sh https://staging.myapp.com
  
  deploy-production:
    needs: [build, deploy-staging]
    runs-on: ubuntu-latest
    environment: production  # Requires manual approval
    
    steps:
      - name: Deploy to production (canary)
        run: |
          helm upgrade order-service ./helm/order-service \
            --namespace production \
            --values ./helm/order-service/values-production.yaml \
            --set image.tag=${{ needs.build.outputs.image-tag }} \
            --set replicaCount=1 \  # Canary: 1 pod trước
            --atomic --wait
      
      - name: Monitor canary (5 minutes)
        run: |
          ./scripts/monitor-canary.sh order-service 5
      
      - name: Full rollout
        run: |
          helm upgrade order-service ./helm/order-service \
            --namespace production \
            --values ./helm/order-service/values-production.yaml \
            --set image.tag=${{ needs.build.outputs.image-tag }} \
            --atomic --wait --timeout 10m
```

---

## 8. Zero-Downtime Deployment Strategies

### 8.1 Rolling Update (mặc định)

```text
Before: [v1] [v1] [v1]
Step 1: [v2] [v1] [v1]  ← v2 healthy, remove one v1
Step 2: [v2] [v2] [v1]  ← v2 healthy, remove one v1
Step 3: [v2] [v2] [v2]  ← Done!
```

### 8.2 Blue-Green Deployment

```bash
# Deploy green (new version) cạnh blue (current)
helm install order-service-green ./order-service \
  --set image.tag=2.4.0 \
  --set replicaCount=3

# Test green
kubectl run test --image=curlimages/curl -- \
  curl -f http://order-service-green/health

# Switch traffic từ blue sang green
kubectl patch service order-service \
  -p '{"spec":{"selector":{"version":"2.4.0"}}}'

# Sau khi confirm ok, delete blue
helm uninstall order-service-blue
```

### 8.3 Canary Deployment với Argo Rollouts

```yaml
# Argo Rollouts cho progressive delivery
apiVersion: argoproj.io/v1alpha1
kind: Rollout
metadata:
  name: order-service
spec:
  replicas: 10
  strategy:
    canary:
      steps:
        - setWeight: 10   # 10% traffic tới canary
        - pause: {duration: 5m}
        - analysis:
            templates:
              - templateName: success-rate
            args:
              - name: service-name
                value: order-service
        - setWeight: 30
        - pause: {duration: 5m}
        - setWeight: 60
        - pause: {duration: 5m}
        - setWeight: 100  # Full rollout
      
      # Auto-rollback nếu error rate cao
      analysis:
        successCondition: "result[0] >= 0.95"
        failureCondition: "result[0] < 0.90"
```

---

## 9. Resource Optimization và Cost Control

```yaml
# Vertical Pod Autoscaler: tự điều chỉnh requests/limits
apiVersion: autoscaling.k8s.io/v1
kind: VerticalPodAutoscaler
metadata:
  name: order-service-vpa
spec:
  targetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  updatePolicy:
    updateMode: "Off"  # Chỉ recommend, không tự update (safety)
  resourcePolicy:
    containerPolicies:
      - containerName: order-service
        minAllowed:
          cpu: "50m"
          memory: "128Mi"
        maxAllowed:
          cpu: "2"
          memory: "2Gi"
```

---

## 10. Checklist production cho Kubernetes Deployment

- [ ] Multi-replica deployment với anti-affinity rules
- [ ] Resource requests VÀ limits cho mọi container
- [ ] Liveness, readiness và startup probes
- [ ] HPA với cả CPU metrics và custom business metrics
- [ ] PodDisruptionBudget để bảo vệ khi maintenance
- [ ] Rolling update strategy với maxUnavailable: 0
- [ ] Helm chart với separate values per environment
- [ ] CI/CD pipeline với staging gate và canary deployment
- [ ] Non-root container user và readOnlyRootFilesystem
- [ ] Network policies để isolate service communication
- [ ] Secrets từ external store (Key Vault/Vault), không baked vào image
- [ ] Image scanning cho CVE trước khi deploy
- [ ] Resource quotas per namespace để tránh resource abuse
- [ ] Node auto-provisioning / cluster autoscaler
- [ ] Disaster recovery: backup, restore procedure được test định kỳ
