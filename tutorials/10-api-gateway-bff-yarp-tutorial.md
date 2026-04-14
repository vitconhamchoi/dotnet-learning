# API Gateway & BFF Pattern với YARP: Cổng vào thông minh cho hệ thống microservices

## 1. Vấn đề khi không có API Gateway

Khi hệ thống có nhiều microservice, frontend (web, mobile, third-party) cần gọi trực tiếp vào từng service. Điều này tạo ra hàng loạt vấn đề:

- **Quá nhiều round-trip**: mobile app phải gọi 5-10 service để render một màn hình
- **CORS phức tạp**: mỗi service phải tự cấu hình CORS riêng
- **Authentication lặp lại**: mỗi service phải validate JWT token riêng
- **Rate limiting thiếu tập trung**: khó protect toàn hệ thống
- **Thay đổi service URL ảnh hưởng client**: khi service refactor, client phải update
- **Không có single entry point**: khó monitor, log và audit
- **SSL termination phân tán**: phức tạp certificate management

**API Gateway** là reverse proxy thông minh đứng trước tất cả service, xử lý các cross-cutting concerns: routing, auth, rate limiting, caching, request aggregation, logging, tracing.

**BFF (Backend For Frontend)** là pattern xây API Gateway chuyên biệt cho từng loại client (web BFF, mobile BFF, third-party BFF), tối ưu response cho từng loại client thay vì một gateway chung cho tất cả.

---

## 2. YARP: Yet Another Reverse Proxy

YARP là reverse proxy library của Microsoft, viết bằng .NET, rất phù hợp làm API Gateway cho hệ thống .NET.

**Ưu điểm của YARP**:
- Config-driven và code-driven đều được
- Performance cao nhờ dùng Kestrel pipeline
- Tích hợp seamless với ASP.NET Core middleware
- Hỗ trợ service discovery động
- Load balancing built-in
- Header manipulation, path transformation
- Health check integration

---

## 3. Setup YARP cơ bản

```bash
dotnet new web -n ApiGateway
cd ApiGateway
dotnet add package Yarp.ReverseProxy
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package AspNetCoreRateLimit
```

### 3.1 Config-driven routing

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "catalog-route": {
        "ClusterId": "catalog-cluster",
        "Match": {
          "Path": "/api/catalog/{**catch-all}"
        },
        "Transforms": [
          { "PathPattern": "/api/{**catch-all}" }
        ]
      },
      "ordering-route": {
        "ClusterId": "ordering-cluster",
        "Match": {
          "Path": "/api/orders/{**catch-all}"
        },
        "AuthorizationPolicy": "AuthenticatedUser",
        "RateLimiterPolicy": "OrderingPolicy"
      },
      "payment-route": {
        "ClusterId": "payment-cluster",
        "Match": {
          "Path": "/api/payments/{**catch-all}"
        },
        "AuthorizationPolicy": "AuthenticatedUser",
        "CorsPolicy": "PaymentCors"
      }
    },
    "Clusters": {
      "catalog-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "catalog-1": { "Address": "http://catalog-api:8080" },
          "catalog-2": { "Address": "http://catalog-api-2:8080" }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:10",
            "Timeout": "00:00:05",
            "Policy": "ConsecutiveFailures",
            "Path": "/health"
          }
        }
      },
      "ordering-cluster": {
        "Destinations": {
          "ordering-1": { "Address": "http://ordering-api:8080" }
        }
      },
      "payment-cluster": {
        "Destinations": {
          "payment-1": { "Address": "http://payment-api:8080" }
        }
      }
    }
  }
}
```

### 3.2 Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver(); // Nếu dùng .NET Aspire service discovery

// Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["Auth:Authority"];
        opts.Audience = builder.Configuration["Auth:Audience"];
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

// Authorization
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());
    
    opts.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("role", "admin"));
});

// Rate Limiting (ASP.NET Core built-in từ .NET 7)
builder.Services.AddRateLimiter(opts =>
{
    opts.AddPolicy("OrderingPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));

    opts.AddPolicy("PublicApi", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 50
            }));

    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", ct);
    };
});

// CORS
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("WebApp", policy =>
        policy.WithOrigins("https://myapp.com", "https://www.myapp.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

var app = builder.Build();

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware trước khi forward
app.Use(async (context, next) =>
{
    // Inject correlation ID vào mọi request
    if (!context.Request.Headers.ContainsKey("X-Correlation-Id"))
    {
        context.Request.Headers["X-Correlation-Id"] = Guid.NewGuid().ToString();
    }
    await next();
});

app.MapReverseProxy(proxyPipeline =>
{
    // Custom pipeline transform
    proxyPipeline.Use(async (ctx, next) =>
    {
        // Strip internal headers từ client request
        ctx.Request.Headers.Remove("X-Internal-Secret");
        await next();
    });
});

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }));

app.Run();
```

---

## 4. Dynamic Route Loading: không cần restart khi thêm service mới

```csharp
// Dynamic config từ database hoặc config service
public class DatabaseRouteConfigProvider : IProxyConfigProvider
{
    private readonly IRouteConfigRepository _repo;
    private readonly ILogger<DatabaseRouteConfigProvider> _logger;
    private CancellationTokenSource _cts = new();
    private IProxyConfig _config;

    public DatabaseRouteConfigProvider(IRouteConfigRepository repo, ILogger<DatabaseRouteConfigProvider> logger)
    {
        _repo = repo;
        _logger = logger;
        _config = new DatabaseProxyConfig([], [], _cts.Token);
        
        // Background reload
        _ = ReloadPeriodicallyAsync();
    }

    public IProxyConfig GetConfig() => _config;

    private async Task ReloadPeriodicallyAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload route config");
            }
        }
    }

    public async Task ReloadAsync()
    {
        var routes = await _repo.GetActiveRoutesAsync();
        var clusters = await _repo.GetActiveClustersAsync();
        
        var old = Interlocked.Exchange(
            ref _config,
            new DatabaseProxyConfig(routes, clusters, _cts.Token));
        
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        oldCts.Cancel();
        oldCts.Dispose();
    }
}
```

---

## 5. BFF Pattern: API Gateway chuyên biệt cho từng client

Thay vì một gateway chung, bạn tạo gateway riêng cho web và mobile.

```text
                     ┌─────────────┐
                     │  Web App    │
                     └──────┬──────┘
                            │
                     ┌──────▼──────────────────┐
                     │   Web BFF               │
                     │  (aggregate + shape)    │
                     └──────┬──────────────────┘
                            │
         ┌──────────────────┼──────────────────┐
         │                  │                  │
┌────────▼───────┐  ┌───────▼──────┐  ┌───────▼──────┐
│  Catalog API   │  │  Order API   │  │  User API    │
└────────────────┘  └──────────────┘  └──────────────┘
         │                  │                  │
                     ┌──────▼──────────────────┐
                     │  Mobile BFF             │
                     │  (lighter payload)      │
                     └──────┬──────────────────┘
                            │
                     ┌──────▼──────┐
                     │ Mobile App  │
                     └─────────────┘
```

### 5.1 Request Aggregation trong BFF

```csharp
// Web BFF aggregates multiple service calls thành một response
app.MapGet("/bff/homepage", async (
    ICatalogClient catalog,
    IOrderClient orders,
    IUserClient users,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var userId = ctx.User.GetUserId();
    
    // Gọi song song nhiều service
    var featuredTask = catalog.GetFeaturedProductsAsync(limit: 10, ct);
    var recentOrdersTask = orders.GetRecentOrdersAsync(userId, limit: 3, ct);
    var userProfileTask = users.GetProfileAsync(userId, ct);

    await Task.WhenAll(featuredTask, recentOrdersTask, userProfileTask);

    // Shape response cho web
    return Results.Ok(new HomepageResponse(
        FeaturedProducts: featuredTask.Result,
        RecentOrders: recentOrdersTask.Result,
        User: userProfileTask.Result));
});

// Mobile BFF: lighter payload, mobile-optimized
app.MapGet("/mobile-bff/homepage", async (
    ICatalogClient catalog,
    IOrderClient orders,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var userId = ctx.User.GetUserId();
    
    // Mobile cần ít data hơn, optimize for bandwidth
    var featured = await catalog.GetFeaturedProductsAsync(limit: 5, ct);
    var hasActiveOrders = await orders.HasActiveOrdersAsync(userId, ct);

    // Mobile-specific lightweight response
    return Results.Ok(new MobileHomepageResponse(
        FeaturedProducts: featured.Select(p => new MobileProductCard(
            p.Id, p.Name, p.ThumbnailUrl, p.Price)),
        HasActiveOrders: hasActiveOrders,
        BadgeCount: hasActiveOrders ? 1 : 0));
});
```

---

## 6. Request Transformation và Header Enrichment

```csharp
// Custom transform: thêm user context vào header trước khi forward
public class UserContextTransform : RequestTransform
{
    public override ValueTask ApplyAsync(RequestTransformContext ctx)
    {
        var user = ctx.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Forward user identity tới downstream services
            ctx.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-User-Id", user.FindFirst("sub")?.Value);
            ctx.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-User-Email", user.FindFirst("email")?.Value);
            ctx.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-User-Roles", string.Join(",", user.FindAll("role").Select(c => c.Value)));
        }
        
        return ValueTask.CompletedTask;
    }
}

// Đăng ký transform
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        ctx.AddRequestTransform(async transformCtx =>
        {
            // Thêm correlation ID và request ID
            transformCtx.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-Correlation-Id",
                transformCtx.HttpContext.Request.Headers["X-Correlation-Id"].ToString());
            
            transformCtx.ProxyRequest.Headers.TryAddWithoutValidation(
                "X-Forwarded-For",
                transformCtx.HttpContext.Connection.RemoteIpAddress?.ToString());
        });
        
        ctx.AddResponseTransform(async transformCtx =>
        {
            // Thêm gateway version header
            transformCtx.ProxyResponse?.Headers.TryAddWithoutValidation(
                "X-Gateway-Version", "2.0");
        });
    });
```

---

## 7. Circuit Breaker tại Gateway

```csharp
// Dùng Polly HttpClient resilience
builder.Services.AddHttpClient("downstream")
    .AddStandardResilienceHandler(opts =>
    {
        opts.CircuitBreaker.FailureRatio = 0.5;
        opts.CircuitBreaker.MinimumThroughput = 10;
        opts.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        opts.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);
        opts.Retry.MaxRetryAttempts = 3;
        opts.Retry.Delay = TimeSpan.FromMilliseconds(100);
        opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
```

---

## 8. Caching tại Gateway cho public endpoints

```csharp
// Output caching cho catalog endpoints
builder.Services.AddOutputCache(opts =>
{
    opts.AddBasePolicy(policy => policy.NoCache());
    
    opts.AddPolicy("CatalogCache", policy =>
        policy
            .Expire(TimeSpan.FromMinutes(5))
            .Tag("catalog")
            .SetVaryByHeader("Accept-Language", "Accept-Encoding"));
});

// Apply cache cho specific route
app.MapGet("/api/catalog/categories", async (ICatalogClient client, CancellationToken ct) =>
{
    return Results.Ok(await client.GetCategoriesAsync(ct));
}).CacheOutput("CatalogCache");

// Cache invalidation endpoint (chỉ dành cho internal services)
app.MapPost("/internal/cache/invalidate", async (
    string tag,
    IOutputCacheStore store,
    CancellationToken ct) =>
{
    await store.EvictByTagAsync(tag, ct);
    return Results.Ok();
}).RequireAuthorization("InternalService");
```

---

## 9. Observability tại API Gateway

```csharp
// Middleware log mọi request/response với structured logging
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].ToString();
    
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["ClientIp"] = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        ["UserId"] = ctx.User.FindFirst("sub")?.Value ?? "anonymous"
    });
    
    await next();
    
    sw.Stop();
    
    logger.LogInformation(
        "Gateway: {Method} {Path} → {StatusCode} in {ElapsedMs}ms to cluster {Cluster}",
        ctx.Request.Method,
        ctx.Request.Path,
        ctx.Response.StatusCode,
        sw.ElapsedMilliseconds,
        ctx.GetReverseProxyFeature()?.Route?.Config?.ClusterId ?? "none");
    
    // Metrics
    gatewayRequestDuration
        .WithLabels(ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode.ToString())
        .Observe(sw.Elapsed.TotalSeconds);
});
```

---

## 10. Checklist production cho API Gateway

- [ ] Rate limiting theo user ID (authenticated) và IP (anonymous)
- [ ] Authentication/authorization tập trung ở gateway - downstream service chỉ trust gateway header
- [ ] Tất cả request đều có correlation ID
- [ ] Circuit breaker cho từng downstream cluster
- [ ] Health check cho gateway và downstream service
- [ ] Logging: mọi request với timing, status, cluster
- [ ] Metrics: request rate, error rate, latency p50/p95/p99
- [ ] Caching cho public read endpoints với proper cache invalidation
- [ ] Header stripping: xóa sensitive headers từ client trước khi forward
- [ ] Có separate BFF nếu web và mobile có requirement khác nhau
- [ ] HTTPS/TLS termination tại gateway
- [ ] mTLS hoặc API key khi gọi giữa gateway và downstream service
