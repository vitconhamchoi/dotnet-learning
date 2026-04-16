# Distributed .NET Tutorials

Bộ tài liệu này gồm **21 tutorial chuyên sâu** từ cơ bản đến nâng cao, đào tạo senior engineer xây dựng distributed application toàn diện với khả năng scale cho hàng tỉ người dùng.

## 🟢 Phần 1: Nền tảng (Bài 1–7)

Các thư viện cốt lõi trong hệ sinh thái .NET distributed application:

1. **Orleans** – Virtual Actor model, stateful distributed computation
2. **MassTransit** – Message bus, consumer pipeline, saga state machine
3. **Marten** – Event sourcing và document store trên PostgreSQL
4. **Wolverine** – Lightweight messaging và HTTP handler
5. **Dapr** – Sidecar runtime, pub/sub, state, bindings, workflow
6. **Hot Chocolate** – GraphQL server với DataLoader, subscriptions
7. **.NET Aspire** – Application composition, orchestration, observability

## 🔵 Phần 2: Patterns nâng cao (Bài 8–13)

Các patterns thiết yếu để xây dựng hệ thống phức tạp:

8. **CQRS & Event Sourcing** – Command/Query separation, event store, projections, time travel debugging
9. **Saga Pattern** – Choreography vs orchestration, compensating transactions, idempotency
10. **API Gateway & BFF với YARP** – Rate limiting, auth tập trung, BFF cho web/mobile, circuit breaker
11. **Distributed Caching với Redis** – Cache strategies, stampede prevention, Redis Cluster, HybridCache
12. **Kafka Event Streaming** – High-throughput pipeline, consumer groups, outbox pattern, dead letter queue
13. **Database Sharding & Multi-tenancy** – Read replicas, hash sharding, row-level security, partitioning

## 🔴 Phần 3: Scale và Production (Bài 14–20)

Kỹ năng vận hành hệ thống production ở quy mô lớn:

14. **Resilience với Polly v8** – Retry, circuit breaker, bulkhead, timeout, hedge, fallback
15. **Observability với OpenTelemetry** – Distributed tracing, structured logging, metrics, health checks, alerting
16. **Security in Distributed Systems** – Zero trust, OAuth2/OIDC, mTLS, secret management, rate limiting
17. **Kubernetes Deployment** – Helm, HPA, PodDisruptionBudget, rolling update, canary, CI/CD
18. **gRPC trong .NET** – Protocol Buffers, streaming, interceptors, client generation
19. **Performance Engineering** – Profiling, memory optimization, async best practices, bulk operations
20. **System Design at Scale** – Reference architecture cho 1 tỷ user, capacity planning, chaos engineering

---

Mỗi bài được viết theo hướng thực chiến:
- Giải thích vấn đề và lý do tại sao cần kỹ thuật này
- Khái niệm cốt lõi với ví dụ trực quan
- Code mẫu đầy đủ, có thể chạy được
- Khi nào nên dùng và không nên dùng
- Checklist production để deploy an toàn

## Danh sách file

### Phần 1 – Nền tảng
- `01-orleans-detailed-tutorial.md`
- `02-masstransit-detailed-tutorial.md`
- `03-marten-detailed-tutorial.md`
- `04-wolverine-detailed-tutorial.md`
- `05-dapr-detailed-tutorial.md`
- `06-hotchocolate-detailed-tutorial.md`
- `07-dotnet-aspire-detailed-tutorial.md`

### Phần 2 – Patterns nâng cao
- `08-cqrs-eventsourcing-tutorial.md`
- `09-saga-distributed-transactions-tutorial.md`
- `10-api-gateway-bff-yarp-tutorial.md`
- `11-distributed-caching-redis-tutorial.md`
- `12-kafka-event-streaming-tutorial.md`
- `13-database-sharding-multitenancy-tutorial.md`

### Phần 3 – Scale và Production
- `14-resilience-polly-tutorial.md`
- `15-observability-opentelemetry-tutorial.md`
- `16-security-distributed-systems-tutorial.md`
- `17-kubernetes-deployment-tutorial.md`
- `18-grpc-dotnet-tutorial.md`
- `19-performance-engineering-tutorial.md`
- `20-system-design-at-scale-tutorial.md`

### Phần 4 – Công Nghệ Mới Nhất (.NET 10 / C# 14)
- `21-latest-dotnet-technologies-integration-guide.md` — Hướng dẫn tích hợp 9 công nghệ mới nhất: C# 14, OpenAPI 3.1, SSE, HybridCache, Native AOT, Blazor 10, Passkey Auth, Microsoft.Extensions.AI, .NET Aspire
