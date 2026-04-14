# Resilience Patterns với Polly v8: Xây dựng hệ thống tự phục hồi và chịu lỗi

## 1. Tại sao Resilience là bắt buộc trong Distributed System

Trong hệ thống distributed, failure là bình thường, không phải ngoại lệ. Network có thể bị chậm, service downstream có thể overload, database connection pool có thể cạn kiệt, third-party API có thể trả về 503.

Nếu không có resilience patterns, một lỗi nhỏ ở một service có thể cascade thành outage toàn hệ thống. Ví dụ:
- Payment service chậm → Order service chờ quá lâu → Request tích lũy → Thread pool exhausted → Order service down → Toàn bộ website down

**Resilience patterns** giúp hệ thống:
1. **Retry**: tự thử lại khi gặp lỗi tạm thời
2. **Circuit Breaker**: dừng gọi service đang lỗi để tránh cascade failure
3. **Timeout**: không chờ vô hạn
4. **Bulkhead**: giới hạn tài nguyên dành cho từng dependency
5. **Fallback**: trả về data dự phòng khi service down
6. **Hedge**: gửi request song song và lấy kết quả nhanh nhất

---

## 2. Polly v8: Microsoft.Extensions.Resilience

Polly v8 tích hợp sâu với .NET HttpClient pipeline thông qua `Microsoft.Extensions.Resilience`.

```bash
dotnet add package Microsoft.Extensions.Http.Resilience
dotnet add package Polly.Extensions
```

---

## 3. Retry Pattern: xử lý lỗi tạm thời

### 3.1 Exponential Backoff với Jitter

```csharp
// Retry với exponential backoff và jitter để tránh thundering herd
builder.Services.AddHttpClient<IPaymentClient, PaymentClient>()
    .AddResilienceHandler("payment-resilience", pipeline =>
    {
        // Retry policy
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true, // Quan trọng: thêm random jitter để tránh thundering herd
            
            // Chỉ retry các lỗi tạm thời
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: HttpStatusCode.ServiceUnavailable } => PredicateResult.True(),
                { Result.StatusCode: HttpStatusCode.TooManyRequests } => PredicateResult.True(),
                { Result.StatusCode: HttpStatusCode.GatewayTimeout } => PredicateResult.True(),
                _ => PredicateResult.False()
            },
            
            // Dùng Retry-After header nếu có
            DelayGenerator = args =>
            {
                if (args.Outcome.Result?.Headers.RetryAfter is { } retryAfter)
                {
                    var delay = retryAfter.Date is not null
                        ? retryAfter.Date.Value - DateTimeOffset.UtcNow
                        : retryAfter.Delta ?? TimeSpan.FromSeconds(5);
                    
                    return ValueTask.FromResult<TimeSpan?>(delay);
                }
                return ValueTask.FromResult<TimeSpan?>(null); // Dùng default backoff
            },
            
            OnRetry = args =>
            {
                logger.LogWarning(
                    "Retry {Attempt}/{MaxAttempts} for payment service after {Delay}ms. Error: {Error}",
                    args.AttemptNumber + 1, 3,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                
                return ValueTask.CompletedTask;
            }
        });
    });
```

### 3.2 Retry với Non-HTTP clients

```csharp
// Retry cho database operations
public class ResilientOrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;
    private readonly ResiliencePipeline _pipeline;

    public ResilientOrderRepository(AppDbContext db, ResiliencePipelineProvider<string> pipelineProvider)
    {
        _db = db;
        _pipeline = pipelineProvider.GetPipeline("database");
    }

    public async Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(async token =>
        {
            return await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, token);
        }, ct);
    }
}

// Đăng ký pipeline chung cho database operations
builder.Services.AddResiliencePipeline("database", pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            .Handle<DbUpdateConcurrencyException>()
            .Handle<TimeoutException>()
    });
    
    pipeline.AddTimeout(TimeSpan.FromSeconds(30));
});
```

---

## 4. Circuit Breaker: dừng gọi service đang lỗi

Circuit Breaker có 3 trạng thái:
- **Closed**: hoạt động bình thường, gọi thẳng vào service
- **Open**: dừng gọi service, trả về lỗi ngay lập tức (fail fast)
- **Half-Open**: cho thử một số request để kiểm tra xem service đã recover chưa

```csharp
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>()
    .AddResilienceHandler("inventory-resilience", pipeline =>
    {
        // Circuit breaker
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            // Mở circuit khi 50% request fail trong window 30 giây
            FailureRatio = 0.5,
            MinimumThroughput = 10,        // Cần ít nhất 10 requests để đánh giá
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30), // Giữ open 30 giây
            
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: >= HttpStatusCode.InternalServerError } => PredicateResult.True(),
                _ => PredicateResult.False()
            },
            
            OnOpened = args =>
            {
                logger.LogError(
                    "Circuit OPENED for inventory service. Break duration: {Duration}s. Failure ratio: {Ratio}",
                    args.BreakDuration.TotalSeconds,
                    args.Outcome.Exception?.Message);
                
                // Alert: gửi notification tới on-call team
                metrics.IncrementCircuitOpenCount("inventory");
                
                return ValueTask.CompletedTask;
            },
            
            OnClosed = args =>
            {
                logger.LogInformation("Circuit CLOSED for inventory service - service recovered");
                return ValueTask.CompletedTask;
            },
            
            OnHalfOpened = args =>
            {
                logger.LogInformation("Circuit HALF-OPEN - probing inventory service");
                return ValueTask.CompletedTask;
            }
        });

        // Retry TRƯỚC circuit breaker trong pipeline
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential
        });
        
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));
    });
```

---

## 5. Bulkhead Pattern: giới hạn tài nguyên cho từng dependency

Bulkhead tách biệt tài nguyên (thread pool, connection pool) cho từng dependency. Nếu một dependency chậm, nó không làm cạn kiệt resources của toàn hệ thống.

```csharp
// Bulkhead với semaphore
builder.Services.AddResiliencePipeline("notification-service", pipeline =>
{
    // Giới hạn 20 concurrent calls tới notification service
    // Nếu đã có 20 calls, request mới sẽ queue hoặc fail fast
    pipeline.AddConcurrencyLimiter(new ConcurrencyLimiterStrategyOptions
    {
        MaxConcurrentExecutions = 20,
        QueuedExecutions = 10, // Cho phép queue 10 requests
        
        OnRejected = args =>
        {
            logger.LogWarning("Notification service bulkhead FULL - rejecting request");
            metrics.IncrementBulkheadRejections("notification");
            return ValueTask.CompletedTask;
        }
    });
    
    pipeline.AddTimeout(TimeSpan.FromSeconds(5));
});
```

---

## 6. Timeout: không chờ vô hạn

```csharp
// Timeout configuration theo loại operation
builder.Services.AddResiliencePipeline("fast-operations", pipeline =>
{
    pipeline.AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(2),
        OnTimeout = args =>
        {
            logger.LogWarning("Operation timed out after {Timeout}s: {Context}",
                args.Timeout.TotalSeconds, args.Context.OperationKey);
            return ValueTask.CompletedTask;
        }
    });
});

builder.Services.AddResiliencePipeline("slow-operations", pipeline =>
{
    pipeline.AddTimeout(TimeSpan.FromSeconds(30));
});

// Dynamic timeout dựa trên context
builder.Services.AddResiliencePipeline("adaptive-timeout", pipeline =>
{
    pipeline.AddTimeout(new TimeoutStrategyOptions
    {
        TimeoutGenerator = args =>
        {
            // Context-aware timeout
            if (args.Context.Properties.TryGetValue(
                new ResiliencePropertyKey<bool>("is-background"), out var isBackground) && isBackground)
            {
                return ValueTask.FromResult(TimeSpan.FromMinutes(5));
            }
            return ValueTask.FromResult(TimeSpan.FromSeconds(10));
        }
    });
});
```

---

## 7. Fallback Pattern: trả về data dự phòng

```csharp
// Fallback cho product recommendations
builder.Services.AddHttpClient<IRecommendationClient, RecommendationClient>()
    .AddResilienceHandler("recommendations", pipeline =>
    {
        pipeline.AddFallback(new HttpFallbackStrategyOptions
        {
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: not null } => PredicateResult.True(),
                { Result.IsSuccessStatusCode: false } => PredicateResult.True(),
                _ => PredicateResult.False()
            },
            
            FallbackAction = args =>
            {
                logger.LogWarning("Recommendation service unavailable, returning fallback");
                
                // Trả về trending products từ cache thay vì personalized recommendations
                var fallback = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new RecommendationsDto(
                        Source: "fallback-trending",
                        Items: GetCachedTrendingProducts()))
                };
                return ValueTask.FromResult(fallback);
            }
        });
        
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromMinutes(1)
        });
        
        pipeline.AddTimeout(TimeSpan.FromSeconds(2));
    });
```

---

## 8. Hedge: gửi request song song để giảm tail latency

Hedge giảm p99 latency bằng cách gửi request tới nhiều instance và lấy kết quả nhanh nhất.

```csharp
builder.Services.AddHttpClient<ICatalogClient, CatalogClient>()
    .AddResilienceHandler("catalog-hedge", pipeline =>
    {
        // Nếu request đầu tiên không response trong 500ms, gửi request thứ hai song song
        // Lấy kết quả của request nào về trước
        pipeline.AddHedging(new HttpHedgingStrategyOptions
        {
            MaxHedgedAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500),
            
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: HttpStatusCode.ServiceUnavailable } => PredicateResult.True(),
                _ => PredicateResult.False()
            },
            
            ActionGenerator = args =>
            {
                // Gửi request tới endpoint khác khi hedge
                return () => args.Callback(args.ActionContext);
            }
        });
        
        pipeline.AddTimeout(TimeSpan.FromSeconds(5));
    });
```

---

## 9. Standard Resilience Handler: bộ preset cho HttpClient

```csharp
// AddStandardResilienceHandler là preset tốt cho hầu hết trường hợp
builder.Services.AddHttpClient<IOrderClient, OrderClient>()
    .AddStandardResilienceHandler(opts =>
    {
        // Customize nếu cần
        opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        
        opts.Retry.MaxRetryAttempts = 3;
        opts.Retry.Delay = TimeSpan.FromSeconds(1);
        opts.Retry.BackoffType = DelayBackoffType.Exponential;
        opts.Retry.UseJitter = true;
        
        opts.CircuitBreaker.FailureRatio = 0.5;
        opts.CircuitBreaker.MinimumThroughput = 10;
        opts.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        opts.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        
        opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    });
```

---

## 10. Resilience Metrics và Observability

```csharp
// Polly v8 tích hợp sẵn với OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Polly"); // Tự động collect Polly metrics
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource("Polly"); // Tự động trace Polly operations
    });

// Custom resilience event handler
builder.Services.AddResiliencePipelineRegistry()
    .ConfigureTelemetry(new TelemetryOptions
    {
        LoggerFactory = LoggerFactory.Create(b => b.AddConsole()),
        MeteringEnricher = new CustomMeteringEnricher()
    });

// Dashboard: xem trạng thái circuit breakers
app.MapGet("/health/resilience", (ResiliencePipelineRegistry registry) =>
{
    // Expose circuit breaker states
    return Results.Ok(new
    {
        CircuitBreakers = new
        {
            Payment = GetCircuitState("payment-resilience"),
            Inventory = GetCircuitState("inventory-resilience"),
            Notification = GetCircuitState("notification-service")
        }
    });
});
```

---

## 11. Testing Resilience

```csharp
// Unit test cho retry behavior
[Fact]
public async Task Should_Retry_On_Transient_Error()
{
    var callCount = 0;
    var pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(10),
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
        pipeline.ExecuteAsync(async _ =>
        {
            callCount++;
            throw new HttpRequestException("Service unavailable");
        }).AsTask());

    Assert.Equal(4, callCount); // 1 initial + 3 retries
}

// Integration test với Chaos engineering
[Fact]
public async Task Should_Open_Circuit_After_Threshold_Failures()
{
    var chaosHandler = new ChaosHandler(failureRate: 1.0); // 100% failure
    var httpClient = new HttpClient(chaosHandler);
    
    // Send enough requests to open circuit
    for (var i = 0; i < 15; i++)
    {
        try { await httpClient.GetAsync("http://test-service/api"); }
        catch { }
    }

    // Next request should fail fast (circuit open)
    var sw = Stopwatch.StartNew();
    await Assert.ThrowsAsync<BrokenCircuitException>(() =>
        httpClient.GetAsync("http://test-service/api"));
    sw.Stop();

    // Fail fast phải rất nhanh (không đợi timeout)
    Assert.True(sw.ElapsedMilliseconds < 100);
}
```

---

## 12. Checklist production cho Resilience

- [ ] Mọi HTTP client gọi external service đều có resilience pipeline
- [ ] Retry chỉ cho idempotent operations (GET, PUT, DELETE) - không retry POST không có idempotency key
- [ ] Circuit breaker cho tất cả critical dependencies
- [ ] Timeout cho mọi I/O operation - không để request chờ vô hạn
- [ ] Bulkhead cho các dependency có thể gây cascade failure
- [ ] Fallback strategy cho các tính năng non-critical (recommendation, analytics)
- [ ] Monitor circuit breaker state changes và alert khi circuit mở
- [ ] Jitter trong retry để tránh thundering herd
- [ ] Test resilience với chaos engineering (chaos monkey, Polly chaos)
- [ ] Resilience metrics trong OpenTelemetry dashboard
- [ ] Document SLA và SLO cho từng service - từ đó suy ra retry/timeout budget
