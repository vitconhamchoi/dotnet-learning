# API Gateway với YARP (Yet Another Reverse Proxy)

## Mục Lục
1. [API Gateway Pattern](#api-gateway-pattern)
2. [YARP Overview](#yarp-overview)
3. [Cài Đặt và Cấu Hình](#cai-dat-cau-hinh)
4. [Route Configuration](#route-configuration)
5. [Load Balancing](#load-balancing)
6. [Rate Limiting](#rate-limiting)
7. [Request Transformation](#request-transformation)
8. [Authentication Middleware](#authentication-middleware)
9. [Complete Sample](#complete-sample)
10. [Best Practices](#best-practices)

---

## 1. API Gateway Pattern

API Gateway là entry point duy nhất cho tất cả client requests. Nó giải quyết cross-cutting concerns như authentication, rate limiting, logging, và routing.

```
                    ┌─────────────────────────────────────────────┐
                    │                API GATEWAY                  │
     Client Apps    │  ┌──────────────────────────────────────┐  │
  ┌────────────┐    │  │  Authentication │ Rate Limiting       │  │
  │  Web App   │───►│  ├──────────────────────────────────────┤  │
  └────────────┘    │  │  Load Balancing │ Request Transform   │  │
  ┌────────────┐    │  ├──────────────────────────────────────┤  │
  │ Mobile App │───►│  │  SSL Termination│ Logging/Tracing    │  │
  └────────────┘    │  ├──────────────────────────────────────┤  │
  ┌────────────┐    │  │  Circuit Break  │ Caching            │  │
  │ 3rd Party  │───►│  └──────────────────────────────────────┘  │
  └────────────┘    └────────────┬────────────────────────────────┘
                                 │
               ┌─────────────────┼─────────────────┐
               │                 │                 │
               ▼                 ▼                 ▼
    ┌─────────────────┐ ┌───────────────┐ ┌───────────────────┐
    │  Order Service  │ │Product Service│ │   User Service    │
    │   :5001         │ │   :5002       │ │   :5003           │
    └─────────────────┘ └───────────────┘ └───────────────────┘

Patterns của API Gateway:
1. Backend for Frontend (BFF) - Gateway riêng cho mỗi client type
2. Gateway Aggregation - Kết hợp nhiều service responses
3. Gateway Offloading - Di chuyển cross-cutting concerns ra gateway
```

### Lợi ích API Gateway:
- **Simplified client** - Client chỉ cần biết 1 endpoint
- **Security** - SSL termination, authentication tập trung
- **Observability** - Centralized logging và tracing
- **Traffic management** - Rate limiting, load balancing
- **Protocol translation** - REST → gRPC, WebSocket, etc.

---

## 2. YARP Overview

YARP (Yet Another Reverse Proxy) là một thư viện .NET của Microsoft để xây dựng reverse proxy và API gateway với hiệu năng cao.

**Tính năng YARP:**
- ✅ Cấu hình từ code hoặc JSON
- ✅ Dynamic route updates (không cần restart)
- ✅ Multiple load balancing algorithms
- ✅ Active/passive health checks
- ✅ Request/Response transformation
- ✅ Session affinity
- ✅ Middleware pipeline integration

---

## 3. Cài Đặt và Cấu Hình

```xml
<!-- ApiGateway.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" Version="2.2.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0" />
    <PackageReference Include="AspNetCoreRateLimit" Version="5.0.0" />
    <PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageReference Include="OpenTelemetry.Exporter.Jaeger" Version="1.5.0" />
    <PackageReference Include="Polly" Version="8.4.2" />
  </ItemGroup>
</Project>
```

### JSON Configuration

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "order-route": {
        "ClusterId": "order-cluster",
        "AuthorizationPolicy": "AuthenticatedUser",
        "Match": {
          "Path": "/api/orders/{**catch-all}",
          "Methods": ["GET", "POST", "PUT", "DELETE"]
        },
        "Transforms": [
          { "PathPattern": "/api/{**catch-all}" },
          { "RequestHeader": "X-Forwarded-For", "Append": "{RemoteIpAddress}" },
          { "RequestHeader": "X-Gateway-Version", "Set": "1.0" }
        ]
      },
      "product-route": {
        "ClusterId": "product-cluster",
        "Match": {
          "Path": "/api/products/{**catch-all}"
        },
        "Transforms": [
          { "PathPattern": "/api/{**catch-all}" }
        ]
      },
      "product-route-v2": {
        "ClusterId": "product-cluster-v2",
        "Match": {
          "Path": "/api/v2/products/{**catch-all}",
          "Headers": [
            {
              "Name": "X-API-Version",
              "Values": ["2.0"],
              "Mode": "ExactHeader"
            }
          ]
        }
      },
      "user-route": {
        "ClusterId": "user-cluster",
        "AuthorizationPolicy": "AuthenticatedUser",
        "Match": {
          "Path": "/api/users/{**catch-all}"
        }
      },
      "auth-route": {
        "ClusterId": "user-cluster",
        "Match": {
          "Path": "/api/auth/{**catch-all}"
        }
      },
      "public-product-route": {
        "ClusterId": "product-cluster",
        "Match": {
          "Path": "/public/products/{**catch-all}"
        },
        "Transforms": [
          { "PathPattern": "/api/{**catch-all}" }
        ],
        "Metadata": {
          "RateLimitPolicy": "public-api"
        }
      }
    },
    "Clusters": {
      "order-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:10",
            "Timeout": "00:00:05",
            "Policy": "ConsecutiveFailures",
            "Path": "/health"
          },
          "Passive": {
            "Enabled": true,
            "Policy": "TransportFailureRate",
            "ReactivationPeriod": "00:02:00"
          }
        },
        "Destinations": {
          "order-service-1": {
            "Address": "http://order-service-1:5001"
          },
          "order-service-2": {
            "Address": "http://order-service-2:5001"
          }
        }
      },
      "product-cluster": {
        "LoadBalancingPolicy": "LeastRequests",
        "SessionAffinity": {
          "Enabled": false
        },
        "HttpRequest": {
          "Timeout": "00:00:30",
          "AllowResponseBuffering": false
        },
        "Destinations": {
          "product-service-1": {
            "Address": "http://product-service-1:5002",
            "Health": "http://product-service-1:5002/health"
          },
          "product-service-2": {
            "Address": "http://product-service-2:5002",
            "Health": "http://product-service-2:5002/health"
          },
          "product-service-3": {
            "Address": "http://product-service-3:5002",
            "Health": "http://product-service-3:5002/health"
          }
        }
      },
      "product-cluster-v2": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "product-service-v2": {
            "Address": "http://product-service-v2:5005"
          }
        }
      },
      "user-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "user-service": {
            "Address": "http://user-service:5003"
          }
        }
      }
    }
  },
  "Authentication": {
    "JwtBearer": {
      "Authority": "http://identity-server:5004",
      "Audience": "api-gateway",
      "ValidateLifetime": true
    }
  },
  "RateLimit": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Real-IP",
    "ClientIdHeader": "X-ClientId",
    "GeneralRules": [
      {
        "Endpoint": "*",
        "Period": "1m",
        "Limit": 100
      },
      {
        "Endpoint": "*/api/orders:post",
        "Period": "1m",
        "Limit": 10
      }
    ]
  }
}
```

---

## 4. Program.cs - Full Gateway Setup

```csharp
// Program.cs
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ==================== Authentication ====================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:JwtBearer:Authority"];
        options.Audience = builder.Configuration["Authentication:JwtBearer:Audience"];
        options.RequireHttpsMetadata = false; // Development only
        
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Support token from query string (for WebSockets, SignalR)
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// ==================== Authorization ====================
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser())
    .AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"))
    .AddPolicy("ProductManager", policy =>
        policy.RequireRole("Admin", "ProductManager"));

// ==================== Rate Limiting ====================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Global fixed window - 100 requests/minute per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });
    
    // Authenticated users get higher limit
    options.AddPolicy("authenticated", context =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "anon";
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"user:{userId}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 1000,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6 // 10-second segments
                });
        }
        
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"anon:{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 50,
                Window = TimeSpan.FromMinutes(1)
            });
    });
    
    // Strict rate limit cho order creation
    options.AddPolicy("order-creation", context =>
    {
        var userId = context.User.FindFirst("sub")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: userId,
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 20,
                AutoReplenishment = true
            });
    });
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = 
                ((int)retryAfter.TotalSeconds).ToString();
        }
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            Error = "Too many requests",
            Message = "Rate limit exceeded. Please try again later.",
            RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) 
                ? ra.TotalSeconds 
                : (double?)null
        }, token);
    };
});

// ==================== CORS ====================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(
                "http://localhost:3000",
                "https://ecommerce-app.com")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// ==================== YARP Reverse Proxy ====================
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms =>
    {
        // Add correlation ID to all proxied requests
        transforms.AddRequestTransform(async context =>
        {
            var correlationId = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? Guid.NewGuid().ToString();
            
            context.ProxyRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            context.HttpContext.Response.Headers["X-Correlation-ID"] = correlationId;
        });
    });

// ==================== Health Checks ====================
builder.Services.AddHealthChecks()
    .AddCheck("gateway", () => HealthCheckResult.Healthy("Gateway is running"))
    .AddUrlGroup(new Uri("http://order-service:5001/health"), "order-service", timeout: TimeSpan.FromSeconds(5))
    .AddUrlGroup(new Uri("http://product-service:5002/health"), "product-service", timeout: TimeSpan.FromSeconds(5))
    .AddUrlGroup(new Uri("http://user-service:5003/health"), "user-service", timeout: TimeSpan.FromSeconds(5));

// ==================== Observability ====================
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("ApiGateway")
        .AddJaegerExporter());

builder.Logging.AddJsonConsole();

var app = builder.Build();

// ==================== Middleware Pipeline ====================

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Request logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    
    using var _ = logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["UserId"] = context.User.FindFirst("sub")?.Value ?? "anonymous",
        ["ClientIP"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
    });
    
    logger.LogInformation("Request: {Method} {Path}", context.Request.Method, context.Request.Path);
    
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();
    
    logger.LogInformation(
        "Response: {StatusCode} in {ElapsedMs}ms",
        context.Response.StatusCode,
        sw.ElapsedMilliseconds);
});

// Health checks (before authentication - không cần auth)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Name != "gateway"
});

// Gateway info endpoint
app.MapGet("/gateway/info", () => new
{
    Version = "1.0.0",
    Environment = app.Environment.EnvironmentName,
    Timestamp = DateTime.UtcNow
}).AllowAnonymous();

// YARP Reverse Proxy (must be last)
app.MapReverseProxy(proxyPipeline =>
{
    // Custom middleware trong proxy pipeline
    proxyPipeline.Use(async (context, next) =>
    {
        // Kiểm tra maintenance mode
        if (IsMaintenanceMode())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Service Temporarily Unavailable",
                Message = "System is under maintenance. Please try again later."
            });
            return;
        }
        
        await next();
    });
    
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
    proxyPipeline.UsePassiveHealthChecks();
});

app.Run();

bool IsMaintenanceMode() => false; // Đọc từ config hoặc feature flag
```

---

## 5. Custom Route Configuration (Code-based)

```csharp
// DynamicProxyConfigProvider.cs - Dynamic routing từ database
public class DatabaseProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private volatile InMemoryConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private readonly Timer _timer;

    public DatabaseProxyConfigProvider(
        IServiceProvider serviceProvider,
        ILogger<DatabaseProxyConfigProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = LoadConfig();
        
        // Reload config mỗi 30 seconds
        _timer = new Timer(_ => Reload(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IProxyConfig GetConfig() => _config;

    private void Reload()
    {
        try
        {
            var newConfig = LoadConfig();
            var oldConfig = _config;
            _config = newConfig;
            oldConfig.SignalChange(); // Notify YARP to reload
            _logger.LogInformation("Proxy config reloaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload proxy config");
        }
    }

    private InMemoryConfig LoadConfig()
    {
        // Trong thực tế, load từ database hoặc service discovery
        var routes = new List<RouteConfig>
        {
            new RouteConfig
            {
                RouteId = "order-route",
                ClusterId = "order-cluster",
                AuthorizationPolicy = "AuthenticatedUser",
                Match = new RouteMatch
                {
                    Path = "/api/orders/{**catch-all}"
                },
                Transforms = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        ["PathPattern"] = "/api/{**catch-all}"
                    }
                }
            }
        };

        var clusters = new List<ClusterConfig>
        {
            new ClusterConfig
            {
                ClusterId = "order-cluster",
                LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = true,
                        Interval = TimeSpan.FromSeconds(10),
                        Timeout = TimeSpan.FromSeconds(5),
                        Policy = HealthCheckConstants.ActivePolicy.ConsecutiveFailures,
                        Path = "/health"
                    }
                },
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["order-1"] = new DestinationConfig { Address = "http://order-service-1:5001" },
                    ["order-2"] = new DestinationConfig { Address = "http://order-service-2:5001" }
                }
            }
        };

        return new InMemoryConfig(routes, clusters);
    }

    public void Dispose() => _timer.Dispose();
}

// Sử dụng provider
builder.Services.AddReverseProxy()
    .Services.AddSingleton<IProxyConfigProvider, DatabaseProxyConfigProvider>();
```

---

## 6. Request Transformation

```csharp
// Custom Transforms
public class GatewayTransformProvider : ITransformProvider
{
    public void Apply(TransformBuilderContext context)
    {
        // Add correlation ID
        context.AddRequestTransform(transformContext =>
        {
            if (!transformContext.ProxyRequest.Headers.Contains("X-Correlation-ID"))
            {
                transformContext.ProxyRequest.Headers.Add(
                    "X-Correlation-ID",
                    Guid.NewGuid().ToString());
            }
            return ValueTask.CompletedTask;
        });

        // Add user info from JWT
        context.AddRequestTransform(transformContext =>
        {
            var user = transformContext.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst("sub")?.Value;
                var userEmail = user.FindFirst("email")?.Value;
                var userRoles = string.Join(",", user.FindAll("role").Select(c => c.Value));

                if (userId != null)
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-ID", userId);
                if (userEmail != null)
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Email", userEmail);
                if (!string.IsNullOrEmpty(userRoles))
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Roles", userRoles);
            }
            return ValueTask.CompletedTask;
        });

        // Add response headers
        context.AddResponseTransform(transformContext =>
        {
            transformContext.HttpContext.Response.Headers["X-Gateway"] = "YARP/2.0";
            return ValueTask.CompletedTask;
        });
    }

    public void ValidateCluster(TransformClusterValidationContext context) { }
    public void ValidateRoute(TransformRouteValidationContext context) { }
}

// Request Aggregation Middleware (BFF Pattern)
public class ProductOrderAggregationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpClient _orderClient;
    private readonly HttpClient _productClient;

    public ProductOrderAggregationMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory)
    {
        _next = next;
        _orderClient = httpClientFactory.CreateClient("OrderService");
        _productClient = httpClientFactory.CreateClient("ProductService");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Custom aggregation endpoint
        if (context.Request.Path == "/api/checkout/summary" &&
            context.Request.Method == "GET")
        {
            await HandleCheckoutSummary(context);
            return;
        }

        await _next(context);
    }

    private async Task HandleCheckoutSummary(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        if (userId is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Parallel calls to multiple services
        var cartTask = _orderClient.GetFromJsonAsync<CartDto>($"api/cart/{userId}");
        var recommendationsTask = _productClient.GetFromJsonAsync<List<ProductSummaryDto>>(
            $"api/products/recommendations?userId={userId}");

        await Task.WhenAll(cartTask, recommendationsTask);

        var summary = new CheckoutSummaryDto
        {
            Cart = await cartTask,
            Recommendations = await recommendationsTask ?? new List<ProductSummaryDto>()
        };

        await context.Response.WriteAsJsonAsync(summary);
    }
}
```

---

## 7. Load Balancing Strategies

```csharp
// Custom Load Balancing Policy
public class WeightedRoundRobinLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly ConcurrentDictionary<string, int> _weights = new();
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public string Name => "WeightedRoundRobin";

    public DestinationState? PickDestination(
        HttpContext context,
        ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0) return null;
        if (availableDestinations.Count == 1) return availableDestinations[0];

        // Tính tổng trọng số
        var totalWeight = availableDestinations.Sum(d =>
            _weights.GetOrAdd(d.DestinationId, 1));

        var random = Random.Shared.Next(totalWeight);
        var cumulative = 0;

        foreach (var destination in availableDestinations)
        {
            var weight = _weights.GetOrAdd(destination.DestinationId, 1);
            cumulative += weight;
            if (random < cumulative)
                return destination;
        }

        return availableDestinations.Last();
    }
}

// Active Health Check Policy
public class HttpHealthCheckPolicy : IActiveHealthCheckPolicy
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpHealthCheckPolicy(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "HttpHealthCheck";

    public async Task ProbeDestionationAsync(
        ClusterConfig cluster,
        IReadOnlyList<DestinationConfig> destinations,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        
        foreach (var destination in destinations)
        {
            var healthUrl = destination.Health ?? $"{destination.Address}/health";
            
            try
            {
                var response = await client.GetAsync(healthUrl, cancellationToken);
                // Process result...
            }
            catch { /* Mark as unhealthy */ }
        }
    }
}
```

---

## 8. JWT Validation Middleware

```csharp
// GatewayAuthMiddleware.cs
public class GatewayAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayAuthMiddleware> _logger;
    
    // Routes không cần auth
    private static readonly HashSet<string> PublicRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/refresh",
        "/api/auth/forgot-password",
        "/health",
        "/gateway/info"
    };

    public GatewayAuthMiddleware(RequestDelegate next, ILogger<GatewayAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";
        
        // Skip auth cho public routes
        if (PublicRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Kiểm tra JWT token
        if (!context.User.Identity?.IsAuthenticated == true)
        {
            _logger.LogWarning("Unauthenticated request to {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Unauthorized",
                Message = "Authentication is required to access this resource"
            });
            return;
        }

        // Kiểm tra token expiry
        var expClaim = context.User.FindFirst("exp");
        if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
        {
            var expiryDate = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expiryDate < DateTimeOffset.UtcNow)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    Error = "Token Expired",
                    Message = "Your session has expired. Please login again."
                });
                return;
            }
        }

        await _next(context);
    }
}

// API Key Authentication (cho external clients)
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiKeyValidator _apiKeyValidator;

    public ApiKeyAuthMiddleware(RequestDelegate next, IApiKeyValidator apiKeyValidator)
    {
        _next = next;
        _apiKeyValidator = apiKeyValidator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check for API key in header
        if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            var result = await _apiKeyValidator.ValidateAsync(apiKey!);
            if (!result.IsValid)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { Error = "Invalid API Key" });
                return;
            }
            
            // Add claims from API key
            var identity = new ClaimsIdentity(result.Claims, "ApiKey");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}
```

---

## 9. Monitoring và Metrics

```csharp
// Gateway Metrics
public class GatewayMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Counter<long> RequestCounter = 
        Metrics.CreateCounter<long>("gateway_requests_total", "Total requests");
    private static readonly Histogram<double> RequestDuration =
        Metrics.CreateHistogram<double>("gateway_request_duration_seconds", "Request duration");
    private static readonly Counter<long> ErrorCounter =
        Metrics.CreateCounter<long>("gateway_errors_total", "Total errors");

    public GatewayMetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        
        RequestCounter.Add(1, new KeyValuePair<string, object?>("path", path),
            new KeyValuePair<string, object?>("method", method));
        
        var sw = Stopwatch.StartNew();
        
        try
        {
            await _next(context);
            
            sw.Stop();
            RequestDuration.Record(sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("status", context.Response.StatusCode.ToString()));
        }
        catch (Exception)
        {
            ErrorCounter.Add(1, new KeyValuePair<string, object?>("path", path));
            throw;
        }
    }
}

// Circuit Breaker cho backend services
public class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<string, CircuitBreakerState> _circuits = new();

    public CircuitBreakerMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var cluster = GetTargetCluster(context);
        
        if (cluster != null && IsCircuitOpen(cluster))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Service Unavailable",
                Message = $"The {cluster} service is temporarily unavailable"
            });
            return;
        }
        
        await _next(context);
    }
    
    private bool IsCircuitOpen(string cluster)
    {
        return _circuits.TryGetValue(cluster, out var state) && 
               state.IsOpen && 
               DateTime.UtcNow < state.OpenUntil;
    }
    
    private string? GetTargetCluster(HttpContext context) => null; // Implementation
}

public record CircuitBreakerState(bool IsOpen, DateTime OpenUntil, int FailureCount);
```

---

## 10. Docker Compose cho Gateway

```yaml
# docker-compose.yml
version: '3.8'

services:
  api-gateway:
    build:
      context: ./src/ApiGateway
      dockerfile: Dockerfile
    ports:
      - "80:80"
      - "443:443"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:80
    depends_on:
      - order-service
      - product-service
      - user-service
    networks:
      - ecommerce-network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  order-service:
    build: ./src/Services/OrderService
    ports:
      - "5001"
    environment:
      - ConnectionStrings__OrdersDb=Host=orders-db;Database=orders;Username=postgres;Password=secret
    networks:
      - ecommerce-network

  product-service:
    build: ./src/Services/ProductService
    ports:
      - "5002"
    deploy:
      replicas: 3  # 3 instances cho load balancing
    networks:
      - ecommerce-network

  user-service:
    build: ./src/Services/UserService
    ports:
      - "5003"
    networks:
      - ecommerce-network

networks:
  ecommerce-network:
    driver: bridge
```

---

## Best Practices

### 1. Timeout Configuration

```csharp
// Set appropriate timeouts
"HttpRequest": {
    "ActivityTimeout": "00:01:40",  // 100 seconds max
    "Timeout": "00:00:30"           // 30 seconds per request
}
```

### 2. Retry Configuration

```csharp
// Chỉ retry idempotent requests (GET, PUT, DELETE)
// KHÔNG retry POST (có thể gây duplicate)
context.AddRequestTransform(async transformContext =>
{
    var method = transformContext.HttpContext.Request.Method;
    if (method == "POST" || method == "PATCH")
    {
        // Disable retry cho non-idempotent requests
        transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
            "X-No-Retry", "true");
    }
});
```

### 3. Security Headers

```csharp
app.Use(async (context, next) =>
{
    // Xóa sensitive headers từ backend response
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Remove("Server");
        context.Response.Headers.Remove("X-Powered-By");
        context.Response.Headers.Remove("X-AspNet-Version");
        return Task.CompletedTask;
    });
    
    await next();
});
```

### 4. Graceful Shutdown

```csharp
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    // Drain connections trước khi shutdown
    Thread.Sleep(TimeSpan.FromSeconds(5));
});
```

---

## Tổng Kết

YARP là giải pháp API Gateway tuyệt vời cho .NET ecosystem:

| Tính năng | YARP | Ocelot | Kong | Nginx |
|-----------|------|--------|------|-------|
| .NET Native | ✅ | ✅ | ❌ | ❌ |
| Dynamic config | ✅ | Partial | ✅ | ❌ |
| Performance | Excellent | Good | Excellent | Excellent |
| gRPC support | ✅ | Limited | ✅ | ✅ |
| Customization | Code-based | Config | Plugin | Module |
| Learning curve | Low | Low | Medium | Medium |

**YARP phù hợp khi:**
- ✅ Microservices .NET thuần túy
- ✅ Cần customize logic phức tạp
- ✅ Team quen với C#
- ✅ Cần performance tốt
- ✅ Dynamic routing requirements
