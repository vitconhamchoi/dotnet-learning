# Docker & Kubernetes cho .NET Applications

## Mục Lục
1. [Dockerfile Best Practices cho .NET](#dockerfile-best-practices)
2. [Multi-stage Builds](#multi-stage-builds)
3. [Docker Compose cho Local Dev](#docker-compose)
4. [Kubernetes Concepts](#kubernetes-concepts)
5. [Kubernetes Manifests](#kubernetes-manifests)
6. [Helm Charts](#helm-charts)
7. [Health Checks trong K8s](#health-checks)
8. [Rolling Updates](#rolling-updates)
9. [Complete Sample](#complete-sample)
10. [Best Practices](#best-practices)

---

## 1. Dockerfile Best Practices cho .NET

### Tại sao cần Dockerfile tốt?

```
Vấn đề thường gặp:
❌ Image size lớn (>1GB)
❌ Build chậm (không cache layers)
❌ Security vulnerabilities
❌ Chạy với root user
❌ Sensitive data trong image

Giải pháp:
✅ Multi-stage builds
✅ .dockerignore file
✅ Non-root user
✅ Minimal base images
✅ Layer caching optimization
```

### .dockerignore

```gitignore
# .dockerignore
**/.git
**/.gitignore
**/.vs
**/.vscode
**/*.[Oo]bj
**/*.[Bb]in
**/node_modules
**/wwwroot/lib
**/*.user
**/*.suo
**/TestResults
**/publish
**/secrets.json
**/.env
**/appsettings.Development.json
**/*.md
**/Dockerfile*
**/docker-compose*
**/.dockerignore
```

---

## 2. Multi-stage Builds

### Dockerfile cho ASP.NET Core API

```dockerfile
# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Create non-root user cho security
RUN addgroup -g 1000 appgroup && \
    adduser -u 1000 -G appgroup -s /bin/sh -D appuser

# =====================================================
# Stage 2: Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files trước (layer caching)
# Chỉ restore khi .csproj thay đổi
COPY ["src/Services/OrderService/ECommerce.OrderService.API/ECommerce.OrderService.API.csproj", 
      "src/Services/OrderService/ECommerce.OrderService.API/"]
COPY ["src/Services/OrderService/ECommerce.OrderService.Application/ECommerce.OrderService.Application.csproj",
      "src/Services/OrderService/ECommerce.OrderService.Application/"]
COPY ["src/Services/OrderService/ECommerce.OrderService.Domain/ECommerce.OrderService.Domain.csproj",
      "src/Services/OrderService/ECommerce.OrderService.Domain/"]
COPY ["src/Services/OrderService/ECommerce.OrderService.Infrastructure/ECommerce.OrderService.Infrastructure.csproj",
      "src/Services/OrderService/ECommerce.OrderService.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "src/Services/OrderService/ECommerce.OrderService.API/ECommerce.OrderService.API.csproj" \
    --runtime linux-musl-x64

# Copy source code
COPY . .

# Build
ARG BUILD_CONFIGURATION=Release
RUN dotnet build "src/Services/OrderService/ECommerce.OrderService.API/ECommerce.OrderService.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build \
    --no-restore

# =====================================================
# Stage 3: Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/Services/OrderService/ECommerce.OrderService.API/ECommerce.OrderService.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    --runtime linux-musl-x64 \
    --self-contained false \
    /p:PublishSingleFile=false \
    /p:UseAppHost=false

# =====================================================
# Stage 4: Final runtime image
FROM base AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Set ownership
RUN chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# Environment variables
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Ho_Chi_Minh

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ECommerce.OrderService.API.dll"]
```

### Dockerfile cho Worker Service

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY ["src/Workers/InventoryWorker/InventoryWorker.csproj", "src/Workers/InventoryWorker/"]
RUN dotnet restore "src/Workers/InventoryWorker/InventoryWorker.csproj"

COPY . .
RUN dotnet publish "src/Workers/InventoryWorker/InventoryWorker.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN adduser -u 1000 -D appuser
USER appuser

ENTRYPOINT ["dotnet", "InventoryWorker.dll"]
```

---

## 3. Docker Compose cho Local Development

```yaml
# docker-compose.yml
version: '3.9'

# Shared network
networks:
  ecommerce-network:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16

# Named volumes
volumes:
  postgres-orders-data:
  postgres-products-data:
  mongo-data:
  redis-data:
  rabbitmq-data:

services:
  # ==================== Infrastructure ====================

  # PostgreSQL cho Order Service
  orders-db:
    image: postgres:16-alpine
    container_name: orders-db
    environment:
      POSTGRES_DB: orders_db
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres_secret
    volumes:
      - postgres-orders-data:/var/lib/postgresql/data
      - ./scripts/init-orders-db.sql:/docker-entrypoint-initdb.d/init.sql
    ports:
      - "5432:5432"
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d orders_db"]
      interval: 10s
      timeout: 5s
      retries: 5

  # MongoDB cho Product Service
  products-db:
    image: mongo:7.0
    container_name: products-db
    environment:
      MONGO_INITDB_ROOT_USERNAME: mongo
      MONGO_INITDB_ROOT_PASSWORD: mongo_secret
      MONGO_INITDB_DATABASE: products_db
    volumes:
      - mongo-data:/data/db
    ports:
      - "27017:27017"
    networks:
      - ecommerce-network
    healthcheck:
      test: echo 'db.runCommand("ping").ok' | mongosh localhost:27017/test --quiet
      interval: 10s
      timeout: 5s
      retries: 5

  # Redis Cache
  redis:
    image: redis:7.2-alpine
    container_name: redis
    command: redis-server --appendonly yes --requirepass redis_secret
    volumes:
      - redis-data:/data
    ports:
      - "6379:6379"
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "redis_secret", "ping"]
      interval: 10s
      timeout: 3s
      retries: 5

  # RabbitMQ Message Broker
  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    container_name: rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: rabbit
      RABBITMQ_DEFAULT_PASS: rabbit_secret
      RABBITMQ_DEFAULT_VHOST: ecommerce
    volumes:
      - rabbitmq-data:/var/lib/rabbitmq
    ports:
      - "5672:5672"   # AMQP
      - "15672:15672" # Management UI
    networks:
      - ecommerce-network
    healthcheck:
      test: rabbitmq-diagnostics -q ping
      interval: 30s
      timeout: 30s
      retries: 3

  # ==================== Microservices ====================

  # API Gateway
  api-gateway:
    build:
      context: .
      dockerfile: src/ApiGateway/Dockerfile
    container_name: api-gateway
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ReverseProxy__Clusters__order-cluster__Destinations__order-1__Address=http://order-service:8080
      - ReverseProxy__Clusters__product-cluster__Destinations__product-1__Address=http://product-service:8080
      - ReverseProxy__Clusters__user-cluster__Destinations__user-1__Address=http://user-service:8080
    depends_on:
      order-service:
        condition: service_healthy
      product-service:
        condition: service_healthy
      user-service:
        condition: service_healthy
    networks:
      - ecommerce-network

  # Order Service
  order-service:
    build:
      context: .
      dockerfile: src/Services/OrderService/Dockerfile
    container_name: order-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__OrdersDb=Host=orders-db;Database=orders_db;Username=postgres;Password=postgres_secret
      - ConnectionStrings__Redis=redis:6379,password=redis_secret
      - Services__ProductService=http://product-service:8080
      - Services__UserService=http://user-service:8080
      - RabbitMQ__Host=rabbitmq
      - RabbitMQ__Username=rabbit
      - RabbitMQ__Password=rabbit_secret
      - RabbitMQ__VirtualHost=ecommerce
    depends_on:
      orders-db:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--tries=1", "--spider", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

  # Product Service
  product-service:
    build:
      context: .
      dockerfile: src/Services/ProductService/Dockerfile
    container_name: product-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__ProductsDb=mongodb://mongo:mongo_secret@products-db:27017/products_db?authSource=admin
      - ConnectionStrings__Redis=redis:6379,password=redis_secret
      - RabbitMQ__Host=rabbitmq
      - RabbitMQ__Username=rabbit
      - RabbitMQ__Password=rabbit_secret
    depends_on:
      products-db:
        condition: service_healthy
      redis:
        condition: service_healthy
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--tries=1", "--spider", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

  # User Service
  user-service:
    build:
      context: .
      dockerfile: src/Services/UserService/Dockerfile
    container_name: user-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__UsersDb=Host=users-db;Database=users_db;Username=postgres;Password=postgres_secret
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--tries=1", "--spider", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  # ==================== Observability ====================

  # Jaeger Distributed Tracing
  jaeger:
    image: jaegertracing/all-in-one:1.57
    container_name: jaeger
    ports:
      - "16686:16686" # UI
      - "6831:6831/udp" # UDP agent
      - "14268:14268" # HTTP collector
    networks:
      - ecommerce-network

  # Prometheus Metrics
  prometheus:
    image: prom/prometheus:v2.51.0
    container_name: prometheus
    volumes:
      - ./observability/prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"
    networks:
      - ecommerce-network

  # Grafana Dashboard
  grafana:
    image: grafana/grafana:10.4.0
    container_name: grafana
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - ./observability/grafana/dashboards:/etc/grafana/provisioning/dashboards
      - ./observability/grafana/datasources:/etc/grafana/provisioning/datasources
    ports:
      - "3000:3000"
    depends_on:
      - prometheus
    networks:
      - ecommerce-network

# Development override
# docker-compose -f docker-compose.yml -f docker-compose.override.yml up
```

```yaml
# docker-compose.override.yml (Development overrides)
version: '3.9'

services:
  order-service:
    volumes:
      # Hot reload trong development
      - ./src/Services/OrderService:/app/src
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - DOTNET_WATCH_SUPPRESS_MSBUILD_INCREMENTALISM=1
    command: ["dotnet", "watch", "run", "--project", "ECommerce.OrderService.API"]

  product-service:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Logging__LogLevel__Default=Debug
```

---

## 4. Kubernetes Concepts

```
Kubernetes Architecture:
┌──────────────────────────────────────────────────────────────────┐
│                      K8s CLUSTER                                 │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                   CONTROL PLANE                          │    │
│  │  ┌──────────────┐ ┌──────────────┐ ┌────────────────┐  │    │
│  │  │  API Server  │ │ Scheduler    │ │ Controller Mgr │  │    │
│  │  └──────────────┘ └──────────────┘ └────────────────┘  │    │
│  │  ┌──────────────┐                                       │    │
│  │  │  etcd        │ (Distributed key-value store)         │    │
│  │  └──────────────┘                                       │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                   WORKER NODE 1                          │    │
│  │  ┌─────────────────────────────────────────────────┐    │    │
│  │  │  POD: order-service                             │    │    │
│  │  │  ┌──────────────────────┐ ┌──────────────────┐  │    │    │
│  │  │  │ Container: order-api │ │ Sidecar: envoy   │  │    │    │
│  │  │  └──────────────────────┘ └──────────────────┘  │    │    │
│  │  └─────────────────────────────────────────────────┘    │    │
│  │  ┌──────────┐ ┌────────────┐ ┌──────────────────────┐  │    │
│  │  │ kubelet  │ │ kube-proxy │ │ Container Runtime    │  │    │
│  │  └──────────┘ └────────────┘ └──────────────────────┘  │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘

Key Objects:
- Pod: Unit nhỏ nhất, chứa 1+ containers
- ReplicaSet: Đảm bảo N pods luôn chạy
- Deployment: Quản lý ReplicaSet, rolling updates
- Service: Load balancer, service discovery
- ConfigMap: Non-sensitive configuration
- Secret: Sensitive data (encoded base64)
- Ingress: HTTP routing từ external
- HorizontalPodAutoscaler: Auto scaling
- PersistentVolume: Storage
```

---

## 5. Kubernetes Manifests

### Namespace

```yaml
# k8s/namespace.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: ecommerce
  labels:
    name: ecommerce
    environment: production
```

### ConfigMap

```yaml
# k8s/configmaps/order-service-config.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: order-service-config
  namespace: ecommerce
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  ASPNETCORE_URLS: "http://+:8080"
  Services__ProductService: "http://product-service:8080"
  Services__UserService: "http://user-service:8080"
  RabbitMQ__Host: "rabbitmq"
  RabbitMQ__VirtualHost: "ecommerce"
  Logging__LogLevel__Default: "Information"
  Logging__LogLevel__Microsoft: "Warning"
  Serilog__MinimumLevel__Default: "Information"
```

### Secret

```yaml
# k8s/secrets/order-service-secrets.yaml
apiVersion: v1
kind: Secret
metadata:
  name: order-service-secrets
  namespace: ecommerce
type: Opaque
# Giá trị phải được base64 encode:
# echo -n "value" | base64
data:
  ConnectionStrings__OrdersDb: SG9zdD1wb3N0Z3Jlcy1zZXJ2aWNlOy4uLg==
  RabbitMQ__Username: cmFiYml0
  RabbitMQ__Password: cmFiYml0X3Bhc3N3b3Jk
  Jwt__Secret: c3VwZXItc2VjcmV0LWp3dC1rZXktMTIzNDU2Nzg5MA==
```

### Deployment

```yaml
# k8s/deployments/order-service-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
  namespace: ecommerce
  labels:
    app: order-service
    version: "1.0.0"
    tier: backend
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1          # Tối đa thêm 1 pod trong khi update
      maxUnavailable: 0    # Không cho phép pod unavailable trong khi update
  template:
    metadata:
      labels:
        app: order-service
        version: "1.0.0"
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    spec:
      # Graceful shutdown
      terminationGracePeriodSeconds: 60
      
      # Pod security
      securityContext:
        runAsNonRoot: true
        runAsUser: 1000
        runAsGroup: 1000
        fsGroup: 1000
      
      # Init container để chờ database
      initContainers:
        - name: wait-for-db
          image: busybox:1.36
          command:
            - sh
            - -c
            - |
              until nc -z postgres-service 5432; do
                echo "Waiting for PostgreSQL..."
                sleep 2
              done
              echo "PostgreSQL is ready!"
      
      containers:
        - name: order-service
          image: ecommerce/order-service:1.0.0
          imagePullPolicy: Always
          ports:
            - name: http
              containerPort: 8080
              protocol: TCP
          
          # Environment từ ConfigMap
          envFrom:
            - configMapRef:
                name: order-service-config
            - secretRef:
                name: order-service-secrets
          
          # Individual env vars
          env:
            - name: POD_NAME
              valueFrom:
                fieldRef:
                  fieldPath: metadata.name
            - name: POD_NAMESPACE
              valueFrom:
                fieldRef:
                  fieldPath: metadata.namespace
            - name: NODE_NAME
              valueFrom:
                fieldRef:
                  fieldPath: spec.nodeName
          
          # Resource limits
          resources:
            requests:
              memory: "128Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
          
          # Liveness Probe - K8s sẽ restart nếu fail
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3
            successThreshold: 1
          
          # Readiness Probe - K8s không gửi traffic nếu fail
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 15
            periodSeconds: 5
            timeoutSeconds: 3
            failureThreshold: 3
            successThreshold: 1
          
          # Startup Probe - cho slow starting containers
          startupProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 5
            failureThreshold: 30  # 30 * 5s = 150s max startup time
          
          # Container security
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities:
              drop:
                - ALL
          
          # Volume mounts
          volumeMounts:
            - name: tmp-dir
              mountPath: /tmp
            - name: app-config
              mountPath: /app/config
              readOnly: true
      
      volumes:
        - name: tmp-dir
          emptyDir: {}
        - name: app-config
          configMap:
            name: order-service-config
      
      # Pod Affinity - ưu tiên spread across nodes
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                labelSelector:
                  matchExpressions:
                    - key: app
                      operator: In
                      values:
                        - order-service
                topologyKey: kubernetes.io/hostname
      
      imagePullSecrets:
        - name: registry-credentials
```

### Service

```yaml
# k8s/services/order-service-svc.yaml
apiVersion: v1
kind: Service
metadata:
  name: order-service
  namespace: ecommerce
  labels:
    app: order-service
spec:
  type: ClusterIP
  selector:
    app: order-service
  ports:
    - name: http
      protocol: TCP
      port: 8080
      targetPort: 8080
---
# LoadBalancer cho external access (hoặc dùng Ingress)
apiVersion: v1
kind: Service
metadata:
  name: api-gateway-external
  namespace: ecommerce
spec:
  type: LoadBalancer
  selector:
    app: api-gateway
  ports:
    - name: http
      port: 80
      targetPort: 8080
    - name: https
      port: 443
      targetPort: 8443
```

### Ingress

```yaml
# k8s/ingress/ecommerce-ingress.yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: ecommerce-ingress
  namespace: ecommerce
  annotations:
    kubernetes.io/ingress.class: nginx
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "60"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "60"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    nginx.ingress.kubernetes.io/rate-limit-window: "1m"
spec:
  tls:
    - hosts:
        - api.ecommerce.com
      secretName: ecommerce-tls
  rules:
    - host: api.ecommerce.com
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: api-gateway
                port:
                  number: 8080
```

### HorizontalPodAutoscaler

```yaml
# k8s/hpa/order-service-hpa.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
  namespace: ecommerce
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  minReplicas: 2
  maxReplicas: 10
  metrics:
    # Scale dựa trên CPU
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
    # Scale dựa trên Memory
    - type: Resource
      resource:
        name: memory
        target:
          type: AverageValue
          averageValue: 400Mi
    # Scale dựa trên custom metric (requests/second)
    - type: Pods
      pods:
        metric:
          name: http_requests_per_second
        target:
          type: AverageValue
          averageValue: "100"
  behavior:
    scaleUp:
      stabilizationWindowSeconds: 60  # Chờ 60s trước khi scale up thêm
      policies:
        - type: Pods
          value: 2       # Scale up tối đa 2 pods mỗi lần
          periodSeconds: 60
    scaleDown:
      stabilizationWindowSeconds: 300  # Chờ 5 phút trước khi scale down
      policies:
        - type: Pods
          value: 1       # Scale down tối đa 1 pod mỗi lần
          periodSeconds: 120
```

---

## 6. Helm Charts

### Chart Structure

```
ecommerce-chart/
├── Chart.yaml
├── values.yaml
├── values-dev.yaml
├── values-prod.yaml
├── templates/
│   ├── _helpers.tpl
│   ├── deployment.yaml
│   ├── service.yaml
│   ├── ingress.yaml
│   ├── configmap.yaml
│   ├── secret.yaml
│   ├── hpa.yaml
│   └── NOTES.txt
└── charts/          # Dependencies
```

```yaml
# Chart.yaml
apiVersion: v2
name: ecommerce
description: E-Commerce Microservices Helm Chart
type: application
version: 1.0.0
appVersion: "1.0.0"
dependencies:
  - name: postgresql
    version: "15.0.0"
    repository: https://charts.bitnami.com/bitnami
    condition: postgresql.enabled
  - name: redis
    version: "19.0.0"
    repository: https://charts.bitnami.com/bitnami
    condition: redis.enabled
  - name: rabbitmq
    version: "14.0.0"
    repository: https://charts.bitnami.com/bitnami
    condition: rabbitmq.enabled
```

```yaml
# values.yaml
global:
  imageRegistry: ""
  imagePullSecrets: []
  storageClass: ""

orderService:
  enabled: true
  image:
    repository: ecommerce/order-service
    tag: "1.0.0"
    pullPolicy: Always
  replicaCount: 3
  resources:
    requests:
      memory: "128Mi"
      cpu: "100m"
    limits:
      memory: "512Mi"
      cpu: "500m"
  autoscaling:
    enabled: true
    minReplicas: 2
    maxReplicas: 10
    targetCPUUtilizationPercentage: 70
  config:
    logLevel: "Information"
  secrets:
    dbConnectionString: ""  # Override này trong values-prod.yaml

ingress:
  enabled: true
  className: nginx
  host: api.ecommerce.com
  tls:
    enabled: true
    secretName: ecommerce-tls
```

```yaml
# templates/deployment.yaml
{{- if .Values.orderService.enabled }}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "ecommerce.fullname" . }}-order-service
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "ecommerce.labels" . | nindent 4 }}
    app.kubernetes.io/component: order-service
spec:
  replicas: {{ .Values.orderService.replicaCount }}
  selector:
    matchLabels:
      {{- include "ecommerce.selectorLabels" . | nindent 6 }}
      app.kubernetes.io/component: order-service
  template:
    metadata:
      labels:
        {{- include "ecommerce.selectorLabels" . | nindent 8 }}
        app.kubernetes.io/component: order-service
      annotations:
        checksum/config: {{ include (print $.Template.BasePath "/configmap.yaml") . | sha256sum }}
    spec:
      containers:
        - name: order-service
          image: "{{ .Values.orderService.image.repository }}:{{ .Values.orderService.image.tag }}"
          imagePullPolicy: {{ .Values.orderService.image.pullPolicy }}
          resources:
            {{- toYaml .Values.orderService.resources | nindent 12 }}
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 15
            periodSeconds: 5
{{- end }}
```

---

## 7. Health Checks trong .NET cho K8s

```csharp
// Program.cs - Health checks cho K8s
builder.Services.AddHealthChecks()
    // Self check - luôn healthy (liveness)
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    
    // Database check (readiness)
    .AddNpgsql(
        connectionString: builder.Configuration.GetConnectionString("OrdersDb")!,
        name: "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "database" })
    
    // Redis check (readiness)
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "cache" })
    
    // RabbitMQ check (readiness)
    .AddRabbitMQ(
        rabbitConnectionString: builder.Configuration["RabbitMQ:ConnectionString"]!,
        name: "rabbitmq",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "messaging" })
    
    // Downstream service check (readiness)
    .AddUrlGroup(
        uri: new Uri("http://product-service:8080/health"),
        name: "product-service",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "dependencies" });

// Liveness endpoint - K8s restarts pod nếu unhealthy
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Timestamp = DateTime.UtcNow
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// Readiness endpoint - K8s không gửi traffic nếu unhealthy
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Full health check với details
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

---

## 8. Rolling Updates và Deployment Strategies

```yaml
# Blue-Green Deployment
# Blue (current):
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service-blue
  namespace: ecommerce
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
      slot: blue
  template:
    metadata:
      labels:
        app: order-service
        slot: blue
    spec:
      containers:
        - name: order-service
          image: ecommerce/order-service:1.0.0

---
# Green (new version):
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service-green
  namespace: ecommerce
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
      slot: green
  template:
    metadata:
      labels:
        app: order-service
        slot: green
    spec:
      containers:
        - name: order-service
          image: ecommerce/order-service:2.0.0

---
# Service trỏ vào blue (switch sang green bằng cách đổi selector)
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
    slot: blue  # Đổi thành "green" để switch traffic
  ports:
    - port: 8080
```

```bash
# Rolling Update Commands
# Update image
kubectl set image deployment/order-service \
  order-service=ecommerce/order-service:2.0.0 \
  -n ecommerce

# Monitor rollout
kubectl rollout status deployment/order-service -n ecommerce

# Rollback nếu có vấn đề
kubectl rollout undo deployment/order-service -n ecommerce

# Rollback về version cụ thể
kubectl rollout undo deployment/order-service \
  --to-revision=2 -n ecommerce

# Xem rollout history
kubectl rollout history deployment/order-service -n ecommerce
```

---

## 9. .NET Application với K8s graceful shutdown

```csharp
// Graceful shutdown trong .NET
public class OrderServiceWorker : BackgroundService
{
    private readonly ILogger<OrderServiceWorker> _logger;

    public OrderServiceWorker(ILogger<OrderServiceWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order service worker starting");

        // K8s gửi SIGTERM khi pod bị terminate
        stoppingToken.Register(() =>
        {
            _logger.LogInformation("Shutdown signal received, completing pending work...");
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOrdersAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker stopped gracefully");
            }
        }
    }

    private async Task ProcessPendingOrdersAsync(CancellationToken ct)
    {
        // Process work...
    }
}

// Program.cs - Configure graceful shutdown
builder.Host.ConfigureHostOptions(options =>
{
    // Cho 30 giây để finish in-flight requests
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

// Middleware để handle graceful shutdown
app.Use(async (context, next) =>
{
    var appLifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
    
    if (appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        context.Response.Headers["Connection"] = "close";
    }
    
    await next();
});
```

---

## 10. Best Practices

### Resource Requests và Limits

```yaml
# Luôn set resource requests và limits
resources:
  requests:
    # Minimum guaranteed resources
    memory: "128Mi"   # 128 megabytes
    cpu: "100m"       # 100 millicores = 0.1 CPU
  limits:
    # Maximum allowed resources
    memory: "512Mi"
    cpu: "500m"       # 0.5 CPU

# Tips:
# - memory limit = OOMKill threshold
# - CPU limit = throttling (không kill pod)
# - Requests dùng cho scheduling
# - Limits > Requests = Burstable QoS
# - Requests = Limits = Guaranteed QoS (tốt nhất)
```

### Namespace Resource Quotas

```yaml
apiVersion: v1
kind: ResourceQuota
metadata:
  name: ecommerce-quota
  namespace: ecommerce
spec:
  hard:
    requests.cpu: "4"
    requests.memory: 8Gi
    limits.cpu: "8"
    limits.memory: 16Gi
    pods: "50"
    services: "20"
```

### Network Policies

```yaml
# Chỉ cho order-service nói chuyện với product-service và user-service
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: order-service-network-policy
  namespace: ecommerce
spec:
  podSelector:
    matchLabels:
      app: order-service
  policyTypes:
    - Ingress
    - Egress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: api-gateway
      ports:
        - protocol: TCP
          port: 8080
  egress:
    - to:
        - podSelector:
            matchLabels:
              app: product-service
      ports:
        - protocol: TCP
          port: 8080
    - to:
        - podSelector:
            matchLabels:
              app: user-service
      ports:
        - protocol: TCP
          port: 8080
    # Allow DNS
    - ports:
        - protocol: UDP
          port: 53
```

---

## Tổng Kết

```
Container Strategy cho .NET:
┌─────────────────────────────────────────────────────┐
│  Development    │  Docker Compose                   │
│  Testing        │  Kind / k3d (local K8s)           │
│  Staging        │  K8s + Helm                       │
│  Production     │  AKS / EKS / GKE + Helm + GitOps  │
└─────────────────────────────────────────────────────┘

Checklist trước khi deploy lên K8s:
✅ Multi-stage Dockerfile (nhỏ, secure)
✅ Non-root user trong container
✅ Resource requests và limits
✅ Liveness và Readiness probes
✅ Graceful shutdown (terminationGracePeriodSeconds)
✅ Health check endpoints (/health/live, /health/ready)
✅ ConfigMap cho config, Secret cho sensitive data
✅ HPA cho auto-scaling
✅ PodDisruptionBudget cho high availability
✅ Network policies cho security
✅ Resource quotas cho namespace
✅ Image scanning (Trivy, Snyk)
```
