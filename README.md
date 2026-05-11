# 🚀 .NET Learning — Từ Nền Tảng Đến Production

Kho tài nguyên học .NET chuyên sâu, tập trung vào **distributed systems**, **kiến trúc microservices**, và các **công nghệ mới nhất** trong hệ sinh thái .NET. Nội dung được thiết kế theo hướng thực chiến, phù hợp cho developer muốn nâng level lên senior/principal engineer.

---

## .NET là gì?

**.NET** là nền tảng phát triển phần mềm **mã nguồn mở, đa nền tảng** do Microsoft xây dựng và duy trì. Với .NET bạn có thể phát triển:

- 🌐 **Web API & Backend** — ASP.NET Core, Minimal APIs, gRPC
- 🖥️ **Web Frontend** — Blazor (WebAssembly & Server)
- 📱 **Mobile & Desktop** — .NET MAUI, WPF, WinForms
- ☁️ **Cloud & Microservices** — Azure, Kubernetes, Dapr, .NET Aspire
- 🤖 **AI & ML** — Microsoft Agent Framework, ML.NET, Microsoft.Extensions.AI
- ⚡ **Real-time** — SignalR, gRPC Streaming, Server-Sent Events

### Tại sao chọn .NET?

| Đặc điểm | Chi tiết |
|---|---|
| **Hiệu năng cao** | Thường xuyên đứng top trong benchmark TechEmpower (Web Framework Benchmarks) |
| **Đa nền tảng** | Chạy trên Windows, Linux, macOS, container, WASM |
| **Hệ sinh thái phong phú** | NuGet với hàng triệu package; tích hợp tốt với Azure, AWS, GCP |
| **Ngôn ngữ mạnh** | C# — một trong những ngôn ngữ type-safe, expressive hàng đầu |
| **Cộng đồng lớn** | Được Microsoft bảo trợ, LTS release định kỳ, roadmap công khai |
| **AI-ready** | Tích hợp sẵn Microsoft.Extensions.AI, Microsoft Agent Framework |

---

## 📁 Cấu trúc repo

```
dotnet-learning/
├── tutorials/          # 21 tutorial chuyên sâu về distributed .NET
├── samples/            # Code mẫu chạy được
│   └── MicrosoftAgentFramework/   # AI Agent với Microsoft Agent Framework
├── dotnet-distributed-apps-report.md      # Báo cáo tổng quan distributed apps
└── microsoft-agent-framework-detailed-guide.md  # Hướng dẫn AI Agent framework
```

---

## 📚 Tutorials — 21 Bài Học Chuyên Sâu

Nội dung chia làm 4 phần, từ thư viện cốt lõi đến kỹ năng vận hành production.

### 🟢 Phần 1 — Nền Tảng (Bài 1–7)

Các thư viện cốt lõi trong hệ sinh thái .NET distributed application:

| # | Tutorial | Nội dung chính |
|---|---|---|
| 01 | [Orleans](tutorials/01-orleans-detailed-tutorial.md) | Virtual Actor model, stateful distributed computation |
| 02 | [MassTransit](tutorials/02-masstransit-detailed-tutorial.md) | Message bus, consumer pipeline, saga state machine |
| 03 | [Marten](tutorials/03-marten-detailed-tutorial.md) | Event sourcing và document store trên PostgreSQL |
| 04 | [Wolverine](tutorials/04-wolverine-detailed-tutorial.md) | Lightweight messaging và HTTP handler |
| 05 | [Dapr](tutorials/05-dapr-detailed-tutorial.md) | Sidecar runtime, pub/sub, state, bindings, workflow |
| 06 | [Hot Chocolate](tutorials/06-hotchocolate-detailed-tutorial.md) | GraphQL server với DataLoader, subscriptions |
| 07 | [.NET Aspire](tutorials/07-dotnet-aspire-detailed-tutorial.md) | Application composition, orchestration, observability |

### 🔵 Phần 2 — Patterns Nâng Cao (Bài 8–13)

Các patterns thiết yếu để xây dựng hệ thống phức tạp:

| # | Tutorial | Nội dung chính |
|---|---|---|
| 08 | [CQRS & Event Sourcing](tutorials/08-cqrs-eventsourcing-tutorial.md) | Command/Query separation, event store, projections, time travel |
| 09 | [Saga Pattern](tutorials/09-saga-distributed-transactions-tutorial.md) | Choreography vs orchestration, compensating transactions |
| 10 | [API Gateway & BFF (YARP)](tutorials/10-api-gateway-bff-yarp-tutorial.md) | Rate limiting, auth tập trung, BFF cho web/mobile |
| 11 | [Distributed Caching (Redis)](tutorials/11-distributed-caching-redis-tutorial.md) | Cache strategies, stampede prevention, HybridCache |
| 12 | [Kafka Event Streaming](tutorials/12-kafka-event-streaming-tutorial.md) | High-throughput pipeline, outbox pattern, dead letter queue |
| 13 | [Database Sharding & Multi-tenancy](tutorials/13-database-sharding-multitenancy-tutorial.md) | Read replicas, hash sharding, row-level security |

### 🔴 Phần 3 — Scale và Production (Bài 14–20)

Kỹ năng vận hành hệ thống production ở quy mô lớn:

| # | Tutorial | Nội dung chính |
|---|---|---|
| 14 | [Resilience với Polly v8](tutorials/14-resilience-polly-tutorial.md) | Retry, circuit breaker, bulkhead, timeout, hedge, fallback |
| 15 | [Observability (OpenTelemetry)](tutorials/15-observability-opentelemetry-tutorial.md) | Distributed tracing, structured logging, metrics, alerting |
| 16 | [Security in Distributed Systems](tutorials/16-security-distributed-systems-tutorial.md) | Zero trust, OAuth2/OIDC, mTLS, secret management |
| 17 | [Kubernetes Deployment](tutorials/17-kubernetes-deployment-tutorial.md) | Helm, HPA, rolling update, canary deploy, CI/CD |
| 18 | [gRPC trong .NET](tutorials/18-grpc-dotnet-tutorial.md) | Protocol Buffers, streaming, interceptors, client generation |
| 19 | [Performance Engineering](tutorials/19-performance-engineering-tutorial.md) | Profiling, memory optimization, async best practices |
| 20 | [System Design at Scale](tutorials/20-system-design-at-scale-tutorial.md) | Reference architecture 1 tỷ user, capacity planning, chaos engineering |

### 🟣 Phần 4 — Công Nghệ Mới Nhất (.NET 10 / C# 14)

| # | Tutorial | Nội dung chính |
|---|---|---|
| 21 | [Latest .NET Technologies](tutorials/21-latest-dotnet-technologies-integration-guide.md) | C# 14, OpenAPI 3.1, SSE, HybridCache, Native AOT, Blazor 10, Passkey Auth, Microsoft.Extensions.AI, .NET Aspire |

---

## 🧪 Samples — Code Mẫu Chạy Được

### Microsoft Agent Framework

Bộ mẫu minh hoạ cách xây dựng AI Agent với [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) — framework thế hệ mới, thay thế Semantic Kernel Agents và AutoGen:

| Mẫu | Mô tả |
|---|---|
| [01 — Basic Agent](samples/MicrosoftAgentFramework/01-BasicAgent) | Agent chat đơn giản với `AgentSession` cho multi-turn conversation |
| [02 — Agent with Tools](samples/MicrosoftAgentFramework/02-AgentWithTools) | Agent tích hợp tools qua `AIFunctionFactory` (chuẩn `Microsoft.Extensions.AI`) |
| [03 — Multi-Agent Sequential Workflow](samples/MicrosoftAgentFramework/03-WriterCriticWorkflow) | Workflow nhiều agent (Planner → Executor → Reviewer) với `AgentWorkflowBuilder` |

---

## 🛣️ Lộ Trình Học Đề Xuất

```
Mới bắt đầu với .NET distributed systems?
  └─▶ Bài 07 (.NET Aspire) → Bài 01 (Orleans) → Bài 02 (MassTransit) → Bài 05 (Dapr)

Muốn nắm vững kiến trúc?
  └─▶ Bài 08 (CQRS) → Bài 09 (Saga) → Bài 10 (API Gateway) → Bài 12 (Kafka)

Chuẩn bị cho production?
  └─▶ Bài 14 (Resilience) → Bài 15 (Observability) → Bài 17 (Kubernetes) → Bài 19 (Performance)

Khám phá AI với .NET?
  └─▶ samples/MicrosoftAgentFramework → Bài 21 (Latest .NET Tech)
```

---

## 🔧 Yêu Cầu Môi Trường

- [.NET 9 SDK](https://dotnet.microsoft.com/download) trở lên (khuyến nghị .NET 10 cho bài 21)
- Docker Desktop — chạy các dependency như Redis, Kafka, PostgreSQL
- IDE: [Visual Studio 2022](https://visualstudio.microsoft.com/) hoặc [VS Code](https://code.visualstudio.com/) + [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)

---

## 📖 Tài Liệu Tham Khảo

- [Tài liệu chính thức .NET](https://docs.microsoft.com/dotnet)
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [.NET Architecture Guides](https://dotnet.microsoft.com/learn/dotnet/architecture-guides)
- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework)
- [Microsoft Agent Framework GitHub](https://github.com/microsoft/agent-framework)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)

---

<div align="center">
  <sub>Được xây dựng với ❤️ để giúp developer Việt Nam làm chủ .NET ecosystem</sub>
</div>
