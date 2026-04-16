# Bài 16: Distributed Tracing & Observability với OpenTelemetry

## Mục lục

1. [Giới thiệu về Observability](#1-giới-thiệu-về-observability)
2. [Ba trụ cột của Observability](#2-ba-trụ-cột-của-observability)
3. [OpenTelemetry là gì?](#3-opentelemetry-là-gì)
4. [Cài đặt OpenTelemetry trong .NET](#4-cài-đặt-opentelemetry-trong-net)
5. [Distributed Tracing - Traces và Spans](#5-distributed-tracing---traces-và-spans)
6. [Context Propagation và Baggage](#6-context-propagation-và-baggage)
7. [Custom Metrics với OpenTelemetry](#7-custom-metrics-với-opentelemetry)
8. [Structured Logging với Serilog + OpenTelemetry](#8-structured-logging-với-serilog--opentelemetry)
9. [Ứng dụng mẫu: Hệ thống đặt hàng đa dịch vụ](#9-ứng-dụng-mẫu-hệ-thống-đặt-hàng-đa-dịch-vụ)
10. [Export sang Jaeger, Zipkin, Prometheus, Grafana](#10-export-sang-jaeger-zipkin-prometheus-grafana)
11. [Docker Compose - Hạ tầng quan sát](#11-docker-compose---hạ-tầng-quan-sát)
12. [Alerting và Monitoring](#12-alerting-và-monitoring)
13. [Best Practices](#13-best-practices)

---

## 1. Giới thiệu về Observability

### Observability là gì?

**Observability** (khả năng quan sát) là khả năng hiểu được trạng thái bên trong của một hệ thống dựa trên các đầu ra bên ngoài của nó. Trong bối cảnh phần mềm, đặc biệt là các hệ thống phân tán (microservices), observability giúp chúng ta trả lời câu hỏi:

> *"Tại sao hệ thống lại hoạt động như vậy?"* — không chỉ là *"Hệ thống có đang hoạt động không?"*

### Tại sao Observability quan trọng?

Trong kiến trúc microservices hiện đại, một request của người dùng có thể đi qua hàng chục service khác nhau. Khi có sự cố, việc tìm ra **root cause** (nguyên nhân gốc rễ) trở nên cực kỳ khó khăn nếu thiếu công cụ quan sát phù hợp.

```
Người dùng → API Gateway → Auth Service → Order Service → Product Service
                                                        ↓
                                               Payment Service → Bank API
                                                        ↓
                                              Notification Service → Email/SMS
```

Khi có lỗi xảy ra, bạn cần biết:
- Request đã đi qua những service nào?
- Mỗi service mất bao lâu để xử lý?
- Service nào trả về lỗi?
- Dữ liệu nào được truyền giữa các service?

---

## 2. Ba trụ cột của Observability

```
┌─────────────────────────────────────────────────────────────┐
│                    OBSERVABILITY PILLARS                     │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │    TRACES    │  │   METRICS    │  │      LOGS        │  │
│  │              │  │              │  │                  │  │
│  │ Theo dõi     │  │ Đo lường     │  │ Ghi lại sự      │  │
│  │ luồng chạy  │  │ hiệu suất    │  │ kiện chi tiết   │  │
│  │ của request │  │ theo thời    │  │                  │  │
│  │ qua nhiều   │  │ gian thực    │  │                  │  │
│  │ service     │  │              │  │                  │  │
│  │             │  │              │  │                  │  │
│  │ "Điều gì    │  │ "Hệ thống    │  │ "Chuyện gì đã   │  │
│  │  đã xảy    │  │  đang hoạt   │  │  xảy ra lúc     │  │
│  │  ra?"       │  │  động thế   │  │  đó?"           │  │
│  │             │  │  nào?"       │  │                  │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### 2.1 Traces (Phân tích luồng)

**Trace** là bản ghi toàn bộ hành trình của một request xuyên suốt hệ thống. Mỗi trace bao gồm nhiều **span** — mỗi span đại diện cho một đơn vị công việc cụ thể.

```
Trace ID: abc-123-xyz
│
├── Span: API Gateway (150ms)
│   ├── Span: Auth validation (20ms)
│   └── Span: Route to OrderService (5ms)
│
├── Span: OrderService.CreateOrder (100ms)
│   ├── Span: DB.Insert orders (30ms)
│   ├── Span: gRPC.GetProduct (40ms) ──→ ProductService
│   └── Span: Publish OrderCreated event (10ms)
│
└── Span: WorkerService.ProcessOrder (200ms)
    ├── Span: PaymentService.Charge (150ms)
    └── Span: NotificationService.Send (30ms)
```

### 2.2 Metrics (Chỉ số đo lường)

**Metrics** là các số liệu định lượng được thu thập theo thời gian, giúp bạn theo dõi xu hướng và phát hiện bất thường.

Các loại metric chính:
- **Counter**: Bộ đếm chỉ tăng (số request, số lỗi)
- **Gauge**: Giá trị có thể tăng/giảm (số kết nối hiện tại, memory usage)
- **Histogram**: Phân phối giá trị (latency, request size)

```
Ví dụ Metrics trong hệ thống đặt hàng:
- orders.total.count          → Tổng số đơn hàng (Counter)
- orders.revenue.total        → Tổng doanh thu (Counter)
- orders.active.gauge         → Đơn hàng đang xử lý (Gauge)
- http.request.duration       → Thời gian xử lý request (Histogram)
- db.connections.active       → Kết nối DB đang dùng (Gauge)
```

### 2.3 Logs (Nhật ký)

**Logs** là bản ghi văn bản về các sự kiện xảy ra trong hệ thống. Khi kết hợp với traces, logs trở nên cực kỳ mạnh mẽ vì bạn có thể liên kết log entry với trace ID cụ thể.

```
[2024-01-15 10:30:45.123] [INFO] [TraceId: abc-123] [SpanId: def-456]
Order created successfully | OrderId=ORD-789 | CustomerId=CUST-001 | Amount=150.00

[2024-01-15 10:30:45.456] [ERROR] [TraceId: abc-123] [SpanId: ghi-789]
Payment processing failed | OrderId=ORD-789 | Error=Insufficient funds
```

---

## 3. OpenTelemetry là gì?

**OpenTelemetry (OTel)** là một dự án mã nguồn mở, một chuẩn mực công nghiệp cho việc thu thập telemetry data (traces, metrics, logs). Nó được hỗ trợ bởi CNCF (Cloud Native Computing Foundation) và hầu hết các nhà cung cấp cloud lớn.

### Kiến trúc OpenTelemetry

```
┌────────────────────────────────────────────────────────────────────┐
│                        YOUR APPLICATION                             │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                   OpenTelemetry SDK                           │  │
│  │                                                               │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌────────────────────┐  │  │
│  │  │ TracerProvider│ │MeterProvider│  │  LoggerProvider    │  │  │
│  │  │              │  │             │  │                    │  │  │
│  │  │ ActivitySource│ │   Meter     │  │  ILogger + OTel    │  │  │
│  │  └──────┬───────┘  └──────┬──────┘  └─────────┬──────────┘  │  │
│  └─────────┼──────────────────┼─────────────────────┼────────────┘  │
│            │                  │                      │               │
└────────────┼──────────────────┼──────────────────────┼───────────────┘
             │                  │                      │
             ▼                  ▼                      ▼
┌────────────────────────────────────────────────────────────────────┐
│                    OTel Collector (optional)                         │
│                                                                      │
│   Receive → Process → Export                                         │
└──────────────┬─────────────────────────────────────────────────────┘
               │
       ┌───────┼──────────┐
       ▼       ▼          ▼
   Jaeger  Prometheus  Grafana  Zipkin  DataDog  NewRelic  ...
```

### Các khái niệm cốt lõi

| Khái niệm | Giải thích |
|-----------|-----------|
| **TracerProvider** | Factory tạo ra các Tracer, được cấu hình một lần |
| **Tracer / ActivitySource** | Dùng để tạo Span mới |
| **Span / Activity** | Đơn vị công việc trong một trace |
| **SpanContext** | Thông tin nhận dạng (TraceId, SpanId, TraceFlags) |
| **Baggage** | Dữ liệu key-value được truyền theo request |
| **MeterProvider** | Factory tạo Meter để đo lường |
| **Meter** | Dùng để tạo các instrument (Counter, Gauge, Histogram) |
| **Exporter** | Gửi dữ liệu đến backend (Jaeger, Prometheus, v.v.) |

---

## 4. Cài đặt OpenTelemetry trong .NET

### NuGet Packages cần thiết

```xml
<!-- File: Directory.Packages.props hoặc từng .csproj -->

<!-- Core OpenTelemetry -->
<PackageReference Include="OpenTelemetry" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />

<!-- Instrumentation tự động cho ASP.NET Core -->
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />

<!-- Instrumentation tự động cho HTTP Client -->
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.9.0" />

<!-- Instrumentation cho Entity Framework -->
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.0.0-beta.12" />

<!-- Instrumentation cho gRPC -->
<PackageReference Include="OpenTelemetry.Instrumentation.GrpcNetClient" Version="1.9.0" />

<!-- Exporters -->
<PackageReference Include="OpenTelemetry.Exporter.Jaeger" Version="1.5.1" />
<PackageReference Include="OpenTelemetry.Exporter.Zipkin" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.9.0-beta.2" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.9.0" />

<!-- Serilog integration -->
<PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
<PackageReference Include="Serilog.Sinks.OpenTelemetry" Version="4.0.0" />
<PackageReference Include="Serilog.Enrichers.Span" Version="3.1.0" />
```

### Cấu hình cơ bản trong Program.cs

```csharp
// File: src/ApiGateway/Program.cs

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog Configuration ───────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithSpan()          // Thêm TraceId, SpanId vào mỗi log entry
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// ─── Resource Definition ─────────────────────────────────────────────────────
// Resource mô tả nguồn gốc của telemetry data
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(
        serviceName: "ApiGateway",
        serviceVersion: "1.0.0",
        serviceInstanceId: Environment.MachineName)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["team.name"] = "platform-team",
    });

// ─── OpenTelemetry Configuration ─────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    // Cấu hình Distributed Tracing
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        // Instrumentation tự động
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = ctx =>
                // Không trace health check endpoints
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/metrics");
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddGrpcClientInstrumentation()
        // Custom ActivitySources của chúng ta
        .AddSource(TelemetryConstants.ApiGatewaySource)
        .AddSource(TelemetryConstants.OrderSource)
        // Exporters
        .AddJaegerExporter(options =>
        {
            options.AgentHost = builder.Configuration["Jaeger:AgentHost"] ?? "localhost";
            options.AgentPort = int.Parse(builder.Configuration["Jaeger:AgentPort"] ?? "6831");
        })
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
        }))
    // Cấu hình Metrics
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()    // CPU, GC, ThreadPool metrics
        // Custom Meters của chúng ta
        .AddMeter(TelemetryConstants.OrderMeter)
        .AddMeter(TelemetryConstants.ProductMeter)
        // Export ra Prometheus endpoint
        .AddPrometheusExporter());

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddGrpcClient<ProductService.ProductServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["Services:ProductService"] ?? "http://localhost:5003");
});

var app = builder.Build();

// Prometheus scrape endpoint: /metrics
app.MapPrometheusScrapingEndpoint();
app.MapControllers();
app.Run();
```

---

## 5. Distributed Tracing - Traces và Spans

### 5.1 Hiểu về Activity và ActivitySource trong .NET

Trong .NET, OpenTelemetry sử dụng `System.Diagnostics.Activity` (API có sẵn từ .NET 5+) và `ActivitySource` để tạo spans. Đây là quyết định thiết kế quan trọng — .NET không cần thư viện bên thứ ba để tạo spans.

```csharp
// File: src/Shared/Telemetry/TelemetryConstants.cs

namespace Shared.Telemetry;

/// <summary>
/// Hằng số định nghĩa tên các ActivitySource và Meter.
/// Đây là "contract" giữa code tạo telemetry và code cấu hình OTel.
/// </summary>
public static class TelemetryConstants
{
    // ActivitySource names — phải khớp với AddSource() trong Program.cs
    public const string ApiGatewaySource = "OrderSystem.ApiGateway";
    public const string OrderSource = "OrderSystem.OrderService";
    public const string ProductSource = "OrderSystem.ProductService";
    public const string WorkerSource = "OrderSystem.WorkerService";

    // Meter names — phải khớp với AddMeter() trong Program.cs
    public const string OrderMeter = "OrderSystem.Orders";
    public const string ProductMeter = "OrderSystem.Products";

    // Baggage keys — dữ liệu được truyền theo request
    public static class BaggageKeys
    {
        public const string CustomerId = "customer.id";
        public const string TenantId = "tenant.id";
        public const string CorrelationId = "correlation.id";
        public const string UserTier = "user.tier";
    }

    // Span attribute names — chuẩn hóa tên attributes
    public static class SpanAttributes
    {
        public const string OrderId = "order.id";
        public const string OrderStatus = "order.status";
        public const string OrderAmount = "order.amount";
        public const string CustomerId = "customer.id";
        public const string ProductId = "product.id";
        public const string ProductCount = "product.count";
        public const string PaymentMethod = "payment.method";
        public const string DbOperation = "db.operation";
        public const string DbTable = "db.table";
        public const string QueueName = "messaging.destination";
    }
}
```

### 5.2 Tạo Custom Spans

```csharp
// File: src/OrderService/Services/OrderService.cs

using System.Diagnostics;
using OpenTelemetry.Trace;
using Shared.Telemetry;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    // ActivitySource là thread-safe và nên được tạo một lần (singleton)
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.OrderSource, "1.0.0");

    private readonly IOrderRepository _repository;
    private readonly IProductServiceClient _productClient;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository repository,
        IProductServiceClient productClient,
        IMessagePublisher publisher,
        ILogger<OrderService> logger)
    {
        _repository = repository;
        _productClient = productClient;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // Tạo span cha cho toàn bộ operation CreateOrder
        using var activity = _activitySource.StartActivity(
            name: "OrderService.CreateOrder",
            kind: ActivityKind.Server);

        if (activity is null)
        {
            // Nếu OTel không được cấu hình, vẫn tiếp tục bình thường
            return await CreateOrderInternalAsync(request, ct);
        }

        // Thêm attributes vào span — đây là metadata có thể search được trong Jaeger
        activity.SetTag(TelemetryConstants.SpanAttributes.CustomerId, request.CustomerId);
        activity.SetTag(TelemetryConstants.SpanAttributes.OrderAmount, request.TotalAmount);
        activity.SetTag("order.items.count", request.Items.Count);

        // Đọc Baggage từ context propagation (được set bởi API Gateway)
        var userTier = Baggage.GetBaggage(TelemetryConstants.BaggageKeys.UserTier);
        if (!string.IsNullOrEmpty(userTier))
        {
            activity.SetTag("user.tier", userTier);
        }

        try
        {
            _logger.LogInformation(
                "Creating order for customer {CustomerId} with {ItemCount} items",
                request.CustomerId, request.Items.Count);

            // Validate inventory — tạo child span
            var inventoryResult = await ValidateInventoryAsync(request.Items, ct);
            if (!inventoryResult.IsSuccess)
            {
                // Đánh dấu span là lỗi
                activity.SetStatus(ActivityStatusCode.Error, inventoryResult.ErrorMessage);
                activity.SetTag("error.type", "InsufficientInventory");
                return OrderResult.Failure(inventoryResult.ErrorMessage!);
            }

            // Lưu vào database — child span được tạo trong repository
            var order = await _repository.CreateAsync(request, ct);

            // Publish event — child span được tạo trong publisher
            await _publisher.PublishAsync(new OrderCreatedEvent(order.Id, order.CustomerId), ct);

            // Đánh dấu span thành công
            activity.SetStatus(ActivityStatusCode.Ok);
            activity.SetTag(TelemetryConstants.SpanAttributes.OrderId, order.Id.ToString());

            _logger.LogInformation(
                "Order {OrderId} created successfully for customer {CustomerId}",
                order.Id, request.CustomerId);

            return OrderResult.Success(order);
        }
        catch (Exception ex)
        {
            // Ghi lại exception vào span — sẽ hiển thị trong Jaeger
            activity.RecordException(ex);
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to create order for customer {CustomerId}", request.CustomerId);
            throw;
        }
    }

    private async Task<InventoryResult> ValidateInventoryAsync(
        IReadOnlyList<OrderItem> items,
        CancellationToken ct)
    {
        // Tạo child span cho validation
        using var activity = _activitySource.StartActivity("OrderService.ValidateInventory");
        activity?.SetTag("items.count", items.Count);

        var productIds = items.Select(i => i.ProductId).ToList();

        // Gọi ProductService qua gRPC — span này sẽ được tạo tự động
        // bởi OpenTelemetry.Instrumentation.GrpcNetClient
        var products = await _productClient.GetProductsAsync(productIds, ct);

        foreach (var item in items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);

            if (product is null)
            {
                activity?.SetTag("error.product_id", item.ProductId.ToString());
                return InventoryResult.Failure($"Product {item.ProductId} not found");
            }

            if (product.StockQuantity < item.Quantity)
            {
                activity?.SetTag("error.product_id", item.ProductId.ToString());
                activity?.SetTag("error.available", product.StockQuantity);
                activity?.SetTag("error.requested", item.Quantity);
                return InventoryResult.Failure(
                    $"Insufficient stock for product {item.ProductId}: " +
                    $"requested {item.Quantity}, available {product.StockQuantity}");
            }
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        return InventoryResult.Success();
    }

    private async Task<OrderResult> CreateOrderInternalAsync(
        CreateOrderRequest request, CancellationToken ct)
    {
        // Fallback khi OTel không được cấu hình
        var order = await _repository.CreateAsync(request, ct);
        return OrderResult.Success(order);
    }
}
```

### 5.3 Trace Flow qua nhiều Service

```
HTTP Request: POST /api/orders
│
│ TraceId: a1b2c3d4e5f6
│
├─[Span 1] ApiGateway: HTTP POST /api/orders          [0ms - 185ms]
│  Tags: http.method=POST, http.url=/api/orders
│  │
│  ├─[Span 2] ApiGateway.AuthMiddleware               [5ms - 25ms]
│  │  Tags: auth.method=JWT, user.id=USR-001
│  │
│  └─[Span 3] HTTP Client → OrderService              [30ms - 180ms]
│     Tags: http.url=http://order-svc/orders
│     │
│     └─[Span 4] OrderService: HTTP POST /orders      [35ms - 175ms]
│        Tags: customer.id=CUST-001, order.amount=150.00
│        │
│        ├─[Span 5] OrderService.ValidateInventory    [40ms - 85ms]
│        │  Tags: items.count=3
│        │  │
│        │  └─[Span 6] gRPC → ProductService          [45ms - 80ms]
│        │     Tags: rpc.system=grpc, rpc.method=GetProducts
│        │     │
│        │     └─[Span 7] ProductService.GetProducts  [48ms - 78ms]
│        │        Tags: product.count=3
│        │        │
│        │        └─[Span 8] DB Query: SELECT products [50ms - 75ms]
│        │           Tags: db.system=postgresql, db.operation=SELECT
│        │
│        ├─[Span 9] DB Insert: orders table           [90ms - 120ms]
│        │  Tags: db.system=postgresql, db.operation=INSERT
│        │
│        └─[Span 10] Publish: order.created           [125ms - 140ms]
│           Tags: messaging.system=rabbitmq
│           │
│           └─ (Async) WorkerService picks up message
│              │
│              └─[Span 11] WorkerService.ProcessOrder [500ms - 850ms]
│                 (Linked span — same TraceId, khác parent)
```

---

## 6. Context Propagation và Baggage

### 6.1 Context Propagation là gì?

**Context Propagation** là cơ chế truyền thông tin tracking (TraceId, SpanId) từ service này sang service khác. OTel sử dụng HTTP headers chuẩn để làm điều này:

- **W3C TraceContext** (chuẩn mới): `traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`
- **B3** (format cũ của Zipkin): `X-B3-TraceId`, `X-B3-SpanId`, `X-B3-Sampled`

### 6.2 Baggage - Truyền dữ liệu theo request

**Baggage** cho phép bạn đính kèm dữ liệu tùy ý vào một trace và dữ liệu này sẽ được tự động truyền sang tất cả downstream services.

```csharp
// File: src/ApiGateway/Middleware/BaggageMiddleware.cs

using OpenTelemetry;
using System.Diagnostics;

namespace ApiGateway.Middleware;

/// <summary>
/// Middleware này set Baggage items từ thông tin authentication,
/// để các downstream service có thể đọc mà không cần truyền tường minh.
/// </summary>
public class BaggageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BaggageMiddleware> _logger;

    public BaggageMiddleware(RequestDelegate next, ILogger<BaggageMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Lấy thông tin từ JWT token (đã được xác thực trước đó)
        var userId = context.User.FindFirst("sub")?.Value;
        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        var userTier = context.User.FindFirst("user_tier")?.Value ?? "standard";
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        // Set Baggage — tự động propagate đến tất cả downstream services
        Baggage.SetBaggage(TelemetryConstants.BaggageKeys.CustomerId, userId ?? "anonymous");
        Baggage.SetBaggage(TelemetryConstants.BaggageKeys.TenantId, tenantId ?? "default");
        Baggage.SetBaggage(TelemetryConstants.BaggageKeys.UserTier, userTier);
        Baggage.SetBaggage(TelemetryConstants.BaggageKeys.CorrelationId, correlationId);

        // Thêm vào span hiện tại để searchable trong Jaeger
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("user.id", userId);
            activity.SetTag("tenant.id", tenantId);
            activity.SetTag("user.tier", userTier);
            activity.SetTag("correlation.id", correlationId);
        }

        // Trả về correlation ID trong response header để client tracking
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        _logger.LogDebug(
            "Set baggage for request: CustomerId={CustomerId}, TenantId={TenantId}, Tier={UserTier}",
            userId, tenantId, userTier);

        await _next(context);
    }
}
```

```csharp
// File: src/OrderService/Services/OrderRepository.cs
// Đọc Baggage trong downstream service

using OpenTelemetry;

namespace OrderService.Services;

public class OrderRepository : IOrderRepository
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.OrderSource);

    private readonly AppDbContext _dbContext;

    public OrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("OrderRepository.Create");

        // Đọc Baggage được set bởi API Gateway
        var tenantId = Baggage.GetBaggage(TelemetryConstants.BaggageKeys.TenantId);
        var correlationId = Baggage.GetBaggage(TelemetryConstants.BaggageKeys.CorrelationId);

        activity?.SetTag(TelemetryConstants.SpanAttributes.DbOperation, "INSERT");
        activity?.SetTag(TelemetryConstants.SpanAttributes.DbTable, "orders");
        activity?.SetTag("tenant.id", tenantId);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            TenantId = tenantId ?? "default",
            CorrelationId = correlationId,
            TotalAmount = request.TotalAmount,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        activity?.SetTag(TelemetryConstants.SpanAttributes.OrderId, order.Id.ToString());
        return order;
    }
}
```

---

## 7. Custom Metrics với OpenTelemetry

### 7.1 Định nghĩa Custom Metrics

```csharp
// File: src/Shared/Telemetry/OrderMetrics.cs

using System.Diagnostics.Metrics;

namespace Shared.Telemetry;

/// <summary>
/// Tập trung định nghĩa tất cả custom metrics cho Order domain.
/// Sử dụng Meter API chuẩn của .NET — không phụ thuộc vào OTel library.
/// </summary>
public sealed class OrderMetrics : IDisposable
{
    private readonly Meter _meter;

    // Counter: Số đơn hàng được tạo (chỉ tăng)
    private readonly Counter<long> _ordersCreatedCounter;

    // Counter: Doanh thu tổng (chỉ tăng)
    private readonly Counter<double> _revenueCounter;

    // Counter: Số đơn hàng thất bại
    private readonly Counter<long> _ordersFailedCounter;

    // Histogram: Phân phối giá trị đơn hàng
    private readonly Histogram<double> _orderAmountHistogram;

    // Histogram: Thời gian xử lý đơn hàng (ms)
    private readonly Histogram<double> _orderProcessingDurationHistogram;

    // ObservableGauge: Số đơn hàng đang ở trạng thái pending
    // (không cần tạo thủ công, callback được gọi khi Prometheus scrape)
    private readonly ObservableGauge<int> _pendingOrdersGauge;

    private readonly IOrderRepository _repository;

    public OrderMetrics(IOrderRepository repository)
    {
        _repository = repository;
        _meter = new Meter(TelemetryConstants.OrderMeter, "1.0.0");

        _ordersCreatedCounter = _meter.CreateCounter<long>(
            name: "orders.created.total",
            unit: "{orders}",
            description: "Tổng số đơn hàng được tạo thành công");

        _revenueCounter = _meter.CreateCounter<double>(
            name: "orders.revenue.total",
            unit: "USD",
            description: "Tổng doanh thu từ các đơn hàng thành công");

        _ordersFailedCounter = _meter.CreateCounter<long>(
            name: "orders.failed.total",
            unit: "{orders}",
            description: "Tổng số đơn hàng thất bại");

        _orderAmountHistogram = _meter.CreateHistogram<double>(
            name: "orders.amount",
            unit: "USD",
            description: "Phân phối giá trị đơn hàng");

        _orderProcessingDurationHistogram = _meter.CreateHistogram<double>(
            name: "orders.processing.duration",
            unit: "ms",
            description: "Thời gian xử lý đơn hàng từ khi tạo đến khi hoàn thành");

        // Observable gauge tự động đọc giá trị từ DB khi cần
        _pendingOrdersGauge = _meter.CreateObservableGauge<int>(
            name: "orders.pending.count",
            observeValue: () => _repository.GetPendingOrderCountAsync().GetAwaiter().GetResult(),
            unit: "{orders}",
            description: "Số đơn hàng đang chờ xử lý");
    }

    /// <summary>
    /// Ghi nhận một đơn hàng được tạo thành công.
    /// </summary>
    public void RecordOrderCreated(Order order, string paymentMethod)
    {
        var tags = new TagList
        {
            { "payment.method", paymentMethod },
            { "customer.tier", order.CustomerTier },
            { "region", order.Region }
        };

        _ordersCreatedCounter.Add(1, tags);
        _revenueCounter.Add((double)order.TotalAmount, tags);
        _orderAmountHistogram.Record((double)order.TotalAmount, tags);
    }

    /// <summary>
    /// Ghi nhận một đơn hàng thất bại.
    /// </summary>
    public void RecordOrderFailed(string reason, string customerId)
    {
        var tags = new TagList
        {
            { "failure.reason", reason },
            { "customer.id", customerId }
        };

        _ordersFailedCounter.Add(1, tags);
    }

    /// <summary>
    /// Ghi nhận thời gian xử lý đơn hàng.
    /// </summary>
    public void RecordOrderProcessingDuration(double milliseconds, OrderStatus finalStatus)
    {
        var tags = new TagList
        {
            { "order.final_status", finalStatus.ToString() }
        };

        _orderProcessingDurationHistogram.Record(milliseconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}
```

### 7.2 Sử dụng Metrics trong Service

```csharp
// File: src/OrderService/Services/OrderProcessingService.cs

namespace OrderService.Services;

public class OrderProcessingService : IOrderProcessingService
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.OrderSource);

    private readonly IOrderRepository _repository;
    private readonly OrderMetrics _metrics;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        IOrderRepository repository,
        OrderMetrics metrics,
        IPaymentService paymentService,
        ILogger<OrderProcessingService> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _paymentService = paymentService;
        _logger = logger;
    }

    public async Task ProcessOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        using var activity = _activitySource.StartActivity("OrderProcessingService.ProcessOrder");
        activity?.SetTag(TelemetryConstants.SpanAttributes.OrderId, orderId.ToString());

        var order = await _repository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        try
        {
            // Xử lý thanh toán
            using (var paymentSpan = _activitySource.StartActivity("ProcessPayment"))
            {
                paymentSpan?.SetTag(TelemetryConstants.SpanAttributes.PaymentMethod,
                    order.PaymentMethod);
                paymentSpan?.SetTag(TelemetryConstants.SpanAttributes.OrderAmount,
                    (double)order.TotalAmount);

                var paymentResult = await _paymentService.ChargeAsync(
                    new ChargeRequest(order.CustomerId, order.TotalAmount, order.PaymentMethod), ct);

                if (!paymentResult.IsSuccess)
                {
                    paymentSpan?.SetStatus(ActivityStatusCode.Error, paymentResult.ErrorMessage);
                    await _repository.UpdateStatusAsync(orderId, OrderStatus.PaymentFailed, ct);

                    var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _metrics.RecordOrderProcessingDuration(duration, OrderStatus.PaymentFailed);
                    _metrics.RecordOrderFailed("PaymentFailed", order.CustomerId.ToString());

                    _logger.LogWarning(
                        "Payment failed for order {OrderId}: {Reason}",
                        orderId, paymentResult.ErrorMessage);
                    return;
                }

                paymentSpan?.SetTag("payment.transaction_id", paymentResult.TransactionId);
            }

            // Cập nhật trạng thái thành công
            await _repository.UpdateStatusAsync(orderId, OrderStatus.Confirmed, ct);

            var processingDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.RecordOrderCreated(order, order.PaymentMethod);
            _metrics.RecordOrderProcessingDuration(processingDuration, OrderStatus.Confirmed);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag(TelemetryConstants.SpanAttributes.OrderStatus,
                OrderStatus.Confirmed.ToString());

            _logger.LogInformation(
                "Order {OrderId} processed successfully in {Duration}ms",
                orderId, processingDuration);
        }
        catch (Exception ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.RecordOrderFailed("Exception", order.CustomerId.ToString());
            _metrics.RecordOrderProcessingDuration(duration, OrderStatus.Failed);

            activity?.RecordException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex,
                "Unhandled exception processing order {OrderId}", orderId);
            throw;
        }
    }
}
```

---

## 8. Structured Logging với Serilog + OpenTelemetry

### 8.1 Cấu hình Serilog với OTel Sink

```csharp
// File: src/Shared/Logging/SerilogConfiguration.cs

using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Sinks.OpenTelemetry;

namespace Shared.Logging;

public static class SerilogConfiguration
{
    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, services, config) =>
        {
            config
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                // Tự động thêm TraceId và SpanId vào mỗi log
                .Enrich.WithSpan(new SpanOptions
                {
                    IncludeOperationName = true,
                    IncludeTags = false  // Không expose tags vào logs vì lý do bảo mật
                })
                .Enrich.WithProperty("ServiceName", context.HostingEnvironment.ApplicationName)
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                // Console output với format đẹp cho development
                .WriteTo.Conditional(
                    condition: _ => context.HostingEnvironment.IsDevelopment(),
                    configureSink: sink => sink.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] " +
                                        "[{TraceId}:{SpanId}] " +
                                        "{SourceContext}: {Message:lj}{NewLine}{Exception}"))
                // JSON format cho production (dễ parse bởi log aggregation tools)
                .WriteTo.Conditional(
                    condition: _ => !context.HostingEnvironment.IsDevelopment(),
                    configureSink: sink => sink.Console(
                        new Serilog.Formatting.Json.JsonFormatter()))
                // Gửi logs đến OTel Collector qua OTLP
                .WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = context.Configuration["Otlp:Endpoint"]
                                       ?? "http://localhost:4317";
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = context.HostingEnvironment.ApplicationName,
                        ["service.version"] = "1.0.0",
                        ["deployment.environment"] = context.HostingEnvironment.EnvironmentName
                    };
                });
        });
    }
}
```

### 8.2 appsettings.json cho Serilog

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning",
        "Grpc": "Warning"
      }
    },
    "Filter": [
      {
        "Name": "ByExcluding",
        "Args": {
          "expression": "RequestPath like '/health%'"
        }
      }
    ]
  },
  "OpenTelemetry": {
    "ServiceName": "OrderService",
    "ServiceVersion": "1.0.0"
  },
  "Jaeger": {
    "AgentHost": "jaeger",
    "AgentPort": 6831
  },
  "Otlp": {
    "Endpoint": "http://otel-collector:4317"
  },
  "Services": {
    "ProductService": "http://product-service:5003",
    "PaymentService": "http://payment-service:5004"
  }
}
```

---

## 9. Ứng dụng mẫu: Hệ thống đặt hàng đa dịch vụ

### Kiến trúc tổng thể

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CLIENT APPLICATIONS                              │
│                    (Web Browser / Mobile App)                            │
└────────────────────────────┬────────────────────────────────────────────┘
                             │ HTTPS
                             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         API GATEWAY (:5001)                              │
│   - Rate Limiting          - JWT Authentication                          │
│   - Request Routing        - Baggage Injection                           │
│   - Load Balancing         - OTel Instrumentation                        │
└────────────┬──────────────────────────┬────────────────────────────────┘
             │ HTTP                     │ HTTP
             ▼                          ▼
┌────────────────────────┐  ┌──────────────────────────────────────────┐
│   ORDER SERVICE (:5002) │  │          PRODUCT SERVICE (:5003)         │
│                         │  │              (gRPC + REST)               │
│  - Create Order         │  │  - Get Products                          │
│  - Get Order Status     │  │  - Update Stock                          │
│  - OTel Instrumentation │  │  - OTel Instrumentation                  │
└────────────┬────────────┘  └──────────────────────────────────────────┘
             │ RabbitMQ
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                       WORKER SERVICE (Background)                        │
│   - Consume order.created events                                         │
│   - Process payments                                                     │
│   - Send notifications                                                   │
│   - Link spans với original trace                                        │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                      OBSERVABILITY STACK                                 │
│                                                                          │
│  ┌──────────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │
│  │  Jaeger (:16686) │  │ Prometheus   │  │    Grafana (:3000)        │  │
│  │  Distributed     │  │ (:9090)      │  │    Dashboards &           │  │
│  │  Tracing UI      │  │ Metrics DB   │  │    Alerting               │  │
│  └──────────────────┘  └──────────────┘  └──────────────────────────┘  │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │              OTel Collector (:4317 gRPC, :4318 HTTP)             │   │
│  │  Receives → Processes → Exports to multiple backends             │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 9.1 API Gateway Service

```csharp
// File: src/ApiGateway/Program.cs

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Shared.Telemetry;
using Shared.Logging;
using ApiGateway.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureSerilog();

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("ApiGateway", serviceVersion: "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName
    });

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
        .AddHttpClientInstrumentation(opt => opt.RecordException = true)
        .AddSource(TelemetryConstants.ApiGatewaySource)
        .AddOtlpExporter(opt =>
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    });

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHttpClient("OrderService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:OrderService"]!);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Inject baggage từ JWT claims vào distributed trace
app.UseMiddleware<BaggageMiddleware>();

app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
```

```csharp
// File: src/ApiGateway/Controllers/OrdersController.cs

using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Telemetry;

namespace ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.ApiGatewaySource);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IHttpClientFactory httpClientFactory,
        ILogger<OrdersController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("ApiGateway.CreateOrder");

        var customerId = User.FindFirst("sub")?.Value;
        activity?.SetTag("customer.id", customerId);
        activity?.SetTag("order.items.count", request.Items.Count);

        _logger.LogInformation(
            "Routing CreateOrder request for customer {CustomerId} with {ItemCount} items",
            customerId, request.Items.Count);

        var client = _httpClientFactory.CreateClient("OrderService");

        // OTel tự động inject traceparent header vào HTTP request
        // nhờ AddHttpClientInstrumentation()
        var response = await client.PostAsJsonAsync("/api/orders", request with
        {
            CustomerId = Guid.Parse(customerId!)
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            activity?.SetStatus(ActivityStatusCode.Error, error);
            _logger.LogWarning(
                "OrderService returned {StatusCode} for customer {CustomerId}",
                response.StatusCode, customerId);
            return StatusCode((int)response.StatusCode, error);
        }

        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(ct);
        activity?.SetTag("order.id", result?.OrderId.ToString());
        activity?.SetStatus(ActivityStatusCode.Ok);

        return Ok(result);
    }

    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("ApiGateway.GetOrder");
        activity?.SetTag("order.id", orderId.ToString());

        var client = _httpClientFactory.CreateClient("OrderService");
        var response = await client.GetAsync($"/api/orders/{orderId}", ct);

        return response.IsSuccessStatusCode
            ? Ok(await response.Content.ReadFromJsonAsync<OrderResponse>(ct))
            : StatusCode((int)response.StatusCode);
    }
}
```

### 9.2 Order Service

```csharp
// File: src/OrderService/Program.cs

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.EntityFrameworkCore;
using Shared.Telemetry;
using Shared.Logging;
using OrderService.Data;
using OrderService.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureSerilog();

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("OrderService", serviceVersion: "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["db.system"] = "postgresql"
    });

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
        .AddHttpClientInstrumentation(opt => opt.RecordException = true)
        .AddGrpcClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(opt =>
        {
            opt.SetDbStatementForText = true;
            opt.SetDbStatementForStoredProcedure = true;
        })
        .AddSource(TelemetryConstants.OrderSource)
        .AddOtlpExporter(opt =>
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(TelemetryConstants.OrderMeter)
        .AddPrometheusExporter());

// Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// gRPC Client cho ProductService
builder.Services.AddGrpcClient<ProductGrpc.ProductGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["Services:ProductService"]!);
}).AddStandardResilienceHandler();   // Retry, Circuit Breaker tự động

// Application Services
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, Services.OrderService>();
builder.Services.AddScoped<IOrderProcessingService, OrderProcessingService>();
builder.Services.AddSingleton<OrderMetrics>();

// RabbitMQ / Message Bus
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>()
    .AddRabbitMQ();

var app = builder.Build();

app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHealthChecks("/health");
app.MapControllers();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
```

```csharp
// File: src/OrderService/Controllers/OrdersController.cs

using Microsoft.AspNetCore.Mvc;
using Shared.Telemetry;
using System.Diagnostics;
using OrderService.Services;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.OrderSource);

    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        // Span này được tạo bởi ASP.NET Core instrumentation tự động
        // Chúng ta chỉ cần thêm business context vào span hiện tại
        var currentActivity = Activity.Current;
        currentActivity?.SetTag(TelemetryConstants.SpanAttributes.CustomerId,
            request.CustomerId.ToString());

        var result = await _orderService.CreateOrderAsync(request, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Order creation failed: {Reason}", result.ErrorMessage);
            return BadRequest(new { error = result.ErrorMessage });
        }

        _logger.LogInformation(
            "Order {OrderId} created via API for customer {CustomerId}",
            result.Order!.Id, request.CustomerId);

        return CreatedAtAction(
            nameof(GetOrder),
            new { orderId = result.Order!.Id },
            new OrderResponse(result.Order));
    }

    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken ct)
    {
        Activity.Current?.SetTag(TelemetryConstants.SpanAttributes.OrderId, orderId.ToString());

        var order = await _orderService.GetByIdAsync(orderId, ct);
        return order is null ? NotFound() : Ok(new OrderResponse(order));
    }
}
```

### 9.3 Product Service (gRPC)

```csharp
// File: src/ProductService/Program.cs

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Shared.Telemetry;
using Shared.Logging;
using ProductService.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureSerilog();

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("ProductService", serviceVersion: "1.0.0");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
        .AddGrpcCoreInstrumentation()           // Server-side gRPC instrumentation
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource(TelemetryConstants.ProductSource)
        .AddOtlpExporter(opt =>
            opt.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(TelemetryConstants.ProductMeter)
        .AddPrometheusExporter());

builder.Services.AddGrpc();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddSingleton<ProductMetrics>();

var app = builder.Build();

app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHealthChecks("/health");
app.MapGrpcService<ProductGrpcService>();   // gRPC service
app.MapControllers();                        // REST fallback

app.Run();
```

```csharp
// File: src/ProductService/Services/ProductGrpcService.cs
// Proto file được generate từ product.proto

using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProductService.Protos;
using Shared.Telemetry;

namespace ProductService.Services;

public class ProductGrpcService : ProductGrpc.ProductGrpcBase
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.ProductSource);

    private readonly IProductRepository _repository;
    private readonly ProductMetrics _metrics;
    private readonly ILogger<ProductGrpcService> _logger;

    public ProductGrpcService(
        IProductRepository repository,
        ProductMetrics metrics,
        ILogger<ProductGrpcService> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public override async Task<GetProductsResponse> GetProducts(
        GetProductsRequest request,
        ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("ProductService.GetProducts");

        var productIds = request.ProductIds.Select(Guid.Parse).ToList();
        activity?.SetTag(TelemetryConstants.SpanAttributes.ProductCount, productIds.Count);

        _logger.LogDebug("GetProducts called for {Count} products", productIds.Count);

        var products = await _repository.GetByIdsAsync(productIds, context.CancellationToken);

        _metrics.RecordProductsQueried(productIds.Count);

        var response = new GetProductsResponse();
        response.Products.AddRange(products.Select(p => new ProductDto
        {
            Id = p.Id.ToString(),
            Name = p.Name,
            Price = (double)p.Price,
            StockQuantity = p.StockQuantity,
            Category = p.Category
        }));

        activity?.SetTag("products.found.count", response.Products.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return response;
    }

    public override async Task<UpdateStockResponse> UpdateStock(
        UpdateStockRequest request,
        ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("ProductService.UpdateStock");

        var productId = Guid.Parse(request.ProductId);
        activity?.SetTag(TelemetryConstants.SpanAttributes.ProductId, request.ProductId);
        activity?.SetTag("stock.delta", request.QuantityDelta);

        _logger.LogInformation(
            "Updating stock for product {ProductId} by {Delta}",
            productId, request.QuantityDelta);

        var success = await _repository.UpdateStockAsync(
            productId, request.QuantityDelta, context.CancellationToken);

        if (!success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Stock update failed");
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Cannot update stock for product {request.ProductId}"));
        }

        _metrics.RecordStockUpdate(productId.ToString(), request.QuantityDelta);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return new UpdateStockResponse { Success = true };
    }
}
```

### 9.4 Worker Service (Background Processing)

```csharp
// File: src/WorkerService/Program.cs

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Shared.Telemetry;
using Shared.Logging;
using WorkerService.Workers;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();  // Sẽ dùng Serilog
builder.Services.AddSerilog();

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("WorkerService", serviceVersion: "1.0.0");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddSource(TelemetryConstants.WorkerSource)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opt =>
            opt.Endpoint = new Uri(
                builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddRuntimeInstrumentation()
        .AddMeter(TelemetryConstants.OrderMeter)
        .AddPrometheusExporter());

builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddHttpClient<IPaymentServiceClient, PaymentServiceClient>();
builder.Services.AddHttpClient<INotificationServiceClient, NotificationServiceClient>();
builder.Services.AddHostedService<OrderProcessingWorker>();

var host = builder.Build();
host.Run();
```

```csharp
// File: src/WorkerService/Workers/OrderProcessingWorker.cs
// Minh họa cách propagate trace context qua message queue

using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Telemetry;

namespace WorkerService.Workers;

/// <summary>
/// Worker xử lý các order events từ RabbitMQ.
/// Phần quan trọng nhất là cách extract trace context từ message headers
/// để tạo linked span, duy trì distributed trace.
/// </summary>
public class OrderProcessingWorker : BackgroundService
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.WorkerSource);

    // Propagator chuẩn của OTel để extract/inject context
    private static readonly TextMapPropagator _propagator =
        Propagators.DefaultTextMapPropagator;

    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly INotificationServiceClient _notificationClient;
    private readonly OrderMetrics _metrics;
    private readonly ILogger<OrderProcessingWorker> _logger;

    public OrderProcessingWorker(
        IConnectionFactory connectionFactory,
        IPaymentServiceClient paymentClient,
        INotificationServiceClient notificationClient,
        OrderMetrics metrics,
        ILogger<OrderProcessingWorker> logger)
    {
        _paymentClient = paymentClient;
        _notificationClient = notificationClient;
        _metrics = metrics;
        _logger = logger;

        _connection = connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(
            queue: "order.created",
            durable: true,
            exclusive: false,
            autoDelete: false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, eventArgs) =>
        {
            await ProcessMessageAsync(eventArgs, stoppingToken);
        };

        _channel.BasicConsume(
            queue: "order.created",
            autoAck: false,
            consumer: consumer);

        return Task.CompletedTask;
    }

    private async Task ProcessMessageAsync(
        BasicDeliverEventArgs eventArgs,
        CancellationToken ct)
    {
        // Bước 1: Extract trace context từ message headers
        // Đây là cách quan trọng để link worker span với span của OrderService
        var parentContext = _propagator.Extract(
            default,
            eventArgs.BasicProperties,
            ExtractTraceContextFromMessageHeaders);

        // Bước 2: Tạo span mới với parent context được extract từ message
        // Điều này tạo ra "linked span" — cùng TraceId, nhưng có thể hiển thị
        // là span con hoặc span liên kết trong Jaeger
        using var activity = _activitySource.StartActivity(
            name: "WorkerService.ProcessOrder",
            kind: ActivityKind.Consumer,
            parentContext: parentContext.ActivityContext);

        var message = JsonSerializer.Deserialize<OrderCreatedEvent>(eventArgs.Body.Span);
        if (message is null)
        {
            _logger.LogWarning("Received null or invalid message");
            _channel.BasicNack(eventArgs.DeliveryTag, false, false);
            return;
        }

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", "order.created");
        activity?.SetTag("messaging.message_id", eventArgs.BasicProperties.MessageId);
        activity?.SetTag(TelemetryConstants.SpanAttributes.OrderId, message.OrderId.ToString());
        activity?.SetTag(TelemetryConstants.SpanAttributes.CustomerId, message.CustomerId.ToString());

        // Propagate Baggage từ message headers nếu có
        Baggage.Current = parentContext.Baggage;

        _logger.LogInformation(
            "Processing order {OrderId} from message queue",
            message.OrderId);

        try
        {
            var startTime = DateTime.UtcNow;

            // Xử lý thanh toán
            using (var paymentSpan = _activitySource.StartActivity("ProcessPayment"))
            {
                paymentSpan?.SetTag("payment.order_id", message.OrderId.ToString());

                var paymentResult = await _paymentClient.ProcessPaymentAsync(
                    new ProcessPaymentRequest(message.OrderId, message.Amount), ct);

                if (!paymentResult.IsSuccess)
                {
                    paymentSpan?.SetStatus(ActivityStatusCode.Error, paymentResult.Error);
                    _logger.LogError(
                        "Payment failed for order {OrderId}: {Error}",
                        message.OrderId, paymentResult.Error);

                    _metrics.RecordOrderFailed("PaymentFailed", message.CustomerId.ToString());
                    _channel.BasicNack(eventArgs.DeliveryTag, false, true); // Requeue
                    return;
                }

                paymentSpan?.SetTag("payment.transaction_id", paymentResult.TransactionId);
                paymentSpan?.SetStatus(ActivityStatusCode.Ok);
            }

            // Gửi thông báo
            using (var notifSpan = _activitySource.StartActivity("SendNotification"))
            {
                notifSpan?.SetTag("notification.channel", "email");

                await _notificationClient.SendOrderConfirmationAsync(
                    new SendNotificationRequest(
                        message.CustomerId,
                        message.OrderId,
                        message.CustomerEmail), ct);

                notifSpan?.SetStatus(ActivityStatusCode.Ok);
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.RecordOrderProcessingDuration(duration, OrderStatus.Confirmed);

            activity?.SetStatus(ActivityStatusCode.Ok);
            _channel.BasicAck(eventArgs.DeliveryTag, false);

            _logger.LogInformation(
                "Order {OrderId} processed successfully in {Duration}ms",
                message.OrderId, duration);
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to process order {OrderId}", message.OrderId);
            _metrics.RecordOrderFailed("ProcessingException", message.CustomerId.ToString());

            // Nack và không requeue để tránh infinite loop
            _channel.BasicNack(eventArgs.DeliveryTag, false, false);
        }
    }

    /// <summary>
    /// Hàm helper để đọc trace context headers từ RabbitMQ message properties.
    /// </summary>
    private static IEnumerable<string> ExtractTraceContextFromMessageHeaders(
        IBasicProperties properties,
        string key)
    {
        if (properties.Headers != null &&
            properties.Headers.TryGetValue(key, out var value))
        {
            return new[] { System.Text.Encoding.UTF8.GetString((byte[])value) };
        }
        return Enumerable.Empty<string>();
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
```

```csharp
// File: src/OrderService/Infrastructure/RabbitMqPublisher.cs
// Cách inject trace context vào message headers khi publish

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using System.Text.Json;
using Shared.Telemetry;

namespace OrderService.Infrastructure;

public class RabbitMqPublisher : IMessagePublisher
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryConstants.OrderSource);

    private static readonly TextMapPropagator _propagator =
        Propagators.DefaultTextMapPropagator;

    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConnectionFactory factory, ILogger<RabbitMqPublisher> logger)
    {
        _connection = factory.CreateConnection();
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class
    {
        var queueName = GetQueueName<T>();

        using var activity = _activitySource.StartActivity(
            name: $"Publish {queueName}",
            kind: ActivityKind.Producer);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", queueName);
        activity?.SetTag("messaging.destination_kind", "queue");

        using var channel = _connection.CreateModel();

        var props = channel.CreateBasicProperties();
        props.MessageId = Guid.NewGuid().ToString();
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        props.Headers = new Dictionary<string, object>();
        props.DeliveryMode = 2; // Persistent

        // Inject trace context vào message headers
        // Worker service sẽ extract headers này để link spans
        _propagator.Inject(
            new PropagationContext(Activity.Current?.Context ?? default, Baggage.Current),
            props,
            InjectTraceContextIntoMessageHeaders);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        channel.BasicPublish("", queueName, props, body);

        activity?.SetTag("messaging.message_id", props.MessageId);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogDebug(
            "Published {MessageType} to {Queue} with MessageId {MessageId}",
            typeof(T).Name, queueName, props.MessageId);

        await Task.CompletedTask;
    }

    private static void InjectTraceContextIntoMessageHeaders(
        IBasicProperties properties,
        string key,
        string value)
    {
        properties.Headers ??= new Dictionary<string, object>();
        properties.Headers[key] = System.Text.Encoding.UTF8.GetBytes(value);
    }

    private static string GetQueueName<T>() => typeof(T).Name switch
    {
        nameof(OrderCreatedEvent) => "order.created",
        nameof(OrderCancelledEvent) => "order.cancelled",
        nameof(OrderShippedEvent) => "order.shipped",
        _ => throw new ArgumentException($"Unknown event type: {typeof(T).Name}")
    };
}
```

---

## 10. Export sang Jaeger, Zipkin, Prometheus, Grafana

### 10.1 OTel Collector Configuration

OTel Collector là một proxy trung gian cho phép bạn nhận telemetry data từ nhiều nguồn, xử lý và gửi đến nhiều backend khác nhau — không cần thay đổi code ứng dụng.

```yaml
# File: otel-collector/otel-collector-config.yaml

receivers:
  # Nhận data từ applications qua gRPC
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

  # Nhận Prometheus metrics để forward
  prometheus:
    config:
      scrape_configs:
        - job_name: 'otel-collector'
          scrape_interval: 10s
          static_configs:
            - targets: ['localhost:8888']

processors:
  # Gộp batch để giảm số lần export
  batch:
    timeout: 1s
    send_batch_size: 1024
    send_batch_max_size: 2048

  # Thêm resource attributes
  resource:
    attributes:
      - action: insert
        key: cluster.name
        value: "production"

  # Lọc spans không cần thiết (health checks, metrics endpoint)
  filter/drop_health:
    spans:
      exclude:
        match_type: regexp
        attributes:
          - key: http.target
            value: "^(/health|/metrics|/ready).*"

  # Memory limiter để tránh OOM
  memory_limiter:
    limit_mib: 512
    spike_limit_mib: 128
    check_interval: 5s

exporters:
  # Export traces sang Jaeger
  jaeger:
    endpoint: jaeger:14250
    tls:
      insecure: true

  # Export traces sang Zipkin
  zipkin:
    endpoint: "http://zipkin:9411/api/v2/spans"

  # Export metrics sang Prometheus
  prometheusremotewrite:
    endpoint: "http://prometheus:9090/api/v1/write"
    tls:
      insecure: true

  # Debug output (chỉ dùng trong development)
  logging:
    verbosity: detailed

  # Export sang Grafana Cloud (production)
  otlp/grafana:
    endpoint: "https://tempo-prod.grafana.net:443"
    headers:
      authorization: "Basic ${GRAFANA_CLOUD_TOKEN}"

extensions:
  health_check:
    endpoint: 0.0.0.0:13133
  pprof:
    endpoint: 0.0.0.0:1777
  zpages:
    endpoint: 0.0.0.0:55679

service:
  extensions: [health_check, pprof, zpages]
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, filter/drop_health, resource, batch]
      exporters: [jaeger, zipkin]

    metrics:
      receivers: [otlp, prometheus]
      processors: [memory_limiter, resource, batch]
      exporters: [prometheusremotewrite]

    logs:
      receivers: [otlp]
      processors: [memory_limiter, resource, batch]
      exporters: [logging]
```

### 10.2 Prometheus Configuration

```yaml
# File: prometheus/prometheus.yml

global:
  scrape_interval: 15s
  evaluation_interval: 15s
  external_labels:
    cluster: 'order-system'
    environment: 'production'

# Alertmanager configuration
alerting:
  alertmanagers:
    - static_configs:
        - targets:
            - alertmanager:9093

# Rules files
rule_files:
  - "alerts/*.yml"

scrape_configs:
  # Scrape API Gateway metrics
  - job_name: 'api-gateway'
    static_configs:
      - targets: ['api-gateway:5001']
    metrics_path: '/metrics'
    scrape_interval: 10s

  # Scrape Order Service metrics
  - job_name: 'order-service'
    static_configs:
      - targets: ['order-service:5002']
    metrics_path: '/metrics'
    scrape_interval: 10s

  # Scrape Product Service metrics
  - job_name: 'product-service'
    static_configs:
      - targets: ['product-service:5003']
    metrics_path: '/metrics'
    scrape_interval: 10s

  # Scrape Worker Service metrics
  - job_name: 'worker-service'
    static_configs:
      - targets: ['worker-service:5005']
    metrics_path: '/metrics'
    scrape_interval: 10s

  # Scrape OTel Collector metrics
  - job_name: 'otel-collector'
    static_configs:
      - targets: ['otel-collector:8888']
    scrape_interval: 15s
```

### 10.3 Grafana Dashboard Configuration

```json
{
  "title": "Order System Overview",
  "uid": "order-system-overview",
  "tags": ["orders", "business", "slo"],
  "panels": [
    {
      "id": 1,
      "title": "Order Creation Rate",
      "type": "stat",
      "gridPos": { "x": 0, "y": 0, "w": 6, "h": 4 },
      "targets": [
        {
          "expr": "sum(rate(orders_created_total[5m])) * 60",
          "legendFormat": "Orders/min"
        }
      ],
      "options": {
        "reduceOptions": { "calcs": ["lastNotNull"] },
        "colorMode": "background",
        "thresholds": {
          "steps": [
            { "color": "green", "value": null },
            { "color": "yellow", "value": 50 },
            { "color": "red", "value": 100 }
          ]
        }
      }
    },
    {
      "id": 2,
      "title": "Revenue per Hour",
      "type": "stat",
      "gridPos": { "x": 6, "y": 0, "w": 6, "h": 4 },
      "targets": [
        {
          "expr": "sum(increase(orders_revenue_total_USD[1h]))",
          "legendFormat": "Revenue (USD)"
        }
      ]
    },
    {
      "id": 3,
      "title": "Order Success Rate",
      "type": "gauge",
      "gridPos": { "x": 12, "y": 0, "w": 6, "h": 4 },
      "targets": [
        {
          "expr": "sum(rate(orders_created_total[5m])) / (sum(rate(orders_created_total[5m])) + sum(rate(orders_failed_total[5m]))) * 100",
          "legendFormat": "Success Rate %"
        }
      ],
      "options": {
        "min": 0,
        "max": 100,
        "thresholds": {
          "steps": [
            { "color": "red", "value": null },
            { "color": "yellow", "value": 95 },
            { "color": "green", "value": 99 }
          ]
        }
      }
    },
    {
      "id": 4,
      "title": "HTTP Request Latency (p50, p95, p99)",
      "type": "timeseries",
      "gridPos": { "x": 0, "y": 4, "w": 24, "h": 8 },
      "targets": [
        {
          "expr": "histogram_quantile(0.50, sum(rate(http_server_request_duration_milliseconds_bucket[5m])) by (le, job))",
          "legendFormat": "p50 - {{job}}"
        },
        {
          "expr": "histogram_quantile(0.95, sum(rate(http_server_request_duration_milliseconds_bucket[5m])) by (le, job))",
          "legendFormat": "p95 - {{job}}"
        },
        {
          "expr": "histogram_quantile(0.99, sum(rate(http_server_request_duration_milliseconds_bucket[5m])) by (le, job))",
          "legendFormat": "p99 - {{job}}"
        }
      ]
    },
    {
      "id": 5,
      "title": "Pending Orders",
      "type": "timeseries",
      "gridPos": { "x": 0, "y": 12, "w": 12, "h": 6 },
      "targets": [
        {
          "expr": "orders_pending_count_orders",
          "legendFormat": "Pending Orders"
        }
      ]
    },
    {
      "id": 6,
      "title": "Order Processing Duration Distribution",
      "type": "heatmap",
      "gridPos": { "x": 12, "y": 12, "w": 12, "h": 6 },
      "targets": [
        {
          "expr": "sum(rate(orders_processing_duration_ms_bucket[5m])) by (le)",
          "format": "heatmap",
          "legendFormat": "{{le}}"
        }
      ]
    }
  ]
}
```

---

## 11. Docker Compose - Hạ tầng quan sát

```yaml
# File: docker-compose.yml

version: '3.9'

networks:
  order-network:
    driver: bridge

volumes:
  prometheus-data:
  grafana-data:
  jaeger-data:
  postgres-data:
  rabbitmq-data:

services:
  # ─── Application Services ─────────────────────────────────────────────
  api-gateway:
    build:
      context: .
      dockerfile: src/ApiGateway/Dockerfile
    ports:
      - "5001:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Otlp__Endpoint: http://otel-collector:4317
      Services__OrderService: http://order-service:8080
      Services__ProductService: http://product-service:8080
      Auth__Authority: http://identity-server:5000
    depends_on:
      otel-collector:
        condition: service_healthy
    networks:
      - order-network

  order-service:
    build:
      context: .
      dockerfile: src/OrderService/Dockerfile
    ports:
      - "5002:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=orders;Username=app;Password=secret"
      Otlp__Endpoint: http://otel-collector:4317
      Services__ProductService: http://product-service:8080
      RabbitMQ__Host: rabbitmq
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      otel-collector:
        condition: service_healthy
    networks:
      - order-network

  product-service:
    build:
      context: .
      dockerfile: src/ProductService/Dockerfile
    ports:
      - "5003:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=products;Username=app;Password=secret"
      Otlp__Endpoint: http://otel-collector:4317
    depends_on:
      postgres:
        condition: service_healthy
      otel-collector:
        condition: service_healthy
    networks:
      - order-network

  worker-service:
    build:
      context: .
      dockerfile: src/WorkerService/Dockerfile
    environment:
      DOTNET_ENVIRONMENT: Production
      Otlp__Endpoint: http://otel-collector:4317
      RabbitMQ__Host: rabbitmq
      Services__PaymentService: http://payment-service:8080
    depends_on:
      rabbitmq:
        condition: service_healthy
      otel-collector:
        condition: service_healthy
    networks:
      - order-network

  # ─── Infrastructure ────────────────────────────────────────────────────
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: app
      POSTGRES_PASSWORD: secret
      POSTGRES_MULTIPLE_DATABASES: orders,products
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app"]
      interval: 5s
      timeout: 5s
      retries: 5
    networks:
      - order-network

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: secret
    ports:
      - "15672:15672"   # Management UI
    volumes:
      - rabbitmq-data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - order-network

  # ─── Observability Stack ───────────────────────────────────────────────
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.105.0
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector/otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317"     # OTLP gRPC
      - "4318:4318"     # OTLP HTTP
      - "8888:8888"     # Collector metrics
      - "55679:55679"   # zPages
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:13133/"]
      interval: 5s
      timeout: 5s
      retries: 3
    depends_on:
      - jaeger
      - prometheus
    networks:
      - order-network

  jaeger:
    image: jaegertracing/all-in-one:1.60
    environment:
      COLLECTOR_OTLP_ENABLED: "true"
      SPAN_STORAGE_TYPE: badger
      BADGER_EPHEMERAL: "false"
      BADGER_DIRECTORY_VALUE: /badger/data
      BADGER_DIRECTORY_KEY: /badger/key
    ports:
      - "16686:16686"   # Jaeger UI
      - "14250:14250"   # gRPC
      - "9411:9411"     # Zipkin-compatible endpoint
    volumes:
      - jaeger-data:/badger
    networks:
      - order-network

  prometheus:
    image: prom/prometheus:v2.54.0
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--web.enable-remote-write-receiver'
      - '--web.enable-lifecycle'
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml
      - ./prometheus/alerts:/etc/prometheus/alerts
      - prometheus-data:/prometheus
    ports:
      - "9090:9090"
    networks:
      - order-network

  grafana:
    image: grafana/grafana:11.2.0
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: secret
      GF_USERS_ALLOW_SIGN_UP: "false"
      GF_FEATURE_TOGGLES_ENABLE: "traceqlEditor"
    volumes:
      - grafana-data:/var/lib/grafana
      - ./grafana/provisioning:/etc/grafana/provisioning
      - ./grafana/dashboards:/var/lib/grafana/dashboards
    ports:
      - "3000:3000"
    depends_on:
      - prometheus
      - jaeger
    networks:
      - order-network

  alertmanager:
    image: prom/alertmanager:v0.27.0
    volumes:
      - ./alertmanager/alertmanager.yml:/etc/alertmanager/alertmanager.yml
    ports:
      - "9093:9093"
    networks:
      - order-network
```

```dockerfile
# File: src/OrderService/Dockerfile

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY ["src/OrderService/OrderService.csproj", "src/OrderService/"]
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
RUN dotnet restore "src/OrderService/OrderService.csproj"
COPY . .
WORKDIR "/src/src/OrderService"
RUN dotnet build "OrderService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Tạo user không phải root để tăng bảo mật
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderService.dll"]
```

---

## 12. Alerting và Monitoring

### 12.1 Prometheus Alert Rules

```yaml
# File: prometheus/alerts/order-system.yml

groups:
  - name: order-system-slo
    interval: 30s
    rules:
      # Cảnh báo khi tỷ lệ lỗi vượt quá 5%
      - alert: HighOrderErrorRate
        expr: |
          sum(rate(orders_failed_total[5m])) /
          (sum(rate(orders_created_total[5m])) + sum(rate(orders_failed_total[5m]))) > 0.05
        for: 2m
        labels:
          severity: critical
          team: platform
        annotations:
          summary: "Tỷ lệ lỗi đơn hàng cao"
          description: |
            Tỷ lệ lỗi hiện tại: {{ $value | humanizePercentage }}
            Ngưỡng cho phép: 5%
            Vui lòng kiểm tra logs tại Jaeger với filter: http.status_code=5xx
          runbook_url: "https://wiki/runbooks/high-order-error-rate"

      # Cảnh báo khi latency p99 vượt quá 2 giây
      - alert: HighOrderLatency
        expr: |
          histogram_quantile(0.99,
            sum(rate(http_server_request_duration_milliseconds_bucket{
              job="order-service",
              http_route="/api/orders"
            }[5m])) by (le)
          ) > 2000
        for: 5m
        labels:
          severity: warning
          team: backend
        annotations:
          summary: "Latency đơn hàng cao (p99 > 2s)"
          description: |
            p99 latency: {{ $value }}ms
            Service: order-service
          runbook_url: "https://wiki/runbooks/high-latency"

      # Cảnh báo khi số đơn hàng pending tăng bất thường
      - alert: PendingOrdersAccumulating
        expr: orders_pending_count_orders > 500
        for: 10m
        labels:
          severity: warning
          team: backend
        annotations:
          summary: "Đơn hàng pending đang tích lũy"
          description: |
            Số đơn hàng pending: {{ $value }}
            Worker service có thể đang gặp vấn đề.

      # Cảnh báo khi service không phản hồi
      - alert: ServiceDown
        expr: up{job=~"order-service|product-service|worker-service"} == 0
        for: 1m
        labels:
          severity: critical
          team: platform
        annotations:
          summary: "Service {{ $labels.job }} không hoạt động"
          description: |
            Service {{ $labels.job }} đã ngừng hoạt động trong hơn 1 phút.
            Instance: {{ $labels.instance }}

  - name: infrastructure
    rules:
      # Cảnh báo memory cao
      - alert: HighMemoryUsage
        expr: |
          process_runtime_dotnet_gc_heap_size_bytes /
          container_memory_limit_bytes > 0.85
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Memory usage cao tại {{ $labels.job }}"
          description: "Memory: {{ $value | humanizePercentage }} của limit"

      # Cảnh báo GC pressure
      - alert: HighGCPressure
        expr: |
          rate(process_runtime_dotnet_gc_collections_total{generation="2"}[5m]) > 1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Gen2 GC rate cao tại {{ $labels.job }}"
```

### 12.2 Alertmanager Configuration

```yaml
# File: alertmanager/alertmanager.yml

global:
  smtp_smarthost: 'smtp.company.com:587'
  smtp_from: 'alerts@company.com'
  smtp_auth_username: 'alerts@company.com'
  smtp_auth_password: '${SMTP_PASSWORD}'
  slack_api_url: '${SLACK_WEBHOOK_URL}'

templates:
  - '/etc/alertmanager/templates/*.tmpl'

route:
  group_by: ['alertname', 'job']
  group_wait: 30s
  group_interval: 5m
  repeat_interval: 3h
  receiver: 'default-receiver'

  routes:
    # Critical alerts → PagerDuty + Slack
    - matchers:
        - severity = critical
      receiver: 'critical-alerts'
      repeat_interval: 1h

    # Warning alerts → Slack only
    - matchers:
        - severity = warning
      receiver: 'warning-alerts'

receivers:
  - name: 'default-receiver'
    slack_configs:
      - channel: '#alerts-general'
        title: 'Alert: {{ .GroupLabels.alertname }}'
        text: '{{ range .Alerts }}{{ .Annotations.description }}{{ end }}'

  - name: 'critical-alerts'
    slack_configs:
      - channel: '#alerts-critical'
        title: '🚨 CRITICAL: {{ .GroupLabels.alertname }}'
        text: |
          *Summary:* {{ .CommonAnnotations.summary }}
          *Description:* {{ .CommonAnnotations.description }}
          *Runbook:* {{ .CommonAnnotations.runbook_url }}
    email_configs:
      - to: 'oncall@company.com'
        subject: '[CRITICAL] {{ .GroupLabels.alertname }}'

  - name: 'warning-alerts'
    slack_configs:
      - channel: '#alerts-warning'
        title: '⚠️ WARNING: {{ .GroupLabels.alertname }}'
        text: '{{ .CommonAnnotations.description }}'
```

### 12.3 Grafana Provisioning

```yaml
# File: grafana/provisioning/datasources/datasources.yaml

apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    editable: false

  - name: Jaeger
    type: jaeger
    access: proxy
    url: http://jaeger:16686
    editable: false
    jsonData:
      tracesToLogsV2:
        datasourceUid: loki
        filterByTraceID: true
        filterBySpanID: true

  - name: Loki
    type: loki
    access: proxy
    url: http://loki:3100
    editable: false
    jsonData:
      derivedFields:
        - datasourceUid: jaeger
          matcherRegex: '"TraceId":"([0-9a-f]{32})"'
          name: TraceID
          url: "$${__value.raw}"
```

---

## 13. Best Practices

### 13.1 Sampling Strategy

```csharp
// File: src/Shared/Telemetry/SamplingConfiguration.cs

using OpenTelemetry.Trace;

namespace Shared.Telemetry;

/// <summary>
/// Cấu hình sampling để cân bằng giữa observability và performance.
/// Trong production, không nên trace 100% request vì tốn tài nguyên.
/// </summary>
public static class SamplingConfiguration
{
    /// <summary>
    /// Tạo composite sampler: trace 100% lỗi, 10% request bình thường.
    /// </summary>
    public static Sampler CreateProductionSampler() =>
        new ParentBasedSampler(
            new TraceIdRatioBasedSampler(0.1));   // 10% sample rate

    /// <summary>
    /// Custom sampler: luôn trace error và slow requests.
    /// </summary>
    public class SmartSampler : Sampler
    {
        private readonly double _defaultSampleRate;
        private readonly TraceIdRatioBasedSampler _ratioSampler;

        public SmartSampler(double defaultSampleRate = 0.1)
        {
            _defaultSampleRate = defaultSampleRate;
            _ratioSampler = new TraceIdRatioBasedSampler(defaultSampleRate);
        }

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            // Luôn trace nếu parent đã được sample
            if (samplingParameters.ParentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded))
            {
                return new SamplingResult(SamplingDecision.RecordAndSample);
            }

            // Luôn trace các endpoint quan trọng
            var httpTarget = samplingParameters.Tags
                .FirstOrDefault(t => t.Key == "http.target").Value?.ToString() ?? "";

            if (httpTarget.StartsWith("/api/orders") ||
                httpTarget.StartsWith("/api/payments"))
            {
                return new SamplingResult(SamplingDecision.RecordAndSample);
            }

            // Không trace health checks và metrics
            if (httpTarget.StartsWith("/health") || httpTarget.StartsWith("/metrics"))
            {
                return new SamplingResult(SamplingDecision.Drop);
            }

            // Áp dụng ratio sampling cho các request còn lại
            return _ratioSampler.ShouldSample(in samplingParameters);
        }
    }
}
```

### 13.2 Span Naming Conventions

```csharp
// File: src/Shared/Telemetry/SpanNamingConventions.cs

namespace Shared.Telemetry;

/// <summary>
/// Quy ước đặt tên span — quan trọng để search và group traces hiệu quả.
///
/// Quy tắc:
/// - Format: "{ServiceName}.{ComponentName}.{OperationName}"
/// - Dùng PascalCase
/// - Không dùng các giá trị dynamic (orderId, userId) trong tên span
///   → Dùng tags/attributes thay thế
///
/// Ví dụ ĐÚNG:
///   "OrderService.OrderRepository.Create"
///   "ProductService.Cache.Get"
///   "WorkerService.PaymentProcessor.Charge"
///
/// Ví dụ SAI:
///   "Create Order for user-123"     ← Dynamic data trong tên
///   "db_query"                       ← Quá chung chung
///   "order-service-create-order"     ← Không đúng format
/// </summary>
public static class SpanNames
{
    public static class OrderService
    {
        public const string CreateOrder = "OrderService.Orders.Create";
        public const string GetOrder = "OrderService.Orders.Get";
        public const string ValidateInventory = "OrderService.Inventory.Validate";
        public const string PublishEvent = "OrderService.Events.Publish";

        public static class Repository
        {
            public const string Create = "OrderService.OrderRepository.Create";
            public const string GetById = "OrderService.OrderRepository.GetById";
            public const string UpdateStatus = "OrderService.OrderRepository.UpdateStatus";
        }
    }

    public static class ProductService
    {
        public const string GetProducts = "ProductService.Products.Get";
        public const string UpdateStock = "ProductService.Stock.Update";

        public static class Cache
        {
            public const string Get = "ProductService.Cache.Get";
            public const string Set = "ProductService.Cache.Set";
        }
    }

    public static class WorkerService
    {
        public const string ProcessOrder = "WorkerService.Orders.Process";
        public const string ProcessPayment = "WorkerService.Payments.Charge";
        public const string SendNotification = "WorkerService.Notifications.Send";
    }
}
```

### 13.3 Health Checks tích hợp với Observability

```csharp
// File: src/Shared/HealthChecks/ObservabilityHealthCheck.cs

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shared.HealthChecks;

/// <summary>
/// Health check kiểm tra OTel Collector có reachable không.
/// Quan trọng để phát hiện sớm khi telemetry pipeline bị gián đoạn.
/// </summary>
public class OtelCollectorHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _collectorEndpoint;

    public OtelCollectorHealthCheck(IHttpClientFactory factory, string collectorEndpoint)
    {
        _httpClientFactory = factory;
        _collectorEndpoint = collectorEndpoint;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            // OTel Collector health endpoint
            var response = await client.GetAsync($"{_collectorEndpoint}/", ct);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("OTel Collector is reachable")
                : HealthCheckResult.Degraded(
                    $"OTel Collector returned {response.StatusCode}. " +
                    "Telemetry data may not be exported.");
        }
        catch (Exception ex)
        {
            // Degraded (không phải Unhealthy) vì app vẫn hoạt động được
            // chỉ là không có observability
            return HealthCheckResult.Degraded(
                "Cannot reach OTel Collector. Telemetry will be lost.",
                ex);
        }
    }
}
```

### 13.4 Testing Observability

```csharp
// File: tests/OrderService.Tests/Telemetry/OrderServiceTelemetryTests.cs

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace OrderService.Tests.Telemetry;

/// <summary>
/// Test để đảm bảo telemetry được emitted đúng cách.
/// Quan trọng trong production vì thiếu trace có thể gây khó debug.
/// </summary>
public class OrderServiceTelemetryTests : IDisposable
{
    private readonly List<Activity> _exportedActivities = new();
    private readonly TracerProvider _tracerProvider;

    public OrderServiceTelemetryTests()
    {
        // Setup in-memory exporter để capture spans trong test
        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(TelemetryConstants.OrderSource)
            .AddInMemoryExporter(_exportedActivities)
            .Build()!;
    }

    [Fact]
    public async Task CreateOrder_ShouldEmitCreateOrderSpan()
    {
        // Arrange
        var orderService = CreateOrderService();
        var request = CreateValidOrderRequest();

        // Act
        await orderService.CreateOrderAsync(request);

        // Assert
        var createOrderSpan = _exportedActivities
            .FirstOrDefault(a => a.OperationName == "OrderService.Orders.Create");

        Assert.NotNull(createOrderSpan);
        Assert.Equal(ActivityStatusCode.Ok, createOrderSpan.Status);
        Assert.Equal(
            request.CustomerId.ToString(),
            createOrderSpan.GetTagItem(TelemetryConstants.SpanAttributes.CustomerId)?.ToString());
    }

    [Fact]
    public async Task CreateOrder_WhenFailed_ShouldEmitErrorSpan()
    {
        // Arrange
        var orderService = CreateOrderServiceWithFailingRepository();
        var request = CreateValidOrderRequest();

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => orderService.CreateOrderAsync(request));

        var failedSpan = _exportedActivities
            .FirstOrDefault(a => a.OperationName == "OrderService.Orders.Create");

        Assert.NotNull(failedSpan);
        Assert.Equal(ActivityStatusCode.Error, failedSpan.Status);
        Assert.NotNull(failedSpan.Events.FirstOrDefault(e => e.Name == "exception"));
    }

    [Fact]
    public async Task CreateOrder_ShouldCreateChildSpans()
    {
        // Arrange
        var orderService = CreateOrderService();
        var request = CreateValidOrderRequest();

        // Act
        await orderService.CreateOrderAsync(request);

        // Assert — kiểm tra child spans được tạo đúng
        var spanNames = _exportedActivities.Select(a => a.OperationName).ToList();

        Assert.Contains("OrderService.Orders.Create", spanNames);
        Assert.Contains("OrderService.Inventory.Validate", spanNames);
        Assert.Contains("OrderService.OrderRepository.Create", spanNames);
        Assert.Contains("OrderService.Events.Publish", spanNames);

        // Kiểm tra parent-child relationship
        var parentSpan = _exportedActivities
            .First(a => a.OperationName == "OrderService.Orders.Create");
        var childSpans = _exportedActivities
            .Where(a => a.ParentId == parentSpan.Id)
            .ToList();

        Assert.True(childSpans.Count >= 2, "CreateOrder should have at least 2 child spans");
    }

    public void Dispose()
    {
        _tracerProvider.Dispose();
    }

    // Helper methods...
    private IOrderService CreateOrderService() => throw new NotImplementedException();
    private IOrderService CreateOrderServiceWithFailingRepository() => throw new NotImplementedException();
    private CreateOrderRequest CreateValidOrderRequest() => throw new NotImplementedException();
}
```

### 13.5 Danh sách kiểm tra trước khi Production

```
OBSERVABILITY PRODUCTION CHECKLIST
====================================

TRACES:
  ☐ Tất cả HTTP endpoints đều được instrument (tự động qua AddAspNetCoreInstrumentation)
  ☐ Tất cả outgoing HTTP calls được instrument (tự động qua AddHttpClientInstrumentation)
  ☐ Custom spans được tạo cho business logic quan trọng
  ☐ Span attributes chứa đủ context để debug
  ☐ Exceptions được record vào spans (RecordException = true)
  ☐ Trace context được propagate qua message queues
  ☐ Sampling strategy phù hợp (không trace 100% trong production)
  ☐ Health check và metrics endpoints bị exclude khỏi tracing

METRICS:
  ☐ Business metrics được định nghĩa (orders created, revenue, errors)
  ☐ System metrics được thu thập (CPU, memory, GC)
  ☐ HTTP metrics được thu thập (latency histograms, error rates)
  ☐ Prometheus scrape endpoint hoạt động (/metrics)
  ☐ Grafana dashboards được tạo cho SLO monitoring

LOGS:
  ☐ Serilog được cấu hình với OTel enricher (TraceId, SpanId trong logs)
  ☐ Log levels phù hợp (không log DEBUG trong production)
  ☐ Sensitive data không bị log (passwords, tokens, PII)
  ☐ Structured logging (không dùng string interpolation)
  ☐ Log aggregation được cấu hình (Loki, ELK, etc.)

ALERTING:
  ☐ Alert rules được định nghĩa cho error rate
  ☐ Alert rules được định nghĩa cho latency SLO
  ☐ Alert rules được định nghĩa cho service availability
  ☐ Alert routing được cấu hình (critical → PagerDuty, warning → Slack)
  ☐ Runbooks được viết cho mỗi alert

INFRASTRUCTURE:
  ☐ OTel Collector được deploy (không send trực tiếp từ app đến backend)
  ☐ Jaeger/Tempo được deploy cho trace storage
  ☐ Prometheus được deploy cho metrics storage
  ☐ Grafana được deploy với dashboards provisioned
  ☐ Alertmanager được cấu hình
  ☐ Retention policy được cấu hình cho mỗi storage
```

---

## Tóm tắt và Kết luận

Trong bài học này, chúng ta đã học cách xây dựng một hệ thống observability hoàn chỉnh cho ứng dụng .NET microservices bằng OpenTelemetry:

### Những điểm quan trọng cần nhớ:

1. **Ba trụ cột Observability** — Traces, Metrics, Logs phải hoạt động cùng nhau. Không có cái nào đủ mạnh khi đứng riêng lẻ.

2. **OpenTelemetry là vendor-neutral** — Viết code một lần, export đến Jaeger, Zipkin, Prometheus, Datadog, hay bất kỳ backend nào mà không cần thay đổi code.

3. **Context Propagation là nền tảng** — Để có distributed trace có ý nghĩa, bạn phải truyền trace context qua HTTP headers, message queue headers, và bất kỳ boundary nào giữa services.

4. **ActivitySource và Meter là zero-cost khi không có listener** — Code production sẽ không bị ảnh hưởng hiệu suất nếu OTel không được cấu hình.

5. **Sampling là bắt buộc trong production** — Đừng trace 100% request. Dùng head-based hoặc tail-based sampling phù hợp với nhu cầu.

6. **Đặt tên quan trọng** — Tên span và metric names phải nhất quán và có ý nghĩa. Chúng ảnh hưởng trực tiếp đến khả năng search và group trong các observability tools.

### Tài liệu tham khảo:

- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/languages/net/)
- [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [Jaeger Documentation](https://www.jaegertracing.io/docs/)
- [Prometheus Best Practices](https://prometheus.io/docs/practices/)
- [CNCF Observability Whitepaper](https://github.com/cncf/tag-observability)
