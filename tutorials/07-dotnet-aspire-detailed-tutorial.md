# .NET Aspire từ cơ bản đến thực chiến: xây dựng distributed application có observability, orchestration và service discovery tốt hơn

## 1. .NET Aspire là gì và tại sao nó đáng học

`.NET Aspire` là bộ công cụ dành cho việc xây dựng, chạy, kết nối và quan sát các ứng dụng .NET dạng distributed application, đặc biệt hữu ích khi hệ thống của bạn không còn là một web app đơn lẻ mà đã có nhiều service, queue, cache, database, worker và dependency ngoài.

Nếu nhìn theo góc độ thực tế, Aspire giải quyết một cụm vấn đề mà các team .NET thường gặp:

- local development của nhiều service rất rối
- cấu hình connection string, endpoint, secret, port dễ lệch giữa các project
- observability thường được thêm muộn, thiếu log correlation, thiếu tracing
- app khởi động lên nhưng không rõ service nào phụ thuộc service nào
- chạy thử một hệ thống microservices trên máy dev thường tốn rất nhiều shell script, docker compose, profile launch, env var

Aspire không phải là một framework thay thế ASP.NET Core, cũng không phải Kubernetes thu nhỏ. Nó là một lớp tooling + conventions + packages giúp bạn:

- tổ chức solution tốt hơn cho distributed app
- định nghĩa topology của hệ thống ở cấp application
- wiring resource như Redis, Postgres, RabbitMQ, SQL Server, Azure Storage thuận tiện hơn
- inject service discovery và configuration vào từng project
- bật OpenTelemetry, health checks, metrics, tracing theo kiểu opinionated nhưng dễ dùng
- có dashboard để nhìn toàn cảnh local app

Nói ngắn gọn, Aspire làm cho trải nghiệm phát triển distributed app trong .NET bớt thủ công và nhất quán hơn rất nhiều.

## 2. Aspire phù hợp trong những trường hợp nào

Aspire đặc biệt đáng giá khi bạn có một trong các bối cảnh sau:

1. **Nhiều project chạy cùng lúc**
   - `ApiGateway`
   - `Catalog.Api`
   - `Ordering.Api`
   - `Basket.Api`
   - `Worker`
   - `Redis`
   - `Postgres`

2. **Muốn local environment gần giống production hơn**
   Bạn muốn chạy hệ thống có cache, database, queue, tracing, dashboard ngay trên máy dev mà không phải tự viết rất nhiều script.

3. **Cần observability từ sớm**
   Thay vì đợi tới khi deploy mới nghĩ đến metrics và traces, Aspire khuyến khích bạn đưa telemetry vào từ đầu.

4. **Team muốn có chuẩn tổ chức solution**
   Một solution Aspire thường có:
   - `AppHost`: nơi định nghĩa toàn bộ application graph
   - `ServiceDefaults`: nơi gom các mặc định cross-cutting như OpenTelemetry, health checks, resilience
   - nhiều project service/web/worker thực hiện business logic

5. **Bạn đang build microservice hoặc modular monolith có nhiều dependency**
   Aspire không bắt buộc phải là microservices thật sự. Ngay cả modular monolith với vài background worker và vài resource cũng hưởng lợi.

## 3. Aspire không phải cái gì

Đây là phần rất quan trọng vì nhiều người kỳ vọng sai:

- Aspire **không thay thế** kiến trúc hệ thống cho bạn
- Aspire **không tự động biến code dở thành microservice tốt**
- Aspire **không buộc phải dùng cloud cụ thể nào**
- Aspire **không chỉ dành cho production**. Giá trị lớn nhất thường đến ngay trong local development
- Aspire **không phải service mesh**
- Aspire **không phải message broker**
- Aspire **không phải orchestrator production như Kubernetes**

Hãy xem Aspire như “application composition + developer experience + observability defaults” cho distributed app trong .NET.

## 4. Các khối chính trong một solution Aspire

Một solution Aspire thường có ba phần nổi bật.

### 4.1. AppHost

Đây là project trung tâm để mô tả toàn bộ ứng dụng. Bạn khai báo:

- có những project nào
- project nào phụ thuộc project nào
- resource nào cần chạy
- resource nào được inject vào service nào
- external endpoint nào được expose

Ví dụ trực quan:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var catalogDb = postgres.AddDatabase("catalogdb");
var redis = builder.AddRedis("redis");

var catalogApi = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WithReference(redis);

builder.AddProject<Projects.WebApp>("webapp")
    .WithReference(catalogApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

Nhìn vào đoạn này, bạn đã hiểu được topology cơ bản của app.

### 4.2. ServiceDefaults

Đây là nơi gom các cấu hình dùng chung giữa nhiều service:

- OpenTelemetry
- health checks
- service discovery
- HTTP client defaults
- resilience policies

Ví dụ extension method thường gặp:

```csharp
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }
}
```

Điểm hay là mọi service chỉ cần gọi:

```csharp
builder.AddServiceDefaults();
```

thay vì mỗi project tự config lại từ đầu.

### 4.3. Các application project

Đây là nơi chứa business logic thật:

- ASP.NET Core Web API
- Blazor app
- MVC app
- Worker Service
- gRPC service

Aspire không chiếm chỗ của business code. Nó hỗ trợ “wiring” và “operability”.

## 5. Tạo solution Aspire đầu tiên

Một flow phổ biến:

```bash
dotnet new aspire-starter -n AspireShop
cd AspireShop
```

Sau đó bạn thường thấy các project kiểu:

- `AspireShop.AppHost`
- `AspireShop.ServiceDefaults`
- `AspireShop.ApiService`
- `AspireShop.Web`

### 5.1. Vai trò từng project

#### AppHost
Chạy như conductor. Nó không chứa business logic chính, mà mô tả hệ thống.

#### ServiceDefaults
Chứa các extension dùng chung cho observability và các mặc định nền tảng.

#### ApiService
Một service backend, ví dụ trả dữ liệu thời tiết, đơn hàng, catalog.

#### Web
Frontend hoặc BFF gọi sang backend service.

## 6. Kiến trúc demo trong bài này

Trong tutorial này, ta sẽ đi qua một hệ thống e-commerce thu gọn gồm:

- `Store.Web`: web frontend hoặc BFF
- `Catalog.Api`: API quản lý và đọc sản phẩm
- `Basket.Api`: API giỏ hàng
- `Ordering.Api`: tạo đơn hàng
- `Ordering.Worker`: xử lý nền sau khi có đơn
- `Postgres`: lưu catalog và order
- `Redis`: lưu cache và basket
- `RabbitMQ`: phát event giữa ordering và worker

Luồng nghiệp vụ:

1. User mở `Store.Web`
2. Web gọi `Catalog.Api` để lấy sản phẩm
3. User thêm hàng vào giỏ, web gọi `Basket.Api`
4. Khi checkout, web gọi `Ordering.Api`
5. `Ordering.Api` ghi order vào database và publish event `OrderCreated`
6. `Ordering.Worker` consume event và gửi email hoặc cập nhật inventory

Điểm quan trọng là Aspire giúp ta:

- chạy toàn bộ topology này trong local
- inject connection string và endpoint đúng chỗ
- có dashboard để xem log, trace, health

## 7. AppHost: mô tả topology của hệ thống

Ví dụ `Program.cs` của `Store.AppHost`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "16")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);

var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var catalogApi = builder.AddProject<Projects.Store_Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WithReference(redis);

var basketApi = builder.AddProject<Projects.Store_Basket_Api>("basket-api")
    .WithReference(redis);

var orderingApi = builder.AddProject<Projects.Store_Ordering_Api>("ordering-api")
    .WithReference(orderingDb)
    .WithReference(rabbitMq);

var orderingWorker = builder.AddProject<Projects.Store_Ordering_Worker>("ordering-worker")
    .WithReference(orderingDb)
    .WithReference(rabbitMq);

builder.AddProject<Projects.Store_Web>("store-web")
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithReference(orderingApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### 7.1. Ý nghĩa của `WithReference`

`WithReference` là một ý niệm cực kỳ quan trọng.

Khi bạn viết:

```csharp
.WithReference(redis)
```

Aspire hiểu rằng project này cần dependency `redis`. Từ đó nó có thể:

- cung cấp configuration phù hợp
- thiết lập service discovery
- đảm bảo dependency được khởi động trước hoặc có quan hệ rõ ràng hơn trong application model

Tương tự với project-to-project reference:

```csharp
.WithReference(catalogApi)
```

Điều đó cho phép web app có thể resolve endpoint của `catalog-api` mà không hard-code `https://localhost:7234`.

### 7.2. Resource naming rất quan trọng

Tên như `catalog-api`, `redis`, `ordering-api` không chỉ để đẹp. Nó thường trở thành một phần của service discovery và configuration. Hãy đặt tên ổn định, rõ nghĩa và tránh đổi lung tung khi team đã dùng rộng rãi.

## 8. Service discovery trong Aspire

Một trong những lợi ích rõ nhất của Aspire là giảm hard-code URL giữa các service.

Thay vì viết:

```csharp
builder.Services.AddHttpClient("catalog", client =>
{
    client.BaseAddress = new Uri("https://localhost:7501");
});
```

bạn có thể dùng service discovery.

Trong `Store.Web`:

```csharp
builder.AddServiceDefaults();

builder.Services.AddHttpClient<CatalogClient>(client =>
{
    client.BaseAddress = new Uri("https+http://catalog-api");
});
```

`https+http://catalog-api` là kiểu URI special dành cho discovery. Aspire sẽ resolve đúng endpoint runtime.

Ví dụ `CatalogClient`:

```csharp
public class CatalogClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await httpClient.GetFromJsonAsync<List<ProductDto>>("/products", cancellationToken);
        return products ?? [];
    }
}
```

### 8.1. Lợi ích thực tế

- không còn phụ thuộc port local cố định
- đổi launch profile ít làm vỡ service khác
- cùng một code base có thể chạy linh hoạt hơn giữa local và môi trường khác
- code client nhìn sạch hơn

## 9. ServiceDefaults: gom observability và health checks

Đây là phần rất nên làm kỹ vì nó tạo ra giá trị lâu dài.

Ví dụ `Extensions.cs` trong `ServiceDefaults`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Store.ServiceDefaults;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new()
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
        }

        return app;
    }
}
```

Và trong từng service, bạn chỉ cần:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();
app.Run();
```

### 9.1. Vì sao cách gom này đáng giá

Nếu mỗi service tự cấu hình telemetry theo một kiểu khác nhau, bạn sẽ nhanh chóng có một mớ hỗn loạn:

- service A có tracing nhưng không có runtime metrics
- service B có health checks nhưng route khác
- service C không có resilience handler cho HttpClient

`ServiceDefaults` giúp bạn có chuẩn thống nhất.

## 10. Kết nối database bằng Aspire

Aspire hỗ trợ nhiều loại resource như PostgreSQL, SQL Server, Redis, RabbitMQ. Với database, lợi ích lớn nhất là giảm việc tự quản lý connection string local.

### 10.1. Khai báo Postgres ở AppHost

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "16")
    .WithPgAdmin();

var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");
```

### 10.2. Gắn database vào service

```csharp
var catalogApi = builder.AddProject<Projects.Store_Catalog_Api>("catalog-api")
    .WithReference(catalogDb);
```

Khi đó `Catalog.Api` có thể đọc connection string từ configuration theo key mà Aspire đưa vào.

Ví dụ trong `Catalog.Api`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddNpgsqlDataSource(builder.Configuration.GetConnectionString("catalogdb"));
builder.Services.AddDbContext<CatalogDbContext>((sp, options) =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource);
});
```

### 10.3. Entity Framework Core với migration

```csharp
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasColumnType("numeric(18,2)");
        });
    }
}
```

Khởi tạo migration:

```bash
dotnet ef migrations add InitialCreate --project Store.Catalog.Api --startup-project Store.Catalog.Api
```

Apply migration khi app khởi động:

```csharp
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
await dbContext.Database.MigrateAsync();
```

### 10.4. Lưu ý production

Trong production, bạn không nhất thiết dùng container database do Aspire provision theo cách local. Nhưng mô hình dependency và config resolution vẫn rất hữu ích.

## 11. Kết nối Redis

Redis thường dùng cho:

- distributed cache
- session
- rate limiting
- basket/cart
- pub/sub đơn giản

Trong AppHost:

```csharp
var redis = builder.AddRedis("redis");
```

Gắn vào `Basket.Api`:

```csharp
var basketApi = builder.AddProject<Projects.Store_Basket_Api>("basket-api")
    .WithReference(redis);
```

Trong `Basket.Api`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("redis");
});

builder.Services.AddScoped<IBasketRepository, RedisBasketRepository>();
```

Repository ví dụ:

```csharp
public interface IBasketRepository
{
    Task<BasketDto?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task SaveAsync(BasketDto basket, CancellationToken cancellationToken = default);
}

public class RedisBasketRepository(IDistributedCache cache) : IBasketRepository
{
    public async Task<BasketDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var json = await cache.GetStringAsync(GetKey(userId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<BasketDto>(json);
    }

    public async Task SaveAsync(BasketDto basket, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(basket);
        await cache.SetStringAsync(GetKey(basket.UserId), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
        }, cancellationToken);
    }

    private static string GetKey(string userId) => $"basket:{userId}";
}
```

## 12. Kết nối message broker với RabbitMQ

Distributed app thực tế thường cần event-driven communication. Aspire giúp phần wiring dễ hơn.

Trong AppHost:

```csharp
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();
```

Gắn vào `Ordering.Api` và `Ordering.Worker`:

```csharp
var orderingApi = builder.AddProject<Projects.Store_Ordering_Api>("ordering-api")
    .WithReference(rabbitMq)
    .WithReference(orderingDb);

var orderingWorker = builder.AddProject<Projects.Store_Ordering_Worker>("ordering-worker")
    .WithReference(rabbitMq)
    .WithReference(orderingDb);
```

### 12.1. Publish event từ API

Ví dụ dùng MassTransit hoặc RabbitMQ client trực tiếp. Với tutorial này, ta minh họa bằng MassTransit vì phổ biến hơn.

```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
    });
});
```

Model event:

```csharp
public record OrderCreated(Guid OrderId, string CustomerEmail, decimal TotalAmount);
```

Endpoint checkout:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrderingDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    CancellationToken cancellationToken) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerEmail = request.CustomerEmail,
        TotalAmount = request.Items.Sum(x => x.UnitPrice * x.Quantity),
        CreatedAtUtc = DateTime.UtcNow,
        Status = "Pending"
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);

    await publishEndpoint.Publish(new OrderCreated(order.Id, order.CustomerEmail, order.TotalAmount), cancellationToken);

    return Results.Accepted($"/orders/{order.Id}", new { order.Id });
});
```

### 12.2. Consume event trong worker

```csharp
public class OrderCreatedConsumer(OrderingDbContext dbContext, ILogger<OrderCreatedConsumer> logger)
    : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        logger.LogInformation("Received OrderCreated event for {OrderId}", context.Message.OrderId);

        var order = await dbContext.Orders.FindAsync(context.Message.OrderId);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found", context.Message.OrderId);
            return;
        }

        order.Status = "Processing";
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Order {OrderId} marked as Processing", order.Id);
    }
}
```

Cấu hình worker:

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureEndpoints(context);
    });
});
```

### 12.3. Giá trị của Aspire ở đây là gì

Aspire không thay MassTransit, không thay RabbitMQ. Nhưng nó giúp resource và configuration nhất quán. Team mới vào project sẽ dễ hiểu hơn: broker nào đang chạy, service nào dùng broker đó.

## 13. Xây dựng Catalog API hoàn chỉnh hơn

Dưới đây là một ví dụ API tương đối thực chiến.

### 13.1. Domain model

```csharp
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Category { get; set; } = string.Empty;
}
```

### 13.2. DTO

```csharp
public record ProductDto(
    Guid Id,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    string Category);

public record CreateProductRequest(
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    string Category);
```

### 13.3. Endpoints

```csharp
app.MapGet("/products", async (CatalogDbContext dbContext, CancellationToken cancellationToken) =>
{
    var products = await dbContext.Products
        .OrderBy(x => x.Name)
        .Select(x => new ProductDto(x.Id, x.Name, x.Sku, x.Price, x.StockQuantity, x.Category))
        .ToListAsync(cancellationToken);

    return Results.Ok(products);
});

app.MapGet("/products/{id:guid}", async (Guid id, CatalogDbContext dbContext, CancellationToken cancellationToken) =>
{
    var product = await dbContext.Products
        .Where(x => x.Id == id)
        .Select(x => new ProductDto(x.Id, x.Name, x.Sku, x.Price, x.StockQuantity, x.Category))
        .FirstOrDefaultAsync(cancellationToken);

    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.MapPost("/products", async (CreateProductRequest request, CatalogDbContext dbContext, CancellationToken cancellationToken) =>
{
    var product = new Product
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Sku = request.Sku,
        Price = request.Price,
        StockQuantity = request.StockQuantity,
        Category = request.Category
    };

    dbContext.Products.Add(product);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/products/{product.Id}", new ProductDto(
        product.Id,
        product.Name,
        product.Sku,
        product.Price,
        product.StockQuantity,
        product.Category));
});
```

### 13.4. Thêm caching với Redis

```csharp
app.MapGet("/products/cached", async (
    CatalogDbContext dbContext,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    const string cacheKey = "catalog:all";

    var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
    if (!string.IsNullOrWhiteSpace(cached))
    {
        return Results.Content(cached, "application/json");
    }

    var products = await dbContext.Products
        .OrderBy(x => x.Name)
        .Select(x => new ProductDto(x.Id, x.Name, x.Sku, x.Price, x.StockQuantity, x.Category))
        .ToListAsync(cancellationToken);

    var json = JsonSerializer.Serialize(products);

    await cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
    {
        SlidingExpiration = TimeSpan.FromMinutes(5)
    }, cancellationToken);

    return Results.Ok(products);
});
```

Đây là ví dụ đơn giản nhưng thực tế. Khi update product, bạn nên invalid cache tương ứng.

## 14. Basket API và giao tiếp giữa các service

`Basket.Api` có thể expose endpoint:

```csharp
app.MapPost("/basket/{userId}/items", async (
    string userId,
    AddBasketItemRequest request,
    IBasketRepository repository,
    CancellationToken cancellationToken) =>
{
    var basket = await repository.GetAsync(userId, cancellationToken)
        ?? new BasketDto(userId, new List<BasketItemDto>());

    var existing = basket.Items.FirstOrDefault(x => x.ProductId == request.ProductId);
    if (existing is not null)
    {
        existing.Quantity += request.Quantity;
    }
    else
    {
        basket.Items.Add(new BasketItemDto(request.ProductId, request.ProductName, request.UnitPrice, request.Quantity));
    }

    await repository.SaveAsync(basket, cancellationToken);
    return Results.Ok(basket);
});
```

Model:

```csharp
public record AddBasketItemRequest(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public record BasketDto(string UserId, List<BasketItemDto> Items);

public class BasketItemDto
{
    public BasketItemDto(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
```

### 14.1. Web gọi Basket API bằng service discovery

```csharp
builder.Services.AddHttpClient<BasketClient>(client =>
{
    client.BaseAddress = new Uri("https+http://basket-api");
});
```

```csharp
public class BasketClient(HttpClient httpClient)
{
    public async Task AddItemAsync(string userId, AddBasketItemRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/basket/{userId}/items", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
```

## 15. Observability: phần đáng tiền nhất của Aspire

Một distributed app mà không có observability sẽ rất nhanh thành cơn ác mộng. Bạn gặp các câu hỏi quen thuộc:

- request bị chậm ở đâu
- service nào đang fail
- worker có đang consume event không
- service A gọi service B mất bao lâu
- spike CPU đến từ project nào

Aspire giúp bạn đưa observability vào ngay từ ngày đầu.

### 15.1. Metrics

Bạn có thể thu thập:

- request duration
- request count
- outbound HTTP duration
- runtime metrics như GC, memory, threads

Ví dụ custom metric:

```csharp
using System.Diagnostics.Metrics;

public static class OrderingMetrics
{
    public static readonly Meter Meter = new("Store.Ordering");
    public static readonly Counter<int> OrdersCreated = Meter.CreateCounter<int>("store.orders.created");
}
```

Trong endpoint tạo order:

```csharp
OrderingMetrics.OrdersCreated.Add(1,
    new KeyValuePair<string, object?>("source", "api"),
    new KeyValuePair<string, object?>("currency", "USD"));
```

Đăng ký meter:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Store.Ordering")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
    });
```

### 15.2. Tracing

Trace rất quan trọng cho request đi qua nhiều hop.

Ví dụ `Store.Web` gọi `Ordering.Api`, rồi `Ordering.Api` publish event, worker consume event. Dù propagation qua message broker cần thêm attention, bạn vẫn có thể theo dõi flow tốt hơn rất nhiều so với chỉ nhìn log text rời rạc.

Custom activity:

```csharp
using System.Diagnostics;

public static class OrderDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("Store.Ordering.Api");
}
```

Trong service:

```csharp
using var activity = OrderDiagnostics.ActivitySource.StartActivity("CreateOrder");
activity?.SetTag("customer.email", request.CustomerEmail);
activity?.SetTag("items.count", request.Items.Count);
```

Đăng ký activity source:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Store.Ordering.Api")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });
```

### 15.3. Logging correlation

Khi OpenTelemetry và logging được cấu hình đúng, bạn có thể correlate log với trace tốt hơn. Điều này cực kỳ hữu ích khi debug các lỗi intermittent.

Ví dụ log trong order API:

```csharp
logger.LogInformation(
    "Creating order for {CustomerEmail} with {ItemCount} items",
    request.CustomerEmail,
    request.Items.Count);
```

Vấn đề không nằm ở việc log có đẹp không, mà là log đó có gắn context theo request/trace hay không.

## 16. Health checks và readiness

Distributed app không chỉ cần “process còn sống”, mà còn cần “service đã sẵn sàng phục vụ chưa”.

Ví dụ thêm health checks cho Postgres và Redis:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("catalogdb")!)
    .AddRedis(builder.Configuration.GetConnectionString("redis")!);
```

Map endpoint:

```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### 16.1. Vì sao điều này quan trọng

Nếu service khởi động xong nhưng DB chưa kết nối được, traffic vào sớm có thể gây lỗi hàng loạt. Health checks giúp dashboard và môi trường chạy nắm được trạng thái service rõ hơn.

## 17. Resilience với HttpClient

Các service gọi nhau qua HTTP luôn có khả năng lỗi tạm thời. Aspire khuyến khích dùng các default resilience policy tốt hơn.

Trong `ServiceDefaults`:

```csharp
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler();
    http.AddServiceDiscovery();
});
```

Điều này thường thêm retry, timeout, circuit-breaker hoặc các chiến lược tương ứng tùy version package mà bạn dùng.

### 17.1. Nhưng đừng lạm dụng retry

Retry có ích với transient failure, nhưng không nên retry mù quáng với:

- validation error
- business conflict
- non-idempotent operation không có bảo vệ

Ví dụ khi tạo order, nếu endpoint không idempotent và client retry vì timeout, bạn có nguy cơ tạo trùng đơn hàng. Hãy kết hợp resilience với idempotency key nếu workflow quan trọng.

## 18. Dashboard của Aspire

Dashboard là một trong những thứ khiến Aspire được thích nhanh.

Khi chạy AppHost, dashboard thường cho bạn thấy:

- danh sách project và resource đang chạy
- trạng thái từng resource
- logs tập trung
- traces
- metrics
- endpoints
- environment/config liên quan

### 18.1. Dashboard hữu ích ra sao trong local dev

Ví dụ trước đây bạn cần mở:

- 4 cửa sổ terminal
- 1 cửa sổ Docker Desktop
- 1 cửa sổ browser cho RabbitMQ management
- 1 cửa sổ log riêng

Với Aspire, ít nhất bạn có một điểm nhìn trung tâm tốt hơn. Điều này giảm đáng kể friction khi onboarding dev mới.

## 19. Seed data và workflow local development

Một project thực chiến thường cần dữ liệu mẫu để dev/test. Bạn có thể seed data trong startup.

Ví dụ:

```csharp
public static class CatalogSeed
{
    public static async Task SeedAsync(CatalogDbContext dbContext)
    {
        if (await dbContext.Products.AnyAsync())
        {
            return;
        }

        dbContext.Products.AddRange(
            new Product
            {
                Id = Guid.NewGuid(),
                Name = "Mechanical Keyboard",
                Sku = "KB-001",
                Price = 120,
                StockQuantity = 50,
                Category = "Accessories"
            },
            new Product
            {
                Id = Guid.NewGuid(),
                Name = "4K Monitor",
                Sku = "MN-004",
                Price = 399,
                StockQuantity = 20,
                Category = "Display"
            });

        await dbContext.SaveChangesAsync();
    }
}
```

Trong startup:

```csharp
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
await dbContext.Database.MigrateAsync();
await CatalogSeed.SeedAsync(dbContext);
```

### 19.1. Kết hợp với resource persistent

Nếu bạn dùng:

```csharp
.WithLifetime(ContainerLifetime.Persistent)
```

thì container có thể giữ state lâu hơn giữa các lần chạy local. Điều này tiện cho dev, nhưng cũng có thể làm test local “bẩn”. Hãy cân nhắc lúc nào cần clean state.

## 20. Cấu hình môi trường và secrets

Aspire giúp wiring config nhưng không có nghĩa là bạn nên nhét secret vào source code.

Nên phân biệt rõ:

- config dùng cho local dev
- secret cục bộ
- config production
- external secret store

Ví dụ đọc API key từ config chuẩn:

```csharp
var paymentOptions = builder.Configuration.GetSection("PaymentGateway").Get<PaymentGatewayOptions>();
```

Class options:

```csharp
public class PaymentGatewayOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
```

Dù Aspire có giúp quản lý resource reference, bạn vẫn nên dùng secret store phù hợp cho production.

## 21. Thêm một background worker vào hệ thống

Một điểm hay của distributed app là tách work tốn thời gian ra background process. Aspire khiến worker trở thành công dân hạng nhất trong solution.

Ví dụ `Ordering.Worker`:

```csharp
public class InventorySyncWorker(
    ILogger<InventorySyncWorker> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

                var pendingOrders = await dbContext.Orders
                    .Where(x => x.Status == "Processing")
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var order in pendingOrders)
                {
                    logger.LogInformation("Syncing inventory for order {OrderId}", order.Id);
                    order.Status = "Completed";
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while syncing inventory");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
```

Đăng ký:

```csharp
builder.Services.AddHostedService<InventorySyncWorker>();
```

Khi worker được thêm trong AppHost, bạn thấy nó cùng tồn tại với các API khác trong dashboard, thay vì trở thành một process bí ẩn chạy đâu đó.

## 22. Chia nhỏ solution và quản lý dependency

Khi dùng Aspire, rất dễ hưng phấn và kéo mọi thứ vào distributed app. Hãy cẩn thận.

### 22.1. Nên tách service khi

- có vòng đời deploy khác nhau
- scale pattern khác nhau
- ràng buộc domain rõ ràng
- cần fault isolation
- có owner/team khác nhau

### 22.2. Không nên tách service chỉ vì trông “enterprise”

Nếu team nhỏ, domain chưa ổn định, throughput thấp, đôi khi modular monolith + Aspire cho local dependencies đã là đủ. Aspire vẫn hữu ích ngay cả khi bạn chưa đi full microservices.

## 23. Một ví dụ end-to-end rõ ràng

Hãy ghép mọi phần lại thành một flow checkout đơn giản.

### 23.1. Web gọi catalog

```csharp
public class HomePageService(CatalogClient catalogClient)
{
    public Task<IReadOnlyList<ProductDto>> GetFeaturedProductsAsync(CancellationToken cancellationToken = default)
        => catalogClient.GetProductsAsync(cancellationToken);
}
```

### 23.2. Web thêm vào giỏ

```csharp
await basketClient.AddItemAsync(
    userId,
    new AddBasketItemRequest(product.Id, product.Name, product.Price, 1),
    cancellationToken);
```

### 23.3. Web checkout

```csharp
public record CheckoutItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
public record CheckoutRequest(string CustomerEmail, List<CheckoutItem> Items);
```

```csharp
public class OrderingClient(HttpClient httpClient)
{
    public async Task<Guid> CreateOrderAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/orders", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken: cancellationToken);
        return payload!.Id;
    }

    private sealed record CreateOrderResponse(Guid Id);
}
```

### 23.4. Ordering API xử lý

```csharp
app.MapPost("/orders", async (
    CheckoutRequest request,
    OrderingDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("Checkout started for {CustomerEmail}", request.CustomerEmail);

    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerEmail = request.CustomerEmail,
        CreatedAtUtc = DateTime.UtcNow,
        Status = "Pending",
        TotalAmount = request.Items.Sum(x => x.UnitPrice * x.Quantity)
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);

    await publishEndpoint.Publish(new OrderCreated(order.Id, order.CustomerEmail, order.TotalAmount), cancellationToken);

    logger.LogInformation("Checkout completed for order {OrderId}", order.Id);

    return Results.Ok(new { Id = order.Id });
});
```

### 23.5. Worker consume event

```csharp
public class SendConfirmationEmailConsumer(ILogger<SendConfirmationEmailConsumer> logger)
    : IConsumer<OrderCreated>
{
    public Task Consume(ConsumeContext<OrderCreated> context)
    {
        logger.LogInformation(
            "Pretend sending confirmation email to {Email} for order {OrderId}",
            context.Message.CustomerEmail,
            context.Message.OrderId);

        return Task.CompletedTask;
    }
}
```

### 23.6. Bạn nhìn thấy gì trong Aspire dashboard

- request từ web sang ordering-api
- log của ordering-api khi tạo order
- worker nhận event
- health trạng thái của Postgres, RabbitMQ, Redis
- endpoint nào đang mở

Đó chính là trải nghiệm “distributed app nhưng còn kiểm soát được”.

## 24. Testing với Aspire

Aspire không thay thế unit test hay integration test, nhưng nó hỗ trợ cách bạn nghĩ về toàn hệ thống.

### 24.1. Unit test vẫn giữ nguyên

- test domain rule
- test application service
- test mapping
- test validation

Ví dụ:

```csharp
[Fact]
public void TotalAmount_should_be_sum_of_all_items()
{
    var items = new[]
    {
        new CheckoutItem(Guid.NewGuid(), "Keyboard", 100, 2),
        new CheckoutItem(Guid.NewGuid(), "Mouse", 50, 1)
    };

    var total = items.Sum(x => x.UnitPrice * x.Quantity);

    Assert.Equal(250, total);
}
```

### 24.2. Integration test nên tập trung vào business boundary

Ví dụ test `Ordering.Api` tạo order và publish event. Phần Aspire value là local topology dễ dựng hơn, nhưng bản thân test vẫn nên rõ mục tiêu chứ không chỉ “spin cả hệ thống lên rồi hy vọng”.

## 25. Các lỗi phổ biến khi mới dùng Aspire

### 25.1. Biến AppHost thành nơi chứa business logic

Sai lầm: nhồi handler, service và logic nghiệp vụ vào AppHost.

Đúng hơn: AppHost chỉ nên giữ vai trò composition root cho distributed application.

### 25.2. Hard-code URL dù đã có service discovery

Sai lầm:

```csharp
client.BaseAddress = new Uri("https://localhost:7021");
```

Đúng hơn:

```csharp
client.BaseAddress = new Uri("https+http://ordering-api");
```

### 25.3. Không gom defaults dùng chung

Nếu không có `ServiceDefaults`, bạn sẽ lặp config và drift cực nhanh giữa các service.

### 25.4. Lạm dụng quá nhiều service nhỏ

Aspire giúp chạy nhiều service dễ hơn, nhưng không có nghĩa là càng nhiều service càng tốt.

### 25.5. Quên chiến lược dữ liệu

Bạn có thể dễ dàng thêm Postgres, Redis, RabbitMQ trong AppHost, nhưng bài toán consistency, transaction boundary, idempotency vẫn là trách nhiệm của kiến trúc hệ thống.

## 26. Một cấu trúc solution gợi ý

```text
Store.sln
├── Store.AppHost
├── Store.ServiceDefaults
├── Store.Web
├── Store.Catalog.Api
├── Store.Basket.Api
├── Store.Ordering.Api
├── Store.Ordering.Worker
├── Store.Contracts
└── tests
    ├── Store.Catalog.Tests
    └── Store.Ordering.Tests
```

### 26.1. Gợi ý tổ chức code

- `Contracts`: event contract, shared DTO thật sự cần thiết
- mỗi service sở hữu persistence riêng nếu bạn đi microservice thật
- tránh shared library quá mức khiến service dính chặt nhau

## 27. Aspire và production deployment

Cần nói rất thẳng: Aspire làm local/dev experience rất tốt, nhưng production deployment vẫn cần chiến lược riêng.

Tùy môi trường, bạn có thể deploy lên:

- container platform
- Kubernetes
- Azure Container Apps
- App Service
- VM/VPS truyền thống

Điểm cần hiểu là:

- AppHost rất hữu ích cho modeling và local orchestration
- production cần thêm concern như scaling, networking, secret management, rollout, security, backup

Đừng nghĩ “chạy được trên máy dev bằng Aspire” là production-ready.

## 28. Một ví dụ hoàn chỉnh hơn cho Program.cs của Web

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<CatalogClient>(client =>
{
    client.BaseAddress = new Uri("https+http://catalog-api");
});
builder.Services.AddHttpClient<BasketClient>(client =>
{
    client.BaseAddress = new Uri("https+http://basket-api");
});
builder.Services.AddHttpClient<OrderingClient>(client =>
{
    client.BaseAddress = new Uri("https+http://ordering-api");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapDefaultEndpoints();

app.Run();
```

Phần này không quá phức tạp, và đó là điểm tốt. Aspire làm phần wiring rõ ràng thay vì khiến application code rối hơn.

## 29. Khi nào nên chọn Aspire cho dự án mới

Tôi sẽ khuyên cân nhắc Aspire nếu:

- bạn biết hệ thống sẽ có nhiều process/resource
- team muốn observability tốt ngay từ đầu
- onboarding dev là vấn đề đau đầu
- bạn dùng .NET hiện đại và không ngại theo conventions của Microsoft
- cần một cách chuẩn để compose distributed app local

Ngược lại, nếu bạn chỉ có một API đơn lẻ, ít dependency, lifecycle đơn giản, có thể Aspire chưa mang lại nhiều giá trị ngay lập tức.

## 30. Checklist thực chiến khi áp dụng Aspire

1. Tạo `AppHost` và `ServiceDefaults` rõ ràng.
2. Dùng `WithReference` cho project và resource thay vì hard-code config.
3. Chuẩn hóa `HttpClient` với service discovery + resilience.
4. Bật OpenTelemetry từ ngày đầu.
5. Expose health endpoints thống nhất.
6. Seed data local để dev dễ thử flow.
7. Theo dõi dashboard thường xuyên khi debug luồng cross-service.
8. Giữ business logic ở service tương ứng, không nhồi vào AppHost.
9. Thiết kế event contract rõ ràng nếu dùng message broker.
10. Đừng quên idempotency và consistency, vì Aspire không giải quyết thay bạn.

## 31. Kết luận

.NET Aspire rất đáng học nếu bạn làm .NET backend hiện đại và đang bước vào vùng distributed application. Giá trị lớn nhất của nó không nằm ở vài helper API nhỏ, mà ở việc nó đưa ra một mô hình phát triển thống nhất hơn cho:

- composition của nhiều service
- quản lý dependency cục bộ
- service discovery
- observability
- local orchestration

Khi dùng đúng cách, Aspire giúp team bớt thời gian vật lộn với hạ tầng local và dành nhiều thời gian hơn cho logic nghiệp vụ thật.

Nói thực tế hơn, Aspire không làm bài toán hệ thống biến mất. Bạn vẫn phải xử lý:

- boundary giữa service
- consistency dữ liệu
- retries và idempotency
- security
- production operations

Nhưng nó khiến “trải nghiệm xây distributed app bằng .NET” bớt đau hơn rất nhiều.

Nếu bạn mới bắt đầu, tôi khuyên làm theo lộ trình sau:

1. tạo một solution Aspire mẫu với web + api
2. thêm Redis và Postgres
3. bật tracing, health checks
4. thêm một worker hoặc message broker
5. xây một flow end-to-end thật nhỏ nhưng đủ đầy

Sau 1 hoặc 2 bài lab như vậy, bạn sẽ hiểu rất rõ vì sao Aspire đang trở thành một mảnh ghép quan trọng trong hệ sinh thái .NET hiện đại.

## 32. Phụ lục: ví dụ AppHost đầy đủ để tham khảo nhanh

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "16")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);

var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var catalogApi = builder.AddProject<Projects.Store_Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WithReference(redis);

var basketApi = builder.AddProject<Projects.Store_Basket_Api>("basket-api")
    .WithReference(redis);

var orderingApi = builder.AddProject<Projects.Store_Ordering_Api>("ordering-api")
    .WithReference(orderingDb)
    .WithReference(rabbitMq);

var orderingWorker = builder.AddProject<Projects.Store_Ordering_Worker>("ordering-worker")
    .WithReference(orderingDb)
    .WithReference(rabbitMq);

builder.AddProject<Projects.Store_Web>("store-web")
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithReference(orderingApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

## 33. Phụ lục: package thường gặp

Tùy version .NET/Aspire, bạn thường sẽ gặp các package hoặc khái niệm liên quan:

- `Aspire.Hosting`
- `Aspire.ServiceDefaults`
- OpenTelemetry packages
- health check packages cho Postgres, Redis
- `Microsoft.Extensions.ServiceDiscovery`
- provider/client library như `Npgsql`, `StackExchange.Redis`, `MassTransit`

Hãy kiểm tra version package giữa các project để tránh mismatch.

## 34. Tóm tắt ngắn gọn cho người bận rộn

Nếu chỉ nhớ 6 ý về .NET Aspire, hãy nhớ:

1. Aspire giúp compose distributed application trong .NET dễ hơn.
2. `AppHost` mô tả topology của hệ thống.
3. `ServiceDefaults` gom observability, health checks, discovery, resilience.
4. `WithReference` là cách rất quan trọng để nối project với resource hoặc service khác.
5. Dashboard giúp debug local tốt hơn đáng kể.
6. Aspire mạnh ở developer experience và operability, không thay thế design kiến trúc hay production platform.

Hy vọng sau bài này, bạn không chỉ biết “Aspire là gì”, mà còn biết cách áp dụng nó vào một solution thực tế có web, API, worker, database, cache và broker, với code đủ cụ thể để bắt đầu ngay.
