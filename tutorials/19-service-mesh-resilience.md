# Bài 19: Service Mesh với Consul & Service Discovery trong .NET

## 1. Service Mesh là gì và tại sao cần nó?

Trong kiến trúc microservices, các service cần giao tiếp với nhau. Khi số lượng service tăng lên, việc quản lý kết nối, retry, timeout, load balancing, và bảo mật trở nên phức tạp. **Service Mesh** giải quyết vấn đề này bằng cách tách logic giao tiếp ra khỏi business logic.

### Vấn đề không có Service Mesh

```
OrderService → (hardcoded URL) → ProductService
             → (hardcoded URL) → InventoryService
```

- Nếu ProductService thay đổi địa chỉ IP, phải cập nhật tất cả service gọi đến nó
- Không có retry tự động khi service tạm thời lỗi
- Không có load balancing giữa nhiều instance
- Khó theo dõi request đi qua nhiều service

### Với Service Mesh

```
OrderService → [Sidecar Proxy] → [Mesh Control Plane] → [Sidecar Proxy] → ProductService
```

Service Mesh cung cấp:
- **Service Discovery**: Tự động tìm địa chỉ service
- **Load Balancing**: Phân phối traffic giữa các instance
- **Health Checking**: Loại bỏ instance không healthy
- **Circuit Breaking**: Ngắt kết nối khi service lỗi liên tục
- **Observability**: Metrics, tracing tự động
- **Security**: mTLS giữa các service

## 2. Service Discovery: Client-side vs Server-side

### Client-side Discovery

Client tự query registry để lấy danh sách instance, rồi tự chọn instance để gọi.

```
Client → Query Registry → [Instance1, Instance2, Instance3]
Client → Chọn Instance2 → Gọi trực tiếp
```

**Ưu điểm**: Client kiểm soát load balancing logic  
**Nhược điểm**: Client phải implement discovery logic, phụ thuộc vào registry client library

### Server-side Discovery

Client gọi đến một load balancer/router, router tự tìm instance phù hợp.

```
Client → Router/LB → Query Registry → Chọn instance → Forward request
```

**Ưu điểm**: Client đơn giản, không cần biết về registry  
**Nhược điểm**: Thêm một hop mạng, load balancer là single point of failure nếu không HA

## 3. Consul - HashiCorp Service Mesh

Consul là một trong những giải pháp service mesh phổ biến nhất, cung cấp:
- Service discovery (DNS và HTTP API)
- Health checking
- Key-Value store
- Service segmentation (intentions)
- Connect (mTLS sidecar proxies)

### Cài đặt Consul với Docker

```yaml
# docker-compose.yml
version: '3.8'

services:
  consul:
    image: hashicorp/consul:1.17
    container_name: consul
    ports:
      - "8500:8500"    # HTTP API và UI
      - "8600:8600/udp" # DNS
    command: >
      agent -server -bootstrap-expect=1
      -ui -client=0.0.0.0
      -bind=0.0.0.0
    environment:
      - CONSUL_BIND_INTERFACE=eth0

  order-service:
    build: ./OrderService
    ports:
      - "5001:5001"
    environment:
      - CONSUL_HOST=consul
      - SERVICE_NAME=order-service
      - SERVICE_PORT=5001
    depends_on:
      - consul

  product-service:
    build: ./ProductService
    ports:
      - "5002:5002"
    environment:
      - CONSUL_HOST=consul
      - SERVICE_NAME=product-service
      - SERVICE_PORT=5002
    depends_on:
      - consul
```

## 4. Tích hợp Consul với .NET

### Cài đặt NuGet packages

```bash
dotnet add package Consul
dotnet add package Microsoft.Extensions.Hosting
```

### ConsulServiceRegistration helper

```csharp
// Infrastructure/ConsulServiceRegistration.cs
using Consul;

public static class ConsulServiceRegistration
{
    public static IServiceCollection AddConsulServiceRegistration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IConsulClient, ConsulClient>(p =>
        {
            var consulHost = configuration["Consul:Host"] ?? "localhost";
            var consulPort = int.Parse(configuration["Consul:Port"] ?? "8500");
            
            return new ConsulClient(config =>
            {
                config.Address = new Uri($"http://{consulHost}:{consulPort}");
            });
        });

        return services;
    }

    public static IApplicationBuilder UseConsulServiceRegistration(
        this IApplicationBuilder app,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration)
    {
        var consulClient = app.ApplicationServices.GetRequiredService<IConsulClient>();
        
        var serviceName = configuration["Service:Name"] 
            ?? throw new InvalidOperationException("Service:Name is required");
        var servicePort = int.Parse(configuration["Service:Port"] ?? "5000");
        var serviceHost = configuration["Service:Host"] ?? "localhost";

        var serviceId = $"{serviceName}-{Guid.NewGuid():N}";

        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = serviceName,
            Address = serviceHost,
            Port = servicePort,
            Tags = new[] { "dotnet", "v1" },
            Check = new AgentServiceCheck
            {
                HTTP = $"http://{serviceHost}:{servicePort}/health",
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30)
            }
        };

        // Đăng ký khi app start
        lifetime.ApplicationStarted.Register(async () =>
        {
            await consulClient.Agent.ServiceRegister(registration);
            Console.WriteLine($"Registered {serviceName} with Consul (ID: {serviceId})");
        });

        // Hủy đăng ký khi app stop
        lifetime.ApplicationStopping.Register(async () =>
        {
            await consulClient.Agent.ServiceDeregister(serviceId);
            Console.WriteLine($"Deregistered {serviceName} from Consul");
        });

        return app;
    }
}
```

## 5. OrderService - Service đặt hàng

```csharp
// OrderService/Program.cs
using Consul;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddConsulServiceRegistration(builder.Configuration);

// HttpClient để gọi ProductService qua Consul
builder.Services.AddHttpClient("ProductService", (sp, client) =>
{
    // Sẽ resolve URL động từ Consul
    client.DefaultRequestHeaders.Add("X-Service-Name", "order-service");
});

builder.Services.AddSingleton<ConsulServiceDiscovery>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Health check endpoint (Consul sẽ ping endpoint này)
app.MapHealthChecks("/health");

// Endpoint tạo đơn hàng
app.MapPost("/orders", async (
    CreateOrderRequest request,
    ConsulServiceDiscovery discovery,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger) =>
{
    var correlationId = Guid.NewGuid().ToString();
    logger.LogInformation("Creating order {CorrelationId} for product {ProductId}", 
        correlationId, request.ProductId);

    // Discover ProductService từ Consul
    var productServiceUrl = await discovery.GetServiceUrlAsync("product-service");
    if (productServiceUrl == null)
    {
        return Results.Problem("ProductService is not available");
    }

    var httpClient = httpClientFactory.CreateClient("ProductService");
    httpClient.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

    // Gọi ProductService để lấy thông tin sản phẩm
    var productResponse = await httpClient.GetAsync($"{productServiceUrl}/products/{request.ProductId}");
    
    if (!productResponse.IsSuccessStatusCode)
    {
        return Results.Problem($"Failed to get product {request.ProductId}");
    }

    var productJson = await productResponse.Content.ReadAsStringAsync();
    var product = JsonSerializer.Deserialize<ProductDto>(productJson, 
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    var order = new Order
    {
        Id = Guid.NewGuid(),
        ProductId = request.ProductId,
        ProductName = product?.Name ?? "Unknown",
        Quantity = request.Quantity,
        UnitPrice = product?.Price ?? 0,
        TotalPrice = (product?.Price ?? 0) * request.Quantity,
        CreatedAt = DateTime.UtcNow,
        CorrelationId = correlationId
    };

    logger.LogInformation("Order {OrderId} created successfully. CorrelationId: {CorrelationId}", 
        order.Id, correlationId);

    return Results.Created($"/orders/{order.Id}", order);
});

app.MapGet("/orders/{id}", (Guid id) =>
{
    // Simplified - in real app, fetch from database
    return Results.Ok(new { Id = id, Status = "Created" });
});

app.UseConsulServiceRegistration(app.Lifetime, builder.Configuration);

app.Run();

// DTOs và Models
record CreateOrderRequest(Guid ProductId, int Quantity);

record ProductDto(Guid Id, string Name, decimal Price, int StockQuantity);

class Order
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CorrelationId { get; set; } = "";
}
```

## 6. Service Discovery Implementation

```csharp
// Shared/ConsulServiceDiscovery.cs
using Consul;

public class ConsulServiceDiscovery
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulServiceDiscovery> _logger;
    private readonly Random _random = new();

    public ConsulServiceDiscovery(
        IConsulClient consulClient,
        ILogger<ConsulServiceDiscovery> logger)
    {
        _consulClient = consulClient;
        _logger = logger;
    }

    public async Task<string?> GetServiceUrlAsync(string serviceName)
    {
        try
        {
            // Lấy tất cả healthy instances của service
            var services = await _consulClient.Health.Service(
                serviceName, 
                tag: null, 
                passingOnly: true); // Chỉ lấy instance đang healthy

            if (!services.Response.Any())
            {
                _logger.LogWarning("No healthy instances found for service: {ServiceName}", serviceName);
                return null;
            }

            // Round-robin load balancing đơn giản
            var instance = services.Response[_random.Next(services.Response.Length)];
            var url = $"http://{instance.Service.Address}:{instance.Service.Port}";
            
            _logger.LogDebug("Resolved {ServiceName} to {Url}", serviceName, url);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering service: {ServiceName}", serviceName);
            return null;
        }
    }

    public async Task<List<ServiceInstance>> GetAllInstancesAsync(string serviceName)
    {
        var services = await _consulClient.Health.Service(
            serviceName, 
            tag: null, 
            passingOnly: true);

        return services.Response.Select(s => new ServiceInstance
        {
            Id = s.Service.ID,
            Name = s.Service.Service,
            Address = s.Service.Address,
            Port = s.Service.Port,
            Tags = s.Service.Tags?.ToList() ?? new List<string>()
        }).ToList();
    }
}

public class ServiceInstance
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public List<string> Tags { get; set; } = new();
}
```

## 7. ProductService - Service quản lý sản phẩm

```csharp
// ProductService/Program.cs
using Consul;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddConsulServiceRegistration(builder.Configuration);
builder.Services.AddHealthChecks();

// Consul KV cho feature flags
builder.Services.AddSingleton<ConsulKVStore>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapHealthChecks("/health");

// Dữ liệu mẫu
var products = new Dictionary<Guid, Product>
{
    [Guid.Parse("11111111-1111-1111-1111-111111111111")] = new Product 
    { 
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "Laptop Dell XPS 15",
        Price = 35000000,
        StockQuantity = 10
    },
    [Guid.Parse("22222222-2222-2222-2222-222222222222")] = new Product 
    { 
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = "iPhone 15 Pro",
        Price = 28000000,
        StockQuantity = 25
    }
};

app.MapGet("/products", async (ConsulKVStore kvStore, ILogger<Program> logger) =>
{
    var correlationId = Guid.NewGuid().ToString();
    
    // Kiểm tra feature flag từ Consul KV
    var showDiscountedPrice = await kvStore.GetValueAsync("features/show-discounted-price");
    var enableDiscount = showDiscountedPrice == "true";

    logger.LogInformation("Listing products. CorrelationId: {CorrelationId}, Discount: {Discount}", 
        correlationId, enableDiscount);

    var result = products.Values.Select(p => new
    {
        p.Id,
        p.Name,
        Price = enableDiscount ? p.Price * 0.9m : p.Price,
        p.StockQuantity,
        IsDiscounted = enableDiscount
    });

    return Results.Ok(result);
});

app.MapGet("/products/{id}", (Guid id, ILogger<Program> logger) =>
{
    if (!products.TryGetValue(id, out var product))
    {
        return Results.NotFound(new { Message = $"Product {id} not found" });
    }

    logger.LogInformation("Retrieved product {ProductId}: {ProductName}", id, product.Name);
    return Results.Ok(product);
});

app.MapGet("/products/{id}/stock", (Guid id) =>
{
    if (!products.TryGetValue(id, out var product))
    {
        return Results.NotFound();
    }
    return Results.Ok(new { ProductId = id, StockQuantity = product.StockQuantity });
});

app.UseConsulServiceRegistration(app.Lifetime, builder.Configuration);

app.Run();

class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
```

## 8. Key-Value Store cho Dynamic Configuration

Consul KV store cho phép lưu trữ và đọc configuration động mà không cần restart service.

```csharp
// Shared/ConsulKVStore.cs
using Consul;
using System.Text;

public class ConsulKVStore
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulKVStore> _logger;

    public ConsulKVStore(IConsulClient consulClient, ILogger<ConsulKVStore> logger)
    {
        _consulClient = consulClient;
        _logger = logger;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        try
        {
            var result = await _consulClient.KV.Get(key);
            if (result.Response?.Value == null) return null;
            
            return Encoding.UTF8.GetString(result.Response.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading KV key: {Key}", key);
            return null;
        }
    }

    public async Task SetValueAsync(string key, string value)
    {
        var kvPair = new KVPair(key)
        {
            Value = Encoding.UTF8.GetBytes(value)
        };

        await _consulClient.KV.Put(kvPair);
        _logger.LogInformation("Set KV {Key} = {Value}", key, value);
    }

    public async Task<Dictionary<string, string>> GetAllWithPrefixAsync(string prefix)
    {
        var result = await _consulClient.KV.List(prefix);
        var dict = new Dictionary<string, string>();

        if (result.Response == null) return dict;

        foreach (var kv in result.Response)
        {
            if (kv.Value != null)
            {
                dict[kv.Key] = Encoding.UTF8.GetString(kv.Value);
            }
        }

        return dict;
    }

    public async Task DeleteAsync(string key)
    {
        await _consulClient.KV.Delete(key);
    }
}
```

### Thiết lập Feature Flags trong Consul

```bash
# Dùng Consul HTTP API để set feature flags
curl -X PUT http://localhost:8500/v1/kv/features/show-discounted-price \
  -d "true"

curl -X PUT http://localhost:8500/v1/kv/config/order-service/max-retry \
  -d "3"

curl -X PUT http://localhost:8500/v1/kv/config/product-service/cache-ttl \
  -d "300"

# Đọc giá trị
curl http://localhost:8500/v1/kv/features/show-discounted-price?raw
```

## 9. Consul Configuration Provider cho .NET

```csharp
// Infrastructure/ConsulConfigurationProvider.cs
using Consul;
using Microsoft.Extensions.Configuration;

public class ConsulConfigurationSource : IConfigurationSource
{
    private readonly string _consulHost;
    private readonly string _keyPrefix;

    public ConsulConfigurationSource(string consulHost, string keyPrefix)
    {
        _consulHost = consulHost;
        _keyPrefix = keyPrefix;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new ConsulConfigurationProvider(_consulHost, _keyPrefix);
    }
}

public class ConsulConfigurationProvider : ConfigurationProvider
{
    private readonly string _consulHost;
    private readonly string _keyPrefix;

    public ConsulConfigurationProvider(string consulHost, string keyPrefix)
    {
        _consulHost = consulHost;
        _keyPrefix = keyPrefix;
    }

    public override void Load()
    {
        var client = new ConsulClient(c => c.Address = new Uri(_consulHost));
        
        var result = client.KV.List(_keyPrefix).GetAwaiter().GetResult();
        
        if (result.Response == null) return;

        foreach (var kv in result.Response)
        {
            var key = kv.Key.Replace(_keyPrefix + "/", "").Replace("/", ":");
            var value = System.Text.Encoding.UTF8.GetString(kv.Value ?? Array.Empty<byte>());
            Data[key] = value;
        }
    }
}

// Extension method
public static class ConsulConfigurationExtensions
{
    public static IConfigurationBuilder AddConsul(
        this IConfigurationBuilder builder,
        string consulHost,
        string keyPrefix)
    {
        return builder.Add(new ConsulConfigurationSource(consulHost, keyPrefix));
    }
}
```

### Sử dụng trong Program.cs

```csharp
// Program.cs - thêm Consul configuration source
builder.Configuration.AddConsul(
    consulHost: "http://localhost:8500",
    keyPrefix: "config/order-service"
);
```

## 10. Load Balancing Strategies

```csharp
// Infrastructure/LoadBalancers.cs

public interface ILoadBalancer
{
    AgentService? Select(IEnumerable<AgentService> services);
}

// Round-robin load balancer
public class RoundRobinLoadBalancer : ILoadBalancer
{
    private int _currentIndex = -1;

    public AgentService? Select(IEnumerable<AgentService> services)
    {
        var serviceList = services.ToList();
        if (!serviceList.Any()) return null;

        var index = Interlocked.Increment(ref _currentIndex) % serviceList.Count;
        return serviceList[index];
    }
}

// Random load balancer
public class RandomLoadBalancer : ILoadBalancer
{
    private readonly Random _random = new();

    public AgentService? Select(IEnumerable<AgentService> services)
    {
        var serviceList = services.ToList();
        if (!serviceList.Any()) return null;

        return serviceList[_random.Next(serviceList.Count)];
    }
}

// Least connections (cần tracking external)
public class LeastConnectionsLoadBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, int> _connectionCounts = new();

    public AgentService? Select(IEnumerable<AgentService> services)
    {
        var serviceList = services.ToList();
        if (!serviceList.Any()) return null;

        return serviceList
            .OrderBy(s => _connectionCounts.GetOrAdd(s.ID, 0))
            .First();
    }

    public void IncrementConnections(string serviceId) =>
        _connectionCounts.AddOrUpdate(serviceId, 1, (_, count) => count + 1);

    public void DecrementConnections(string serviceId) =>
        _connectionCounts.AddOrUpdate(serviceId, 0, (_, count) => Math.Max(0, count - 1));
}
```

## 11. Correlation IDs và Request Tracing

Correlation ID giúp theo dõi một request khi nó đi qua nhiều service.

```csharp
// Middleware/CorrelationIdMiddleware.cs
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // Lấy correlation ID từ header hoặc tạo mới
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                           ?? Guid.NewGuid().ToString();

        // Thêm vào response header
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Lưu vào Items để các middleware sau dùng
        context.Items["CorrelationId"] = correlationId;

        // Thêm vào log context
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}

// Extension method
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
```

```csharp
// HttpClient DelegatingHandler để truyền Correlation ID
public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString()
                           ?? Guid.NewGuid().ToString();

        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        return await base.SendAsync(request, cancellationToken);
    }
}

// Đăng ký trong Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
builder.Services.AddHttpClient("ProductService")
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
```

## 12. appsettings.json cho các service

```json
// OrderService/appsettings.json
{
  "Consul": {
    "Host": "localhost",
    "Port": "8500"
  },
  "Service": {
    "Name": "order-service",
    "Host": "localhost",
    "Port": "5001"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

```json
// ProductService/appsettings.json
{
  "Consul": {
    "Host": "localhost",
    "Port": "8500"
  },
  "Service": {
    "Name": "product-service",
    "Host": "localhost",
    "Port": "5002"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

## 13. Consul DNS Discovery

Consul cung cấp DNS interface để discover service mà không cần Consul client library.

```bash
# Query DNS cho product-service
# Format: <service-name>.service.consul
dig @127.0.0.1 -p 8600 product-service.service.consul

# Query với tag
dig @127.0.0.1 -p 8600 v1.product-service.service.consul

# Query SRV record (bao gồm port)
dig @127.0.0.1 -p 8600 product-service.service.consul SRV
```

```csharp
// Dùng DNS trong HttpClient (khi có Consul DNS setup)
builder.Services.AddHttpClient("ProductService", client =>
{
    // Consul DNS sẽ resolve "product-service.service.consul" thành IP:Port
    client.BaseAddress = new Uri("http://product-service.service.consul");
});
```

## 14. Health Checks nâng cao

```csharp
// Health checks tích hợp với Consul
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is running"))
    .AddCheck("database", async () =>
    {
        // Kiểm tra kết nối database
        try
        {
            // await dbContext.Database.CanConnectAsync();
            return HealthCheckResult.Healthy("Database connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Database connection failed: {ex.Message}");
        }
    })
    .AddCheck("consul", async (sp, ct) =>
    {
        var consulClient = sp.GetRequiredService<IConsulClient>();
        try
        {
            var status = await consulClient.Status.Leader(ct);
            return string.IsNullOrEmpty(status.Response)
                ? HealthCheckResult.Unhealthy("No Consul leader elected")
                : HealthCheckResult.Healthy($"Consul leader: {status.Response}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Cannot reach Consul: {ex.Message}");
        }
    });

// Map health check với response chi tiết
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(response));
    }
});
```

## 15. Chạy và kiểm tra

```bash
# Khởi động infrastructure
docker-compose up -d consul

# Chờ Consul khởi động
sleep 5

# Set feature flags
curl -X PUT http://localhost:8500/v1/kv/features/show-discounted-price -d "false"
curl -X PUT http://localhost:8500/v1/kv/config/order-service/max-retry -d "3"

# Khởi động services
dotnet run --project OrderService
dotnet run --project ProductService

# Kiểm tra đăng ký trong Consul UI
open http://localhost:8500/ui

# Test API
# Lấy danh sách sản phẩm
curl http://localhost:5002/products

# Tạo đơn hàng
curl -X POST http://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": "11111111-1111-1111-1111-111111111111", "quantity": 2}'

# Bật discount feature flag
curl -X PUT http://localhost:8500/v1/kv/features/show-discounted-price -d "true"

# Kiểm tra lại - giá đã giảm 10%
curl http://localhost:5002/products
```

## 16. Tóm tắt

| Tính năng | Consul | Alternatives |
|-----------|--------|--------------|
| Service Discovery | ✅ HTTP API + DNS | etcd, ZooKeeper, Eureka |
| Health Checking | ✅ HTTP, TCP, Script | Kubernetes readiness probes |
| Key-Value Store | ✅ Built-in | Redis, etcd |
| Service Mesh | ✅ Consul Connect | Istio, Linkerd |
| Multi-datacenter | ✅ Native | Requires extra config |

### Best Practices

1. **Luôn implement health check** - Consul cần endpoint `/health` để kiểm tra service còn sống
2. **Deregister khi shutdown** - Đăng ký `ApplicationStopping` để tự động xóa khỏi Consul
3. **Sử dụng tags** để phân biệt version, environment
4. **Circuit breaker** kết hợp với service discovery để tăng resilience
5. **Correlation ID** truyền qua tất cả service calls để dễ debug
6. **Feature flags** trong Consul KV giúp rollout tính năng mà không cần deploy lại
