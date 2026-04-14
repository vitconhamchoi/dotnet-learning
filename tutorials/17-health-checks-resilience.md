# Bài 17: Health Checks & Resilience Patterns trong .NET

> **Mục tiêu**: Hiểu và áp dụng Health Checks + Resilience Patterns để xây dựng hệ thống .NET có khả năng chịu lỗi cao, tự phục hồi và dễ vận hành trên môi trường production / Kubernetes.

---

## Mục lục

1. [Giới thiệu: Tại sao cần Health Checks & Resilience?](#1-giới-thiệu)
2. [Health Check Concepts: Liveness, Readiness, Startup](#2-health-check-concepts)
3. [ASP.NET Core Health Checks](#3-aspnet-core-health-checks)
4. [Health Check UI Dashboard](#4-health-check-ui-dashboard)
5. [Resilience Patterns](#5-resilience-patterns)
6. [Microsoft.Extensions.Http.Resilience (Polly v8)](#6-polly-v8)
7. [ShoppingCart Service - Ví dụ hoàn chỉnh](#7-shoppingcart-service)
8. [Chaos Engineering](#8-chaos-engineering)
9. [SLA, SLO, SLI và Error Budgets](#9-sla-slo-sli)
10. [Best Practices](#10-best-practices)

---

## 1. Giới thiệu

Trong hệ thống phân tán (microservices), **mọi thứ đều có thể thất bại**:

- Database bị quá tải hoặc restart
- Một service phụ thuộc (dependency) bị chậm hoặc sập
- Network bị gián đoạn tạm thời
- Bộ nhớ hoặc CPU bị cạn kiệt

**Resilience** (khả năng phục hồi) là năng lực của hệ thống tiếp tục hoạt động đúng đắn — hoặc phục hồi nhanh chóng — khi gặp lỗi. Health Checks giúp orchestration platform (Kubernetes) và load balancer biết instance nào đang "khỏe mạnh" để định tuyến traffic.

```
┌─────────────────────────────────────────────────────────────────┐
│                    Resilience Mindset                           │
│                                                                 │
│  "Không phải IF hệ thống sẽ lỗi, mà là WHEN nó sẽ lỗi"        │
│                                                                 │
│   Hệ thống tốt = Detect lỗi sớm + Recover tự động + Degrade    │
│                   gracefully khi không recover được             │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Health Check Concepts

### 2.1 Ba loại Probe trong Kubernetes

Kubernetes sử dụng ba loại probe để quản lý vòng đời của một Pod:

```
┌──────────────────────────────────────────────────────────────────────┐
│                    Kubernetes Pod Lifecycle                          │
│                                                                      │
│  [Pod Start]                                                         │
│      │                                                               │
│      ▼                                                               │
│  ┌─────────────┐   FAIL (timeout)    ┌──────────────────────┐       │
│  │  Startup    │──────────────────►  │  Pod killed &        │       │
│  │  Probe      │                     │  restarted           │       │
│  │  /health/   │                     └──────────────────────┘       │
│  │  startup    │                                                     │
│  └──────┬──────┘                                                     │
│         │ PASS                                                       │
│         ▼                                                            │
│  ┌─────────────┐   FAIL (N times)    ┌──────────────────────┐       │
│  │  Liveness   │──────────────────►  │  Pod killed &        │       │
│  │  Probe      │                     │  restarted           │       │
│  │  /health/   │                     └──────────────────────┘       │
│  │  live       │                                                     │
│  └──────┬──────┘  (chạy liên tục)                                   │
│         │ PASS                                                       │
│         ▼                                                            │
│  ┌─────────────┐   FAIL             ┌──────────────────────┐        │
│  │  Readiness  │──────────────────► │  Remove from Service │        │
│  │  Probe      │                    │  Load Balancer       │        │
│  │  /health/   │                    │  (Pod NOT killed)    │        │
│  │  ready      │                    └──────────────────────┘        │
│  └─────────────┘  (chạy liên tục)                                   │
└──────────────────────────────────────────────────────────────────────┘
```

#### Startup Probe
- **Khi nào chạy**: Chỉ khi Pod mới khởi động
- **Mục đích**: Kiểm tra ứng dụng đã khởi động xong chưa (đặc biệt với app chậm như legacy app cần warm-up lâu)
- **Hành động khi FAIL**: Kubernetes kill và restart Pod
- **Lưu ý**: Trong khi Startup Probe chưa PASS, Liveness và Readiness Probe không chạy

#### Liveness Probe
- **Khi nào chạy**: Liên tục sau khi Startup Probe pass
- **Mục đích**: Kiểm tra ứng dụng có còn "sống" không (không bị deadlock, không bị crash silent)
- **Hành động khi FAIL**: Kubernetes kill và restart Pod
- **Ví dụ check**: Process còn chạy không? Có respond HTTP không? Memory leak chưa?

#### Readiness Probe
- **Khi nào chạy**: Liên tục sau khi Startup Probe pass
- **Mục đích**: Kiểm tra ứng dụng có **sẵn sàng nhận traffic** không
- **Hành động khi FAIL**: Kubernetes loại Pod khỏi Service endpoints (không kill Pod!)
- **Ví dụ check**: Database connection OK? Cache warm up xong? Dependent services available?

### 2.2 So sánh trực quan

| Tiêu chí | Startup | Liveness | Readiness |
|----------|---------|----------|-----------|
| Thời điểm chạy | Chỉ khi start | Luôn luôn | Luôn luôn |
| Khi FAIL | Kill + Restart Pod | Kill + Restart Pod | Remove khỏi LB |
| Mục đích | App đã load xong? | App còn sống? | App nhận traffic được? |
| Check gì | Init tasks xong chưa | Basic liveness | All deps ready |

---

## 3. ASP.NET Core Health Checks

### 3.1 Cài đặt packages

```bash
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks
dotnet add package AspNetCore.HealthChecks.SqlServer
dotnet add package AspNetCore.HealthChecks.Redis
dotnet add package AspNetCore.HealthChecks.RabbitMQ
dotnet add package AspNetCore.HealthChecks.Uris
```

### 3.2 Health Check cơ bản

```csharp
// Program.cs - cấu hình cơ bản
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    // Built-in: kiểm tra SQL Server
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        healthQuery: "SELECT 1",
        name: "sqlserver",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "db", "sql", "ready" })

    // Built-in: kiểm tra Redis
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "cache", "ready" })

    // Built-in: kiểm tra RabbitMQ
    .AddRabbitMQ(
        rabbitConnectionString: builder.Configuration.GetConnectionString("RabbitMQ")!,
        name: "rabbitmq",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "messaging", "ready" });

var app = builder.Build();

// Endpoint cho Kubernetes Liveness Probe
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Chỉ check những thứ cơ bản - app có còn sống không
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Endpoint cho Kubernetes Readiness Probe  
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Check tất cả dependencies
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Endpoint cho Kubernetes Startup Probe
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Endpoint tổng quan (cho monitoring dashboard)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 3.3 Custom Health Check - IHealthCheck Interface

#### Custom Check đơn giản: kiểm tra disk space

```csharp
// Checks/DiskSpaceHealthCheck.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly long _minimumFreeBytesRequired;
    private readonly ILogger<DiskSpaceHealthCheck> _logger;

    public DiskSpaceHealthCheck(
        long minimumFreeBytesRequired,
        ILogger<DiskSpaceHealthCheck> logger)
    {
        _minimumFreeBytesRequired = minimumFreeBytesRequired;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);

            if (drive == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Không tìm thấy ổ đĩa nào"));
            }

            var freeBytes = drive.AvailableFreeSpace;
            var freeMb = freeBytes / (1024 * 1024);
            var totalMb = drive.TotalSize / (1024 * 1024);
            var usedPercent = (double)(drive.TotalSize - freeBytes) / drive.TotalSize * 100;

            var data = new Dictionary<string, object>
            {
                { "drive", drive.Name },
                { "free_mb", freeMb },
                { "total_mb", totalMb },
                { "used_percent", Math.Round(usedPercent, 1) }
            };

            if (freeBytes < _minimumFreeBytesRequired)
            {
                _logger.LogWarning("Disk space thấp: {FreeMb}MB còn lại", freeMb);
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Disk space thấp: {freeMb}MB. Yêu cầu tối thiểu: {_minimumFreeBytesRequired / (1024 * 1024)}MB",
                    data: data));
            }

            if (usedPercent > 80)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Disk đã dùng {usedPercent:F1}% - sắp hết dung lượng",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk OK: {freeMb}MB còn lại ({usedPercent:F1}% đã dùng)",
                data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi kiểm tra disk space");
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                "Không thể kiểm tra disk space",
                ex));
        }
    }
}
```

#### Custom Check nâng cao: kiểm tra business logic

```csharp
// Checks/LowStockHealthCheck.cs
public class LowStockHealthCheck : IHealthCheck
{
    private readonly IProductRepository _productRepository;
    private readonly int _lowStockThreshold;
    private readonly int _criticalStockThreshold;

    public LowStockHealthCheck(
        IProductRepository productRepository,
        IConfiguration configuration)
    {
        _productRepository = productRepository;
        _lowStockThreshold = configuration.GetValue<int>("HealthChecks:LowStockThreshold", 10);
        _criticalStockThreshold = configuration.GetValue<int>("HealthChecks:CriticalStockThreshold", 3);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Query số sản phẩm có tồn kho thấp
            var lowStockCount = await _productRepository
                .CountProductsBelowStockAsync(_lowStockThreshold, cancellationToken);

            var criticalStockCount = await _productRepository
                .CountProductsBelowStockAsync(_criticalStockThreshold, cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "low_stock_products", lowStockCount },
                { "critical_stock_products", criticalStockCount },
                { "low_threshold", _lowStockThreshold },
                { "critical_threshold", _criticalStockThreshold }
            };

            if (criticalStockCount > 0)
            {
                // Degraded: hệ thống vẫn chạy nhưng cần chú ý
                return HealthCheckResult.Degraded(
                    $"Cảnh báo: {criticalStockCount} sản phẩm sắp hết hàng (< {_criticalStockThreshold} đơn vị)",
                    data: data);
            }

            if (lowStockCount > 5)
            {
                return HealthCheckResult.Degraded(
                    $"Lưu ý: {lowStockCount} sản phẩm có tồn kho thấp",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Tồn kho bình thường. {lowStockCount} sản phẩm cần chú ý.",
                data);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Không thể kiểm tra tồn kho",
                ex);
        }
    }
}
```

#### Extension method để đăng ký custom checks gọn hơn

```csharp
// Extensions/HealthCheckExtensions.cs
public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddDiskSpaceCheck(
        this IHealthChecksBuilder builder,
        long minimumFreeMegabytes = 512,
        string name = "disk_space",
        HealthStatus failureStatus = HealthStatus.Degraded,
        IEnumerable<string>? tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new DiskSpaceHealthCheck(
                minimumFreeMegabytes * 1024 * 1024,
                sp.GetRequiredService<ILogger<DiskSpaceHealthCheck>>()),
            failureStatus,
            tags ?? new[] { "infrastructure", "ready" }));
    }

    public static IHealthChecksBuilder AddLowStockCheck(
        this IHealthChecksBuilder builder,
        string name = "low_stock",
        HealthStatus failureStatus = HealthStatus.Degraded,
        IEnumerable<string>? tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new LowStockHealthCheck(
                sp.GetRequiredService<IProductRepository>(),
                sp.GetRequiredService<IConfiguration>()),
            failureStatus,
            tags ?? new[] { "business", "ready" }));
    }
}
```

### 3.4 Health Check Publisher - Gửi metrics lên monitoring

```csharp
// Publishers/PrometheusHealthCheckPublisher.cs
// Publish health check results lên Prometheus metrics
public class PrometheusHealthCheckPublisher : IHealthCheckPublisher
{
    private readonly ILogger<PrometheusHealthCheckPublisher> _logger;

    // Trong thực tế dùng prometheus-net library để expose metrics
    // Ở đây dùng ILogger để minh họa
    public PrometheusHealthCheckPublisher(ILogger<PrometheusHealthCheckPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        // Log tổng trạng thái
        _logger.LogInformation(
            "Health Check Report: Status={Status}, Duration={Duration}ms",
            report.Status,
            report.TotalDuration.TotalMilliseconds);

        foreach (var entry in report.Entries)
        {
            var (name, result) = (entry.Key, entry.Value);

            // Trong thực tế: set Prometheus gauge metric
            // healthCheckGauge.WithLabels(name, result.Status.ToString()).Set(
            //     result.Status == HealthStatus.Healthy ? 1 : 0);

            _logger.LogInformation(
                "  [{Name}]: {Status} - {Description} ({Duration}ms)",
                name,
                result.Status,
                result.Description,
                result.Duration.TotalMilliseconds);

            if (result.Exception != null)
            {
                _logger.LogError(result.Exception,
                    "  [{Name}] Exception: {Message}", name, result.Exception.Message);
            }
        }

        return Task.CompletedTask;
    }
}

// Đăng ký publisher
builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(5);    // Chờ 5s sau khi app start
    options.Period = TimeSpan.FromSeconds(30);  // Publish mỗi 30 giây
    options.Timeout = TimeSpan.FromSeconds(10); // Timeout cho mỗi lần publish
});

builder.Services.AddSingleton<IHealthCheckPublisher, PrometheusHealthCheckPublisher>();
```

---

## 4. Health Check UI Dashboard

### 4.1 Cài đặt

```bash
dotnet add package AspNetCore.HealthChecks.UI
dotnet add package AspNetCore.HealthChecks.UI.InMemory.Storage
```

### 4.2 Cấu hình UI

```csharp
// Program.cs
builder.Services
    .AddHealthChecksUI(options =>
    {
        options.SetEvaluationTimeInSeconds(15);      // Poll mỗi 15 giây
        options.MaximumHistoryEntriesPerEndpoint(50); // Giữ 50 records lịch sử
        options.SetApiMaxActiveRequests(1);           // Tránh DDoS chính mình

        // Thêm các endpoints cần monitor
        options.AddHealthCheckEndpoint("ShoppingCart API", "/health");
        options.AddHealthCheckEndpoint("Product Service", "http://product-service/health");
        options.AddHealthCheckEndpoint("Payment Service", "http://payment-service/health");
    })
    .AddInMemoryStorage(); // Dùng InMemory cho dev, dùng SQL cho production

var app = builder.Build();

// Serve health check UI tại /healthchecks-ui
app.MapHealthChecksUI(options =>
{
    options.UIPath = "/healthchecks-ui";
    options.ApiPath = "/healthchecks-api";
});
```

### 4.3 appsettings.json cho Health Check UI

```json
{
  "HealthChecksUI": {
    "HealthChecks": [
      {
        "Name": "ShoppingCart API",
        "Uri": "http://localhost:5000/health"
      }
    ],
    "EvaluationTimeInSeconds": 15,
    "MinimumSecondsBetweenFailureNotifications": 60
  }
}
```

---

## 5. Resilience Patterns

### 5.1 Retry Pattern

**Retry** là pattern đơn giản nhất: khi request thất bại, thử lại sau một khoảng thời gian.

#### Exponential Backoff

Thay vì retry ngay lập tức (có thể làm server bị quá tải hơn), ta tăng dần thời gian chờ:

```
┌──────────────────────────────────────────────────────────────┐
│              Exponential Backoff với Jitter                  │
│                                                              │
│  Request 1 ──────────────────────────────────► FAIL         │
│                │                                             │
│                └─── wait 1s ──► Retry 1 ───────► FAIL       │
│                                    │                         │
│                                    └─── wait 2s ─► Retry 2  │
│                                                      │       │
│                                                      └─ FAIL │
│                                                        │     │
│                                              wait 4s ──┘     │
│                                                        │     │
│                                                     Retry 3  │
│                                                        │     │
│                                                      SUCCESS │
│                                                              │
│  Thời gian chờ: 2^attempt × baseDelay + random jitter       │
│  Attempt 1: 2^1 × 500ms + jitter = ~1000-1500ms             │
│  Attempt 2: 2^2 × 500ms + jitter = ~2000-2500ms             │
│  Attempt 3: 2^3 × 500ms + jitter = ~4000-4500ms             │
└──────────────────────────────────────────────────────────────┘
```

**Tại sao cần Jitter?**

Nếu 100 clients đều retry cùng một lúc (sau đúng 2 giây), server vẫn bị quá tải theo từng đợt. Jitter (ngẫu nhiên nhỏ) giúp trải đều các retry request, tránh "thundering herd problem".

### 5.2 Circuit Breaker Pattern

Circuit Breaker mô phỏng cầu dao điện trong nhà: khi phát hiện quá nhiều lỗi, tự động "ngắt mạch" để bảo vệ hệ thống downstream.

```
┌────────────────────────────────────────────────────────────────────┐
│                Circuit Breaker State Machine                       │
│                                                                    │
│                     ┌──────────────┐                               │
│                     │    CLOSED    │  ◄─── Trạng thái bình thường  │
│                     │  (Requests   │       Cho phép tất cả request  │
│                     │   flow OK)   │                               │
│                     └──────┬───────┘                               │
│                            │                                       │
│              Lỗi vượt threshold                                    │
│              (vd: 50% fail trong 10s)                              │
│                            │                                       │
│                            ▼                                       │
│                     ┌──────────────┐                               │
│                     │     OPEN     │  ◄─── "Ngắt mạch"             │
│                     │  (Fail fast, │       Từ chối ngay tất cả     │
│                     │   no calls)  │       request, không gọi      │
│                     └──────┬───────┘       downstream              │
│                            │                                       │
│              Sau break duration                                    │
│              (vd: 30 giây)                                         │
│                            │                                       │
│                            ▼                                       │
│                     ┌──────────────┐                               │
│                     │  HALF-OPEN   │  ◄─── Thử nghiệm              │
│                     │  (Test with  │       Cho qua một số request  │
│                     │  few reqs)   │       để test xem server đã   │
│                     └──────┬───────┘       phục hồi chưa           │
│                            │                                       │
│               ┌────────────┴────────────┐                          │
│               │                         │                          │
│           Test PASS                 Test FAIL                      │
│        (server OK)              (server vẫn lỗi)                   │
│               │                         │                          │
│               ▼                         ▼                          │
│           CLOSED                      OPEN                         │
│         (Khôi phục)               (Ngắt lại)                       │
└────────────────────────────────────────────────────────────────────┘
```

### 5.3 Timeout Pattern

**Timeout** đặt giới hạn thời gian tối đa cho một operation. Không có timeout → một request bị treo có thể giữ thread/connection mãi mãi.

```
Không có Timeout:
  Request ──────────────────────────────────────────────► (treo mãi mãi)
  Thread bị block, connection pool cạn kiệt

Có Timeout (5 giây):
  Request ─────────────────────────┐
                                   │ 5s timeout
                                   ▼
                              TimeoutException
                              → Retry / Fallback / Error response
```

### 5.4 Bulkhead / Isolation Pattern

**Bulkhead** (vách ngăn) là kỹ thuật cô lập tài nguyên để lỗi trong một phần không lan sang phần khác.

```
┌──────────────────────────────────────────────────────────────────┐
│                    Bulkhead Isolation                            │
│                                                                  │
│  KHÔNG có Bulkhead:                                              │
│  ┌─────────────────────────────────────────────┐                 │
│  │           Thread Pool: 100 threads          │                 │
│  │                                             │                 │
│  │  Payment calls: 98 threads (bị treo)  ──────┼──► Pool cạn    │
│  │  Product calls: 2 threads còn lại     ──────┼──► Bị ảnh hưởng│
│  └─────────────────────────────────────────────┘                 │
│                                                                  │
│  CÓ Bulkhead:                                                    │
│  ┌───────────────────────┐  ┌───────────────────────┐            │
│  │  Payment Thread Pool  │  │  Product Thread Pool  │            │
│  │     (30 threads)      │  │     (30 threads)      │            │
│  │                       │  │                       │            │
│  │  Payment calls: 30    │  │  Product calls: OK ✓  │            │
│  │  (bị treo hết)        │  │  (không bị ảnh hưởng) │            │
│  │  → Fail fast          │  │  → Tiếp tục hoạt động │            │
│  └───────────────────────┘  └───────────────────────┘            │
└──────────────────────────────────────────────────────────────────┘
```

### 5.5 Fallback Pattern

**Fallback** cung cấp giá trị/hành vi thay thế khi operation thất bại:

- Trả về cached data cũ
- Trả về default value
- Gọi service khác (backup)
- Trả về response partial/degraded

### 5.6 Hedge Pattern (Speculative Retry)

**Hedge** gửi request thứ hai (song song) nếu request đầu không trả về kết quả trong thời gian quy định, nhận kết quả từ request nào về trước. Giảm tail latency (P99) hiệu quả.

```
Hedge Pattern:
  Request 1 ──────────────────────────────────────────► SUCCESS (dùng kết quả này)
                    │
                    │ Sau 200ms (hedge delay), nếu chưa có kết quả:
                    ▼
  Request 2 ──────────────────► SUCCESS (dùng nếu R1 chưa về)
```

---

## 6. Polly v8 - Microsoft.Extensions.Http.Resilience

### 6.1 Tổng quan Polly v8

Polly v8 (tích hợp vào `Microsoft.Extensions.Http.Resilience`) là bộ thư viện resilience chính thức của Microsoft cho .NET 8+. API mới hoàn toàn khác Polly v7.

```bash
dotnet add package Microsoft.Extensions.Http.Resilience
dotnet add package Microsoft.Extensions.Resilience
```

### 6.2 Resilience Pipeline cơ bản

```csharp
// Cách tạo ResiliencePipeline thủ công
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.Fallback;

var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    // 1. Fallback - wrap bên ngoài cùng (chạy sau cùng khi tất cả thất bại)
    .AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<Exception>()
            .HandleResult(r => !r.IsSuccessStatusCode),
        FallbackAction = args =>
        {
            var fallbackResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { source = "cache", data = "cached_value" }))
            };
            return ValueTask.FromResult(fallbackResponse);
        },
        OnFallback = args =>
        {
            Console.WriteLine($"Fallback được kích hoạt: {args.Outcome.Exception?.Message}");
            return ValueTask.CompletedTask;
        }
    })
    // 2. Circuit Breaker
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError),
        FailureRatio = 0.5,          // 50% request lỗi → trip
        SamplingDuration = TimeSpan.FromSeconds(10),
        MinimumThroughput = 5,       // Cần ít nhất 5 requests để đánh giá
        BreakDuration = TimeSpan.FromSeconds(30),
        OnOpened = args =>
        {
            Console.WriteLine($"Circuit OPENED! Lỗi: {args.Outcome.Exception?.Message}");
            return ValueTask.CompletedTask;
        },
        OnClosed = args =>
        {
            Console.WriteLine("Circuit CLOSED - Service đã phục hồi");
            return ValueTask.CompletedTask;
        },
        OnHalfOpened = args =>
        {
            Console.WriteLine("Circuit HALF-OPEN - Đang thử nghiệm...");
            return ValueTask.CompletedTask;
        }
    })
    // 3. Retry với exponential backoff + jitter
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests
                            || r.StatusCode >= HttpStatusCode.InternalServerError),
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(500),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,  // Thêm random jitter
        OnRetry = args =>
        {
            Console.WriteLine(
                $"Retry lần {args.AttemptNumber + 1}, chờ {args.RetryDelay.TotalMilliseconds}ms. " +
                $"Lỗi: {args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString()}");
            return ValueTask.CompletedTask;
        }
    })
    // 4. Timeout - wrap trong cùng (áp dụng cho mỗi attempt)
    .AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        OnTimeout = args =>
        {
            Console.WriteLine($"Timeout sau {args.Timeout.TotalSeconds}s");
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

### 6.3 HttpClient với Resilience (cách khuyến nghị)

```csharp
// Cách đơn giản nhất - dùng Standard Resilience Handler
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri("http://product-service");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(options =>
{
    // Standard handler bao gồm: Retry + Circuit Breaker + Timeout + Rate Limiter
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(500);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;

    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    options.CircuitBreaker.MinimumThroughput = 5;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);  // Timeout mỗi attempt
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30); // Timeout tổng
});

// Cách nâng cao - tạo custom pipeline có tên
builder.Services.AddResiliencePipeline<string, HttpResponseMessage>(
    "payment-pipeline",
    (pipelineBuilder, context) =>
    {
        pipelineBuilder
            .AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
            {
                // Fallback cho payment: không nên tự động approve,
                // hãy trả về pending để xử lý offline
                FallbackAction = args => ValueTask.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Accepted)
                    {
                        Content = new StringContent("""{"status":"pending","message":"Payment queued"}""")
                    }),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<BrokenCircuitException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.3,  // Payment service: ngưỡng thấp hơn (30%)
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromMinutes(1), // Ngắt lâu hơn cho payment
                SamplingDuration = TimeSpan.FromSeconds(30)
            })
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2, // Chỉ retry 2 lần cho payment (tránh double charge)
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddTimeout(TimeSpan.FromSeconds(10));
    });
```

### 6.4 Hedge Strategy

```csharp
// Hedge: giảm tail latency bằng cách gửi request dự phòng song song
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>()
    .AddResilienceHandler("product-hedge", pipeline =>
    {
        pipeline.AddHedging(new HedgingStrategyOptions<HttpResponseMessage>
        {
            // Sau 200ms chưa có kết quả → gửi request thứ 2
            Delay = TimeSpan.FromMilliseconds(200),
            MaxHedgedAttempts = 2, // Tối đa 2 request song song
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError),
            ActionGenerator = args =>
            {
                // Có thể gửi đến endpoint khác (backup region)
                return () => args.Callback(args.ActionContext);
            }
        });
    });
```

---

## 7. ShoppingCart Service - Ví dụ hoàn chỉnh

### 7.1 Cấu trúc project

```
ShoppingCart.Api/
├── Checks/
│   ├── LowStockHealthCheck.cs
│   ├── RedisHealthCheck.cs        (custom vì cần check specific keys)
│   └── ExternalServiceHealthCheck.cs
├── Clients/
│   ├── IProductServiceClient.cs
│   ├── ProductServiceClient.cs
│   ├── IPaymentServiceClient.cs
│   └── PaymentServiceClient.cs
├── Models/
│   ├── Cart.cs
│   ├── CartItem.cs
│   └── PaymentRequest.cs
├── Services/
│   └── CartService.cs
├── Publishers/
│   └── HealthCheckMetricsPublisher.cs
└── Program.cs
```

### 7.2 Models

```csharp
// Models/Cart.cs
public record Cart(
    Guid Id,
    string UserId,
    List<CartItem> Items,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CartItem(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    int AvailableStock);

public record PaymentRequest(
    Guid CartId,
    string UserId,
    decimal Amount,
    string Currency,
    string PaymentMethod);

public record PaymentResult(
    Guid TransactionId,
    string Status,        // "succeeded", "pending", "failed"
    string Message,
    bool IsFromFallback);
```

### 7.3 Product Service Client với Resilience

```csharp
// Clients/IProductServiceClient.cs
public interface IProductServiceClient
{
    Task<Product?> GetProductAsync(Guid productId, CancellationToken ct = default);
    Task<IEnumerable<Product>> GetProductsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<bool> CheckStockAsync(Guid productId, int quantity, CancellationToken ct = default);
}

// Clients/ProductServiceClient.cs
public class ProductServiceClient : IProductServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductServiceClient> _logger;
    private readonly IMemoryCache _cache;

    public ProductServiceClient(
        HttpClient httpClient,
        ILogger<ProductServiceClient> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<Product?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        // Thử lấy từ cache trước
        var cacheKey = $"product:{productId}";
        if (_cache.TryGetValue(cacheKey, out Product? cachedProduct))
        {
            _logger.LogDebug("Cache hit cho product {ProductId}", productId);
            return cachedProduct;
        }

        // Resilience được xử lý bởi HttpClient pipeline (cấu hình trong DI)
        var response = await _httpClient.GetAsync($"/api/products/{productId}", ct);
        response.EnsureSuccessStatusCode();

        var product = await response.Content.ReadFromJsonAsync<Product>(cancellationToken: ct);

        if (product != null)
        {
            // Cache 5 phút
            _cache.Set(cacheKey, product, TimeSpan.FromMinutes(5));
        }

        return product;
    }

    public async Task<IEnumerable<Product>> GetProductsByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = string.Join(",", ids);
        var response = await _httpClient.GetAsync($"/api/products?ids={idList}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IEnumerable<Product>>(
            cancellationToken: ct) ?? [];
    }

    public async Task<bool> CheckStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/products/{productId}/stock?quantity={quantity}", ct);
        
        if (!response.IsSuccessStatusCode) return false;
        
        var result = await response.Content.ReadFromJsonAsync<StockCheckResult>(cancellationToken: ct);
        return result?.IsAvailable ?? false;
    }
}

public record Product(Guid Id, string Name, decimal Price, int Stock, bool IsActive);
public record StockCheckResult(bool IsAvailable, int CurrentStock);
```

### 7.4 Payment Service Client với Circuit Breaker và Fallback

```csharp
// Clients/PaymentServiceClient.cs
public class PaymentServiceClient : IPaymentServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline<PaymentResult> _resiliencePipeline;
    private readonly ILogger<PaymentServiceClient> _logger;
    private readonly IMessageQueue _messageQueue; // Để queue payment offline

    public PaymentServiceClient(
        HttpClient httpClient,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<PaymentServiceClient> logger,
        IMessageQueue messageQueue)
    {
        _httpClient = httpClient;
        _resiliencePipeline = pipelineProvider.GetPipeline<PaymentResult>("payment-pipeline");
        _logger = logger;
        _messageQueue = messageQueue;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken ct = default)
    {
        return await _resiliencePipeline.ExecuteAsync(
            async token =>
            {
                _logger.LogInformation(
                    "Đang xử lý payment cho cart {CartId}, amount {Amount}",
                    request.CartId, request.Amount);

                var response = await _httpClient.PostAsJsonAsync(
                    "/api/payments", request, token);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(token);
                    _logger.LogWarning(
                        "Payment service trả về lỗi {StatusCode}: {Error}",
                        response.StatusCode, error);
                    response.EnsureSuccessStatusCode(); // Throw để trigger retry/circuit breaker
                }

                return await response.Content.ReadFromJsonAsync<PaymentResult>(
                    cancellationToken: token)
                    ?? throw new InvalidOperationException("Payment service trả về response rỗng");
            },
            ct);
    }
}

// IPaymentServiceClient.cs
public interface IPaymentServiceClient
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken ct = default);
}
```

### 7.5 Custom Health Checks cho ShoppingCart

```csharp
// Checks/ExternalServiceHealthCheck.cs
// Kiểm tra các external service dependencies
public class ExternalServiceHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _serviceName;
    private readonly string _healthEndpoint;

    public ExternalServiceHealthCheck(
        HttpClient httpClient,
        string serviceName,
        string healthEndpoint)
    {
        _httpClient = httpClient;
        _serviceName = serviceName;
        _healthEndpoint = healthEndpoint;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5)); // Timeout 5s cho health check

            var sw = Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(_healthEndpoint, cts.Token);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                { "service", _serviceName },
                { "endpoint", _healthEndpoint },
                { "status_code", (int)response.StatusCode },
                { "response_time_ms", sw.ElapsedMilliseconds }
            };

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    $"{_serviceName} trả về HTTP {response.StatusCode}",
                    data: data);
            }

            if (sw.ElapsedMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded(
                    $"{_serviceName} phản hồi chậm: {sw.ElapsedMilliseconds}ms",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"{_serviceName} OK ({sw.ElapsedMilliseconds}ms)",
                data);
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                $"{_serviceName} timeout (>5s)",
                data: new Dictionary<string, object> { { "service", _serviceName } });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{_serviceName} không thể kết nối: {ex.Message}",
                ex);
        }
    }
}

// Checks/CartRedisHealthCheck.cs
// Custom Redis check: kiểm tra read/write thực sự (không chỉ ping)
public class CartRedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CartRedisHealthCheck> _logger;

    public CartRedisHealthCheck(
        IConnectionMultiplexer redis,
        ILogger<CartRedisHealthCheck> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var testKey = "health:check:cart";
            var testValue = DateTimeOffset.UtcNow.ToString();

            // Test write
            await db.StringSetAsync(testKey, testValue, TimeSpan.FromSeconds(10));

            // Test read và verify
            var readValue = await db.StringGetAsync(testKey);
            if (readValue != testValue)
            {
                return HealthCheckResult.Unhealthy(
                    "Redis read/write không nhất quán");
            }

            // Cleanup
            await db.KeyDeleteAsync(testKey);

            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync("server");
            var uptimeSection = info.FirstOrDefault(x => x.Key == "server");

            return HealthCheckResult.Healthy(
                "Redis hoạt động bình thường - read/write OK",
                new Dictionary<string, object>
                {
                    { "connected_clients", _redis.GetCounters().TotalOutstanding },
                    { "endpoint", _redis.GetEndPoints().First().ToString() ?? "unknown" }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check thất bại");
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Redis không khả dụng: {ex.Message}",
                ex);
        }
    }
}
```

### 7.6 Program.cs hoàn chỉnh

```csharp
// Program.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Polly.Fallback;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ═══════════════════════════════════════════════════════════════
// INFRASTRUCTURE SERVICES
// ═══════════════════════════════════════════════════════════════

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(config.GetConnectionString("Redis")!));

// Memory Cache (cho client-side caching)
builder.Services.AddMemoryCache();

// ═══════════════════════════════════════════════════════════════
// HTTP CLIENTS VỚI RESILIENCE
// ═══════════════════════════════════════════════════════════════

// Product Service Client - dùng Standard Resilience Handler
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(config["Services:ProductService:BaseUrl"]
        ?? "http://product-service");
    client.DefaultRequestHeaders.Add("X-Service-Name", "shopping-cart");
})
.AddStandardResilienceHandler(options =>
{
    // Retry: 3 lần với exponential backoff + jitter
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(500);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.Retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
        .Handle<HttpRequestException>()
        .Handle<TimeoutRejectedException>()
        .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests
                        || (int)r.StatusCode >= 500);

    // Circuit Breaker: ngắt khi 50% request lỗi
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 5;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    // Timeout
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
});

// Payment Service Client - custom pipeline với fallback đặc biệt
builder.Services.AddHttpClient<IPaymentServiceClient, PaymentServiceClient>(client =>
{
    client.BaseAddress = new Uri(config["Services:PaymentService:BaseUrl"]
        ?? "http://payment-service");
});

// Đăng ký custom resilience pipeline cho payment
builder.Services.AddResiliencePipeline<string, PaymentResult>(
    "payment-pipeline",
    (pipelineBuilder, context) =>
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILogger<PaymentServiceClient>>();
        var messageQueue = context.ServiceProvider
            .GetRequiredService<IMessageQueue>();

        pipelineBuilder
            // Fallback: queue payment để xử lý sau
            .AddFallback(new FallbackStrategyOptions<PaymentResult>
            {
                ShouldHandle = new PredicateBuilder<PaymentResult>()
                    .Handle<BrokenCircuitException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<HttpRequestException>(),
                FallbackAction = async args =>
                {
                    logger.LogWarning(
                        "Payment service không khả dụng, đang queue payment để xử lý sau");

                    // Queue vào message queue để xử lý offline
                    var pendingId = Guid.NewGuid();
                    // await messageQueue.PublishAsync("payment.pending", pendingId);

                    return new PaymentResult(
                        pendingId,
                        "pending",
                        "Payment đang được xử lý, bạn sẽ nhận thông báo qua email",
                        IsFromFallback: true);
                },
                OnFallback = args =>
                {
                    logger.LogError(
                        args.Outcome.Exception,
                        "Payment fallback được kích hoạt");
                    return ValueTask.CompletedTask;
                }
            })
            // Circuit Breaker: ngưỡng thấp hơn cho payment (30%)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<PaymentResult>
            {
                FailureRatio = 0.3,
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromMinutes(1),
                OnOpened = args =>
                {
                    logger.LogCritical(
                        "Payment service Circuit Breaker OPENED! " +
                        "Duration: {BreakDuration}", args.BreakDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("Payment service Circuit Breaker CLOSED - Phục hồi OK");
                    return ValueTask.CompletedTask;
                }
            })
            // Retry: chỉ 2 lần cho payment (tránh double-charge)
            .AddRetry(new RetryStrategyOptions<PaymentResult>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<PaymentResult>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Payment retry lần {Attempt}, chờ {Delay}ms",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                OnTimeout = args =>
                {
                    logger.LogError("Payment request timeout sau {Timeout}s", args.Timeout.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            });
    });

// ═══════════════════════════════════════════════════════════════
// HEALTH CHECKS
// ═══════════════════════════════════════════════════════════════
builder.Services.AddHealthChecks()
    // === LIVENESS: Chỉ check app còn sống không ===
    .AddCheck("self", () => HealthCheckResult.Healthy("App đang chạy"),
        tags: new[] { "live", "startup" })

    // === STARTUP: Check sau khi app init xong ===
    .AddCheck<StartupHealthCheck>("startup_tasks",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "startup" })

    // === READINESS: Check tất cả dependencies ===
    // SQL Server
    .AddSqlServer(
        connectionString: config.GetConnectionString("DefaultConnection")!,
        healthQuery: "SELECT 1",
        name: "sqlserver",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "db", "ready" })

    // Redis (custom check)
    .AddCheck<CartRedisHealthCheck>(
        name: "redis",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "cache", "ready" })

    // RabbitMQ
    .AddRabbitMQ(
        rabbitConnectionString: config.GetConnectionString("RabbitMQ")!,
        name: "rabbitmq",
        failureStatus: HealthStatus.Degraded, // Degraded vì có thể queue offline
        tags: new[] { "messaging", "ready" })

    // External Services
    .AddUrlGroup(
        new Uri($"{config["Services:ProductService:BaseUrl"]}/health"),
        name: "product_service",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "external", "ready" })

    .AddUrlGroup(
        new Uri($"{config["Services:PaymentService:BaseUrl"]}/health"),
        name: "payment_service",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "external", "ready" })

    // Business Logic Check
    .AddCheck<LowStockHealthCheck>(
        name: "low_stock_warning",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "business", "ready" })

    // Infrastructure
    .AddDiskStorageHealthCheck(setup =>
        setup.AddDrive("/", minimumFreeMegabytes: 512),
        name: "disk_space",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "infrastructure" });

// Health Check UI
builder.Services
    .AddHealthChecksUI(options =>
    {
        options.SetEvaluationTimeInSeconds(15);
        options.MaximumHistoryEntriesPerEndpoint(50);
        options.AddHealthCheckEndpoint("ShoppingCart", "/health");
    })
    .AddInMemoryStorage();

// Health Check Publisher
builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(5);
    options.Period = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IHealthCheckPublisher, HealthCheckMetricsPublisher>();

// ═══════════════════════════════════════════════════════════════
// APPLICATION SERVICES
// ═══════════════════════════════════════════════════════════════
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddSingleton<StartupHealthCheck>();

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════
// HEALTH CHECK ENDPOINTS (Kubernetes probes)
// ═══════════════════════════════════════════════════════════════

// Startup Probe: kiểm tra app đã init xong chưa
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).RequireHost("*"); // Cho phép tất cả hosts (quan trọng trong container)

// Liveness Probe: app còn sống không
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK, // Degraded vẫn "sống"
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Readiness Probe: sẵn sàng nhận traffic không
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK, // Vẫn nhận traffic dù degraded
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Tổng quan (cho dashboard và ops team)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Health Check UI Dashboard
app.MapHealthChecksUI(options => options.UIPath = "/healthchecks-ui");

app.MapControllers();
app.Run();
```

### 7.7 Startup Health Check

```csharp
// Checks/StartupHealthCheck.cs
// Track xem app đã hoàn thành initialization chưa
public class StartupHealthCheck : IHealthCheck
{
    private volatile bool _isReady = false;
    private string _statusMessage = "Đang khởi động...";

    public void SetReady(string message = "Khởi động hoàn tất")
    {
        _statusMessage = message;
        _isReady = true;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_isReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy(_statusMessage));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(_statusMessage));
    }
}

// Trong Program.cs hoặc Hosted Service, gọi khi init xong:
// var startupCheck = app.Services.GetRequiredService<StartupHealthCheck>();
// startupCheck.SetReady("Database migrations xong, cache warm up hoàn tất");
```

### 7.8 Cart Service sử dụng Resilient Clients

```csharp
// Services/CartService.cs
public class CartService : ICartService
{
    private readonly IProductServiceClient _productClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CartService> _logger;

    public CartService(
        IProductServiceClient productClient,
        IPaymentServiceClient paymentClient,
        IConnectionMultiplexer redis,
        ILogger<CartService> logger)
    {
        _productClient = productClient;
        _paymentClient = paymentClient;
        _redis = redis;
        _logger = logger;
    }

    public async Task<Cart> AddItemToCartAsync(
        string userId,
        Guid productId,
        int quantity,
        CancellationToken ct = default)
    {
        // Lấy thông tin product (có retry + circuit breaker qua HttpClient pipeline)
        var product = await _productClient.GetProductAsync(productId, ct)
            ?? throw new ProductNotFoundException(productId);

        if (!product.IsActive)
            throw new ProductNotAvailableException(productId);

        // Kiểm tra tồn kho
        var hasStock = await _productClient.CheckStockAsync(productId, quantity, ct);
        if (!hasStock)
            throw new InsufficientStockException(productId, quantity);

        // Lấy cart hiện tại từ Redis
        var cart = await GetOrCreateCartAsync(userId, ct);

        // Thêm item
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            cart = cart with
            {
                Items = cart.Items
                    .Select(i => i.ProductId == productId
                        ? i with { Quantity = i.Quantity + quantity }
                        : i)
                    .ToList()
            };
        }
        else
        {
            cart = cart with
            {
                Items = [.. cart.Items, new CartItem(
                    productId, product.Name, quantity, product.Price, product.Stock)]
            };
        }

        // Tính tổng tiền và lưu
        var total = cart.Items.Sum(i => i.Quantity * i.UnitPrice);
        cart = cart with
        {
            TotalAmount = total,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await SaveCartAsync(cart, ct);

        _logger.LogInformation(
            "Đã thêm {Quantity}x {ProductName} vào cart của user {UserId}",
            quantity, product.Name, userId);

        return cart;
    }

    public async Task<PaymentResult> CheckoutAsync(
        string userId,
        string paymentMethod,
        CancellationToken ct = default)
    {
        var cart = await GetCartAsync(userId, ct)
            ?? throw new CartNotFoundException(userId);

        if (!cart.Items.Any())
            throw new EmptyCartException(userId);

        var paymentRequest = new PaymentRequest(
            cart.Id,
            userId,
            cart.TotalAmount,
            "VND",
            paymentMethod);

        // Payment client có circuit breaker + fallback built-in
        var result = await _paymentClient.ProcessPaymentAsync(paymentRequest, ct);

        if (result.IsFromFallback)
        {
            _logger.LogWarning(
                "Checkout của user {UserId} dùng fallback payment. TransactionId: {TxId}",
                userId, result.TransactionId);
        }
        else if (result.Status == "succeeded")
        {
            // Xóa cart sau khi thanh toán thành công
            await ClearCartAsync(userId, ct);
        }

        return result;
    }

    private async Task<Cart> GetOrCreateCartAsync(string userId, CancellationToken ct)
    {
        var existing = await GetCartAsync(userId, ct);
        return existing ?? new Cart(
            Guid.NewGuid(), userId, [], 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private async Task<Cart?> GetCartAsync(string userId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"cart:{userId}");
        return json.HasValue ? JsonSerializer.Deserialize<Cart>(json!) : null;
    }

    private async Task SaveCartAsync(Cart cart, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(cart);
        await db.StringSetAsync($"cart:{cart.UserId}", json, TimeSpan.FromDays(7));
    }

    private async Task ClearCartAsync(string userId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"cart:{userId}");
    }
}
```

### 7.9 Kubernetes Deployment YAML

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: shopping-cart-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: shopping-cart-api
  template:
    metadata:
      labels:
        app: shopping-cart-api
    spec:
      containers:
      - name: shopping-cart-api
        image: myregistry/shopping-cart-api:latest
        ports:
        - containerPort: 8080
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: db-secrets
              key: connection-string
        
        # Startup Probe: chờ tối đa 5 phút (30 failures × 10s interval)
        startupProbe:
          httpGet:
            path: /health/startup
            port: 8080
          failureThreshold: 30
          periodSeconds: 10
          initialDelaySeconds: 10
        
        # Liveness Probe: kiểm tra mỗi 10s, fail 3 lần → restart
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 0  # Startup probe đã chạy trước
          periodSeconds: 10
          failureThreshold: 3
          successThreshold: 1
          timeoutSeconds: 5
        
        # Readiness Probe: kiểm tra mỗi 5s
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 0
          periodSeconds: 5
          failureThreshold: 3
          successThreshold: 1
          timeoutSeconds: 5
        
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

### 7.10 Integration Tests cho Resilience

```csharp
// Tests/ResilienceIntegrationTests.cs
public class ResilienceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ResilienceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProductClient_ShouldRetry_WhenServiceReturns503()
    {
        // Arrange: mock product service trả 503 2 lần, lần 3 trả 200
        var callCount = 0;
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Product(
                    Guid.NewGuid(), "Test Product", 100, 50, true))
            };
        });

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace HttpClient với mock
                services.AddHttpClient<IProductServiceClient, ProductServiceClient>()
                    .ConfigurePrimaryHttpMessageHandler(_ => mockHandler);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, callCount); // Phải gọi đúng 3 lần (2 fail + 1 success)
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpen_AfterConsecutiveFailures()
    {
        // Arrange: mock product service luôn trả 500
        var callCount = 0;
        var mockHandler = new MockHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IProductServiceClient, ProductServiceClient>()
                    .ConfigurePrimaryHttpMessageHandler(_ => mockHandler);
            });
        });

        var client = factory.CreateClient();

        // Act: gửi đủ request để trip circuit breaker
        for (int i = 0; i < 10; i++)
        {
            await client.GetAsync($"/api/products/{Guid.NewGuid()}");
        }

        var callCountAfterTrip = callCount;

        // Sau khi circuit mở, thêm request không nên gọi xuống service nữa
        await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        // Assert: circuit breaker đang open, không gọi thêm
        Assert.Equal(callCountAfterTrip, callCount);
    }

    [Fact]
    public async Task PaymentClient_ShouldFallback_WhenCircuitOpen()
    {
        // Arrange: payment service không khả dụng
        var mockHandler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IPaymentServiceClient, PaymentServiceClient>()
                    .ConfigurePrimaryHttpMessageHandler(_ => mockHandler);
            });
        });

        var client = factory.CreateClient();

        // Act: thực hiện checkout
        var checkoutResponse = await client.PostAsJsonAsync("/api/cart/checkout", new
        {
            UserId = "user123",
            PaymentMethod = "card"
        });

        // Assert: vẫn trả 200 (fallback) với status "pending"
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        var result = await checkoutResponse.Content.ReadFromJsonAsync<PaymentResult>();
        Assert.NotNull(result);
        Assert.Equal("pending", result.Status);
        Assert.True(result.IsFromFallback);
    }

    [Fact]
    public async Task HealthCheck_Live_ShouldReturn200_WhenAppRunning()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// Helper class
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}
```

### 7.11 Health Check Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                  Health Check Flow - ShoppingCart                   │
│                                                                     │
│  Kubernetes                  ShoppingCart API                       │
│  ──────────                  ──────────────────                     │
│                                                                     │
│  [Pod Start]                                                        │
│      │                                                              │
│      │ GET /health/startup                                          │
│      ├──────────────────────►  Check: startup_tasks                 │
│      │                         • DB migrations done?               │
│      │                         • Cache warmed?                      │
│      │◄──────────────────────  200 OK / 503                         │
│      │                                                              │
│      │ (every 10s)                                                  │
│      │ GET /health/live                                             │
│      ├──────────────────────►  Check: "self" (always healthy)       │
│      │◄──────────────────────  200 OK                               │
│      │                                                              │
│      │ (every 5s)                                                   │
│      │ GET /health/ready                                            │
│      ├──────────────────────►  Check all:                           │
│      │                         • sqlserver (Unhealthy → 503)        │
│      │                         • redis (Unhealthy → 503)            │
│      │                         • rabbitmq (Degraded → 200)          │
│      │                         • product_service (Degraded → 200)   │
│      │                         • payment_service (Degraded → 200)   │
│      │                         • low_stock_warning (Degraded → 200) │
│      │◄──────────────────────  200 OK (khi tất cả ≥ Degraded)      │
│      │                         503 (khi có Unhealthy)               │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 8. Chaos Engineering

### 8.1 Chaos Engineering là gì?

**Chaos Engineering** là phương pháp thực nghiệm trên hệ thống phân tán, cố tình đưa vào các sự cố (failures) có kiểm soát để khám phá điểm yếu trước khi chúng gây ra sự cố thực tế ở production.

> "Break things on purpose to make systems more resilient"

### 8.2 Quy trình Chaos Engineering

```
┌─────────────────────────────────────────────────────────────┐
│              Vòng lặp Chaos Engineering                     │
│                                                             │
│  1. DEFINE STEADY STATE                                     │
│     Xác định "trạng thái bình thường" của hệ thống         │
│     Ví dụ: p99 latency < 200ms, error rate < 0.1%          │
│                    │                                        │
│                    ▼                                        │
│  2. HYPOTHESIZE                                             │
│     "Nếu Redis bị chậm thêm 500ms, checkout rate          │
│      vẫn giữ nguyên do có circuit breaker và fallback"     │
│                    │                                        │
│                    ▼                                        │
│  3. INTRODUCE CHAOS (môi trường staging/production)         │
│     Inject fault có kiểm soát vào hệ thống                 │
│                    │                                        │
│                    ▼                                        │
│  4. OBSERVE & MEASURE                                       │
│     So sánh với steady state. Hệ thống có hold không?      │
│                    │                                        │
│                    ▼                                        │
│  5. FIX & LEARN                                             │
│     Sửa điểm yếu phát hiện được. Cập nhật runbook.        │
│                    │                                        │
│                    └─────────────────────────────────┐      │
│                                              Lặp lại │      │
└──────────────────────────────────────────────────────┘      │
```

### 8.3 Các công cụ Chaos Engineering phổ biến

| Công cụ | Platform | Mô tả |
|---------|----------|-------|
| **Chaos Monkey** | AWS, Kubernetes | Netflix's original tool - ngẫu nhiên kill instances |
| **LitmusChaos** | Kubernetes | CNCF project, chaos experiments dưới dạng CRDs |
| **Chaos Mesh** | Kubernetes | Fault injection toàn diện (network, pod, I/O) |
| **Gremlin** | Multi-cloud | SaaS platform, dễ dùng, có trả phí |
| **Azure Chaos Studio** | Azure | Fault injection cho Azure services |

### 8.4 Chaos Experiments thường gặp

```csharp
// Mô phỏng chaos với .NET Aspire (hoặc middleware)
// Trong development/staging, thêm fault injection middleware

public class ChaosMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ChaosOptions _options;

    public ChaosMiddleware(RequestDelegate next, IOptions<ChaosOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // Inject latency (mô phỏng network chậm)
        if (_options.LatencyMs > 0 && Random.Shared.NextDouble() < _options.LatencyProbability)
        {
            await Task.Delay(_options.LatencyMs);
        }

        // Inject fault (mô phỏng service lỗi)
        if (_options.FaultProbability > 0 && Random.Shared.NextDouble() < _options.FaultProbability)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Chaos: Service unavailable");
            return;
        }

        await _next(context);
    }
}

public class ChaosOptions
{
    public bool Enabled { get; set; }
    public int LatencyMs { get; set; } = 500;
    public double LatencyProbability { get; set; } = 0.1; // 10%
    public double FaultProbability { get; set; } = 0.05;   // 5%
}

// Đăng ký (chỉ trong dev/staging)
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseMiddleware<ChaosMiddleware>();
}
```

### 8.5 LitmusChaos Experiment mẫu (Kubernetes)

```yaml
# litmus-pod-delete.yaml
# Ngẫu nhiên xóa pods của shopping-cart-api mỗi 5 phút
apiVersion: litmuschaos.io/v1alpha1
kind: ChaosEngine
metadata:
  name: shopping-cart-chaos
spec:
  appinfo:
    appns: production
    applabel: "app=shopping-cart-api"
    appkind: deployment
  engineState: "active"
  chaosServiceAccount: litmus-admin
  experiments:
  - name: pod-delete
    spec:
      components:
        env:
        - name: TOTAL_CHAOS_DURATION
          value: "300"  # 5 phút
        - name: CHAOS_INTERVAL
          value: "60"   # Xóa pod mỗi 60 giây
        - name: FORCE
          value: "false"
        - name: PODS_AFFECTED_PERC
          value: "33"   # Kill 33% pods
```

---

## 9. SLA, SLO, SLI và Error Budgets

### 9.1 Định nghĩa

**SLI (Service Level Indicator)** - Chỉ số đo lường thực tế:
- Tỷ lệ request thành công (success rate)
- Latency phân vị (p50, p95, p99)
- Availability (% thời gian hệ thống hoạt động)

**SLO (Service Level Objective)** - Mục tiêu nội bộ:
- "99.9% requests thành công trong 30 ngày"
- "p99 latency < 500ms"

**SLA (Service Level Agreement)** - Cam kết với khách hàng (có penalty):
- "99.5% uptime, nếu vi phạm hoàn tiền 10%"

### 9.2 Error Budget

```
┌─────────────────────────────────────────────────────────────┐
│                    Error Budget                             │
│                                                             │
│  SLO: 99.9% availability trong 30 ngày                     │
│                                                             │
│  Tổng thời gian: 30 × 24 × 60 = 43,200 phút               │
│  Cho phép downtime: (1 - 0.999) × 43,200 = 43.2 phút      │
│                                                             │
│  Error Budget = 43.2 phút/tháng                             │
│                                                             │
│  ████████████████████████░░░░  70% còn lại                  │
│  │─────────────── 30.24 min đã dùng ──│                    │
│                                                             │
│  Khi Error Budget cạn:                                      │
│  → STOP mọi feature deployment                              │
│  → Tập trung 100% vào reliability                           │
│  → Review toàn bộ change management                         │
└─────────────────────────────────────────────────────────────┘
```

### 9.3 Tính toán Availability

```csharp
// Helpers/SloCalculator.cs
public static class SloCalculator
{
    // Tính error budget còn lại
    public static TimeSpan CalculateErrorBudget(
        double sloPercentage,      // vd: 99.9
        TimeSpan period,           // vd: 30 ngày
        TimeSpan downtimeUsed)     // thời gian đã dùng
    {
        var allowedDowntime = period * (1 - sloPercentage / 100.0);
        var remaining = allowedDowntime - downtimeUsed;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    // Tính availability từ số lượng request
    public static double CalculateAvailability(long successRequests, long totalRequests)
    {
        if (totalRequests == 0) return 100.0;
        return (double)successRequests / totalRequests * 100.0;
    }

    // Nines of availability
    // 99%    = 3.65 ngày downtime/năm
    // 99.9%  = 8.77 giờ downtime/năm
    // 99.95% = 4.38 giờ downtime/năm
    // 99.99% = 52.6 phút downtime/năm
    public static TimeSpan AnnualDowntime(double availabilityPercent)
    {
        var unavailability = 1 - availabilityPercent / 100.0;
        return TimeSpan.FromDays(365) * unavailability;
    }
}
```

### 9.4 Monitoring SLOs với custom metrics

```csharp
// Middleware/SloTrackingMiddleware.cs
// Đo lường SLI để tính SLO
public class SloTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SloTrackingMiddleware> _logger;

    // Counters (trong thực tế dùng Prometheus metrics)
    private static long _totalRequests = 0;
    private static long _successRequests = 0;
    private static readonly List<double> _latencies = [];

    public SloTrackingMiddleware(RequestDelegate next, ILogger<SloTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bỏ qua health check endpoints khỏi SLI
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        try
        {
            await _next(context);
            sw.Stop();

            if (context.Response.StatusCode < 500)
            {
                Interlocked.Increment(ref _successRequests);
            }

            // Track latency
            lock (_latencies)
            {
                _latencies.Add(sw.Elapsed.TotalMilliseconds);
                // Giữ 1000 measurements gần nhất
                if (_latencies.Count > 1000)
                    _latencies.RemoveAt(0);
            }

            // Log nếu request chậm (SLI vi phạm)
            if (sw.Elapsed.TotalMilliseconds > 500)
            {
                _logger.LogWarning(
                    "Slow request: {Method} {Path} took {Ms}ms (SLO threshold: 500ms)",
                    context.Request.Method,
                    context.Request.Path,
                    sw.Elapsed.TotalMilliseconds);
            }
        }
        catch
        {
            sw.Stop();
            throw;
        }
    }

    public static (double availability, double p50, double p95, double p99) GetCurrentMetrics()
    {
        var availability = _totalRequests > 0
            ? (double)_successRequests / _totalRequests * 100
            : 100.0;

        double p50, p95, p99;
        lock (_latencies)
        {
            if (!_latencies.Any()) return (availability, 0, 0, 0);
            var sorted = _latencies.Order().ToList();
            p50 = sorted[(int)(sorted.Count * 0.50)];
            p95 = sorted[(int)(sorted.Count * 0.95)];
            p99 = sorted[(int)(sorted.Count * 0.99)];
        }

        return (availability, p50, p95, p99);
    }
}
```

---

## 10. Best Practices

### 10.1 Health Checks Best Practices

```
✅ NÊN làm:
  • Giữ health check nhanh (< 1 giây) - đừng làm heavy computation
  • Phân biệt rõ Liveness vs Readiness vs Startup probes
  • Trả về data hữu ích (response time, connection count, v.v.)
  • Dùng tags để phân loại checks
  • Test health check endpoints trong integration tests
  • Có fallback khi health check endpoint bị lỗi (không crash app)
  • Dùng Degraded thay vì Unhealthy khi service vẫn hoạt động được

❌ KHÔNG nên:
  • Check những thứ không thể fix tự động (dùng để alert, không restart)
  • Để health check timeout gây chậm app
  • Expose sensitive information trong health check response
  • Dùng cùng một endpoint cho Liveness và Readiness
  • Block Readiness probe chỉ vì một non-critical dependency fail
```

### 10.2 Resilience Best Practices

```
✅ Retry Pattern:
  • Luôn dùng exponential backoff với jitter
  • Set retry count hợp lý (3-5 lần, không phải 100)
  • Chỉ retry transient errors (5xx, timeout) - không retry 4xx
  • Với payment: retry ít hơn, cẩn thận hơn (tránh double charge)
  • Respect Retry-After header từ server

✅ Circuit Breaker:
  • Tune threshold phù hợp với workload (không quá nhạy, không quá chậm)
  • Log rõ khi circuit open/close
  • Pair với Fallback để có graceful degradation
  • Monitor circuit state như một metric quan trọng

✅ Timeout:
  • Set timeout ở mọi I/O operation
  • Timeout per-attempt (trong retry) + total timeout
  • Timeout nên nhỏ hơn client's timeout (tránh cascade)

✅ Bulkhead:
  • Phân tách thread/connection pool cho các services khác nhau
  • Critical services (payment) cần nhiều resources hơn
  • Dùng với Rate Limiter để bảo vệ resources

✅ Fallback:
  • Trả về cached data (stale nhưng vẫn có ích)
  • Degrade gracefully (show partial data)
  • Queue operation để xử lý sau (async fallback)
  • Thông báo rõ cho user biết đang ở degraded mode
```

### 10.3 Cấu hình khuyến nghị cho Production

```csharp
// appsettings.Production.json - template cấu hình production
{
  "HealthChecks": {
    "LowStockThreshold": 10,
    "CriticalStockThreshold": 3
  },
  "Resilience": {
    "ProductService": {
      "MaxRetryAttempts": 3,
      "BaseDelayMs": 500,
      "CircuitBreakerFailureRatio": 0.5,
      "CircuitBreakerBreakDurationSeconds": 30,
      "AttemptTimeoutSeconds": 5,
      "TotalTimeoutSeconds": 30
    },
    "PaymentService": {
      "MaxRetryAttempts": 2,
      "BaseDelayMs": 1000,
      "CircuitBreakerFailureRatio": 0.3,
      "CircuitBreakerBreakDurationSeconds": 60,
      "AttemptTimeoutSeconds": 10,
      "TotalTimeoutSeconds": 45
    }
  },
  "Chaos": {
    "Enabled": false
  }
}
```

### 10.4 Checklist trước khi deploy

```
Pre-deployment Resilience Checklist:
□ Tất cả HttpClient đều có resilience pipeline (retry + circuit breaker + timeout)
□ Health check endpoints /health/live, /health/ready, /health/startup hoạt động
□ Kubernetes deployment YAML có đủ 3 loại probes
□ Circuit breaker state được log và monitor
□ Fallback behavior được test (unit test + integration test)
□ Timeout được set ở tất cả I/O operations
□ Chaos engineering test đã chạy ở staging
□ Runbook cho các failure scenarios đã được viết
□ Alert đã được set cho SLO vi phạm
□ Error budget dashboard đã có
```

---

## Tổng kết

Trong bài này chúng ta đã học:

| Chủ đề | Key Takeaway |
|--------|-------------|
| **Liveness Probe** | Kill và restart Pod khi app bị deadlock/crash |
| **Readiness Probe** | Remove khỏi load balancer khi chưa sẵn sàng (không kill!) |
| **Startup Probe** | Đặc biệt cho app chậm khởi động |
| **Custom IHealthCheck** | Implement business-specific health checks |
| **Retry + Jitter** | Tránh thundering herd, chỉ retry transient errors |
| **Circuit Breaker** | Fail fast, bảo vệ downstream services |
| **Fallback** | Graceful degradation thay vì complete failure |
| **Polly v8** | `AddStandardResilienceHandler()` + custom pipeline |
| **Chaos Engineering** | Break intentionally to improve resilience |
| **SLO/Error Budget** | Data-driven reliability decisions |

> **Nguyên tắc vàng**: Một hệ thống resilient không phải là hệ thống không bao giờ lỗi, mà là hệ thống **phục hồi nhanh và gracefully** khi lỗi xảy ra.

---

## Tài liệu tham khảo

- [ASP.NET Core Health Checks Docs](https://docs.microsoft.com/aspnet/core/host-and-deploy/health-checks)
- [Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/dotnet/core/resilience/)
- [Polly v8 Documentation](https://github.com/App-vNext/Polly)
- [Google SRE Book - SLOs](https://sre.google/sre-book/service-level-objectives/)
- [LitmusChaos Documentation](https://litmuschaos.io/)
- [Kubernetes Probe Configuration](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/)
