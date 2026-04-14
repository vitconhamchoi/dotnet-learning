# Observability at Scale: OpenTelemetry, Structured Logging và Distributed Tracing

## 1. Tại sao Observability là bắt buộc cho hệ thống phức tạp

Trong hệ thống microservices với 20+ service, khi có incident xảy ra lúc 3 giờ sáng, câu hỏi đầu tiên là: "Cái gì đang sai?" Nếu không có observability tốt, bạn sẽ phải:
- SSH vào từng server xem log
- Không biết request đi qua service nào
- Không có metric để correlate với incident
- Mất hàng giờ để tìm root cause

**Observability** có ba trụ cột (Three Pillars of Observability):

1. **Metrics**: số liệu aggregate theo thời gian (request rate, error rate, latency p50/p95/p99, CPU, memory)
2. **Logs**: sự kiện rời rạc có context (structured logs, request logs, error logs)
3. **Traces**: journey của một request qua nhiều service (distributed traces, spans)

**OpenTelemetry** là standard mở cho observability, giúp bạn instrument một lần và export tới nhiều backend (Jaeger, Prometheus, Grafana, Datadog, New Relic...).

---

## 2. Setup OpenTelemetry trong .NET

```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.SqlClient
dotnet add package OpenTelemetry.Instrumentation.StackExchangeRedis
dotnet add package Npgsql.OpenTelemetry
```

### 2.1 Setup toàn bộ trong ServiceDefaults (Aspire style)

```csharp
// ServiceDefaults/Extensions.cs - tập trung tất cả observability defaults
public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddObservability(
        this IHostApplicationBuilder builder, 
        string serviceName)
    {
        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";
        var serviceVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName);
                resource.AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["service.region"] = Environment.GetEnvironmentVariable("REGION") ?? "unknown"
                });
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        // Filter ra health checks để không spam traces
                        opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        // Filter ra outgoing health checks
                        opts.FilterHttpRequestMessage = msg =>
                            !msg.RequestUri?.AbsolutePath.Contains("/health") == true;
                    })
                    .AddNpgsql()           // PostgreSQL traces
                    .AddRedisInstrumentation()  // Redis traces
                    .AddSource("MassTransit")   // MassTransit traces
                    .AddSource("Orleans")       // Orleans traces
                    .AddSource(serviceName)     // Custom traces
                    .AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));

                if (builder.Environment.IsDevelopment())
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()   // GC, thread pool, memory
                    .AddProcessInstrumentation()    // CPU, memory process
                    .AddMeter("MassTransit")
                    .AddMeter(serviceName)          // Custom meters
                    .AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint))
                    .AddPrometheusExporter();       // Prometheus /metrics endpoint

                if (builder.Environment.IsDevelopment())
                {
                    metrics.AddConsoleExporter();
                }
            });

        return builder;
    }
}
```

---

## 3. Structured Logging với Serilog

Structured logging lưu log như data có structure, không phải plain text. Điều này cho phép query, filter và aggregate log.

```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.OpenTelemetry
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Enrichers.Environment
dotnet add package Serilog.Enrichers.Thread
dotnet add package Serilog.Enrichers.Process
```

```csharp
// Program.cs - Setup Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("ServiceName", "order-service")
    .Enrich.WithProperty("ServiceVersion", Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown")
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)
    .WriteTo.OpenTelemetry(opts =>
    {
        opts.Endpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";
        opts.Protocol = OtlpProtocol.Grpc;
    })
    .CreateLogger();

builder.Host.UseSerilog();
```

### 3.1 Log với proper context

```csharp
// Xấu: log string interpolation - mất structure
logger.LogInformation($"Order {orderId} placed by user {userId} for amount {amount}");

// Tốt: structured logging với named properties
logger.LogInformation(
    "Order {OrderId} placed by user {UserId} for amount {Amount:C}",
    orderId, userId, amount);

// Tốt hơn: dùng LoggerMessage source generator (performance tốt nhất)
public partial class OrderHandler
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Order {OrderId} placed by {UserId} for {Amount:C}")]
    private static partial void LogOrderPlaced(
        ILogger logger, Guid orderId, Guid userId, decimal amount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Order {OrderId} failed validation: {ValidationErrors}")]
    private static partial void LogOrderValidationFailed(
        ILogger logger, Guid orderId, string validationErrors);
}
```

### 3.2 Correlation ID và Request Context

```csharp
// Middleware tự động gắn correlation ID vào tất cả logs trong request
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers["X-Correlation-Id"].ToString();
        if (string.IsNullOrEmpty(correlationId))
            correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        ctx.Response.Headers["X-Correlation-Id"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", ctx.Request.Path))
        using (LogContext.PushProperty("UserId", ctx.User.FindFirst("sub")?.Value ?? "anonymous"))
        {
            await _next(ctx);
        }
    }
}
```

---

## 4. Custom Metrics

```csharp
// Define metrics bằng .NET Meter API
public class OrderMetrics
{
    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersFailed;
    private readonly Histogram<double> _orderValue;
    private readonly Histogram<double> _checkoutDuration;
    private readonly UpDownCounter<int> _activeCheckouts;
    private readonly ObservableGauge<int> _pendingOrderCount;

    public OrderMetrics(IMeterFactory meterFactory, IOrderRepository repo)
    {
        var meter = meterFactory.Create("OrderService", "1.0.0");

        _ordersPlaced = meter.CreateCounter<long>(
            "orders.placed.total",
            unit: "{orders}",
            description: "Total number of orders placed");

        _ordersFailed = meter.CreateCounter<long>(
            "orders.failed.total",
            unit: "{orders}",
            description: "Total number of failed orders");

        _orderValue = meter.CreateHistogram<double>(
            "orders.value",
            unit: "USD",
            description: "Distribution of order values");

        _checkoutDuration = meter.CreateHistogram<double>(
            "checkout.duration",
            unit: "ms",
            description: "Time to complete checkout process");

        _activeCheckouts = meter.CreateUpDownCounter<int>(
            "checkout.active",
            description: "Number of checkouts currently in progress");

        // Observable gauge: đọc từ repository
        _pendingOrderCount = meter.CreateObservableGauge<int>(
            "orders.pending.count",
            () => new Measurement<int>(repo.CountPendingAsync().GetAwaiter().GetResult()),
            description: "Current number of pending orders");
    }

    public void RecordOrderPlaced(decimal amount, string paymentMethod, string region)
    {
        var tags = new TagList
        {
            { "payment_method", paymentMethod },
            { "region", region }
        };
        
        _ordersPlaced.Add(1, tags);
        _orderValue.Record((double)amount, tags);
    }

    public void RecordOrderFailed(string reason)
    {
        _ordersFailed.Add(1, new TagList { { "failure_reason", reason } });
    }

    public IDisposable TrackCheckout()
    {
        _activeCheckouts.Add(1);
        var sw = Stopwatch.StartNew();
        
        return new CheckoutTracker(() =>
        {
            sw.Stop();
            _checkoutDuration.Record(sw.ElapsedMilliseconds);
            _activeCheckouts.Add(-1);
        });
    }
}

// Đăng ký
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddSingleton<IMeterFactory>(sp => sp.GetRequiredService<IMeterFactory>());
```

---

## 5. Custom Distributed Traces

```csharp
// Activity source cho custom tracing
public class OrderTracing
{
    private static readonly ActivitySource _source = new("OrderService", "1.0.0");

    public static Activity? StartProcessOrder(Guid orderId, Guid customerId)
    {
        var activity = _source.StartActivity("ProcessOrder", ActivityKind.Internal);
        activity?.SetTag("order.id", orderId.ToString());
        activity?.SetTag("customer.id", customerId.ToString());
        activity?.SetTag("order.operation", "place");
        return activity;
    }

    public static void RecordOrderValue(Activity? activity, decimal amount)
    {
        activity?.SetTag("order.amount", amount.ToString("F2"));
        activity?.AddEvent(new ActivityEvent("OrderValueCalculated", 
            tags: new ActivityTagsCollection { { "amount", amount } }));
    }

    public static void RecordInventoryCheck(Activity? activity, int itemCount, bool success)
    {
        activity?.AddEvent(new ActivityEvent("InventoryChecked", tags: new ActivityTagsCollection
        {
            { "item_count", itemCount },
            { "success", success }
        }));
    }
}

// Sử dụng trong handler
public class PlaceOrderHandler
{
    private readonly OrderMetrics _metrics;
    private readonly ILogger<PlaceOrderHandler> _logger;

    public async Task<Guid> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        using var activity = OrderTracing.StartProcessOrder(cmd.OrderId, cmd.CustomerId);
        using var checkoutTracker = _metrics.TrackCheckout();
        
        try
        {
            var order = Order.Place(cmd);
            
            OrderTracing.RecordOrderValue(activity, order.TotalAmount);
            
            // Check inventory
            var inventoryResult = await _inventoryService.CheckAsync(cmd.Lines, ct);
            OrderTracing.RecordInventoryCheck(activity, cmd.Lines.Count, inventoryResult.IsAvailable);
            
            if (!inventoryResult.IsAvailable)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Insufficient inventory");
                _metrics.RecordOrderFailed("insufficient_inventory");
                throw new InsufficientInventoryException();
            }

            await _session.SaveChangesAsync(ct);
            
            activity?.SetStatus(ActivityStatusCode.Ok);
            _metrics.RecordOrderPlaced(order.TotalAmount, cmd.PaymentMethod, cmd.Region);
            
            _logger.LogInformation(
                "Order {OrderId} placed successfully for {Amount:C}",
                cmd.OrderId, order.TotalAmount);
            
            return cmd.OrderId;
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

---

## 6. Health Checks: kiểm tra sức khỏe hệ thống

```csharp
builder.Services.AddHealthChecks()
    // Database health
    .AddNpgsql(
        connectionString: builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgres",
        failureStatus: HealthStatus.Degraded,
        tags: ["db", "critical"])
    
    // Redis health
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: ["cache"])
    
    // RabbitMQ health
    .AddRabbitMQ(
        name: "rabbitmq",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["messaging", "critical"])
    
    // Custom business health check
    .AddCheck<OrderProcessingHealthCheck>(
        "order-processing",
        failureStatus: HealthStatus.Degraded,
        tags: ["business"])
    
    // Disk space
    .AddDiskStorageHealthCheck(
        s => s.AddDrive("/", minimumFreeMegabytes: 1024),
        name: "disk",
        failureStatus: HealthStatus.Degraded);

// Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness: chỉ check app có đang chạy không (không check dependencies)
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Readiness: check app đã sẵn sàng nhận traffic chưa
    Predicate = check => check.Tags.Contains("critical")
});
```

---

## 7. Alerting Rules trong Prometheus/Grafana

```yaml
# prometheus-alerts.yml
groups:
  - name: dotnet-service
    rules:
      # Error rate cao
      - alert: HighErrorRate
        expr: |
          rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m])
          / rate(http_server_request_duration_seconds_count[5m]) > 0.05
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "High error rate on {{ $labels.service_name }}"
          description: "Error rate is {{ $value | humanizePercentage }} for {{ $labels.service_name }}"

      # Latency cao
      - alert: HighLatency
        expr: |
          histogram_quantile(0.95,
            rate(http_server_request_duration_seconds_bucket[5m])
          ) > 2
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High p95 latency on {{ $labels.service_name }}"
          description: "p95 latency is {{ $value }}s"

      # Circuit breaker mở
      - alert: CircuitBreakerOpen
        expr: polly_circuit_breaker_state == 1
        for: 0m
        labels:
          severity: critical
        annotations:
          summary: "Circuit breaker open: {{ $labels.pipeline_name }}"

      # Consumer lag cao
      - alert: KafkaConsumerLagHigh
        expr: kafka_consumer_lag > 10000
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Kafka consumer lag high for {{ $labels.group }}/{{ $labels.topic }}"
```

---

## 8. Log Aggregation Architecture

```text
Service A ──► OpenTelemetry Collector ──► Grafana Loki (logs)
Service B ──►         │               ──► Jaeger/Tempo (traces)
Service C ──►         │               ──► Prometheus (metrics)
                       │
                       └──► Long-term storage (S3, GCS)
                       └──► Alert manager
                       └──► Grafana Dashboard
```

---

## 9. Grafana Dashboard thiết yếu

Mỗi service cần có dashboard với các panel:

```text
Row 1 - Overview
├── Request Rate (req/s)
├── Error Rate (%)
├── p50/p95/p99 Latency
└── Apdex Score

Row 2 - Dependencies
├── Database Query Duration
├── Cache Hit Rate
├── External API Success Rate
└── Queue/Topic Consumer Lag

Row 3 - Resources
├── CPU Usage
├── Memory Usage
├── GC Pause Duration
└── Thread Pool Queue

Row 4 - Business Metrics
├── Orders per Minute
├── Revenue per Minute
├── Failed Orders
└── Active Users
```

---

## 10. Checklist production cho Observability

- [ ] OpenTelemetry instrumentation cho tất cả service: traces, metrics, logs
- [ ] Correlation ID trong mọi request và log entry
- [ ] Structured logging (không log string concatenation)
- [ ] Custom business metrics: không chỉ technical metrics
- [ ] Health checks: liveness và readiness riêng biệt
- [ ] Distributed tracing: có thể trace một request qua tất cả service
- [ ] Alerting: error rate, latency p95, circuit breaker state, consumer lag
- [ ] Runbook cho mỗi alert: hướng dẫn xử lý khi alert fire
- [ ] Log retention: production logs giữ ít nhất 30 ngày
- [ ] Trace sampling: không trace 100% request ở production (30-day retention cho sampled traces)
- [ ] Dashboard cho từng service và overview dashboard
- [ ] SLO tracking: đo và report service level objectives
