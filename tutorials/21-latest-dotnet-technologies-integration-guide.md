# 🚀 Hướng Dẫn Tích Hợp Công Nghệ Mới Nhất Của .NET Vào Ứng Dụng Web

> **Phiên bản:** .NET 10 LTS (November 2025) | C# 14 | ASP.NET Core 10
>
> Tài liệu này tổng hợp **9 công nghệ mới nhất** của .NET ecosystem và hướng dẫn tích hợp vào ứng dụng web với code mẫu đầy đủ, chạy được ngay.

---

## 📋 Mục Lục

1. [C# 14 - Tính Năng Ngôn Ngữ Mới](#1-c-14---tính-năng-ngôn-ngữ-mới)
2. [OpenAPI 3.1 - Tích Hợp Native](#2-openapi-31---tích-hợp-native)
3. [Minimal API Enhancements - SSE & Validation](#3-minimal-api-enhancements---sse--validation)
4. [HybridCache - Caching Thế Hệ Mới](#4-hybridcache---caching-thế-hệ-mới)
5. [Native AOT - Biên Dịch Trước](#5-native-aot---biên-dịch-trước)
6. [Blazor 10 - Interactive Web UI](#6-blazor-10---interactive-web-ui)
7. [Passkey/FIDO2 Authentication - Đăng Nhập Không Mật Khẩu](#7-passkeyfido2-authentication---đăng-nhập-không-mật-khẩu)
8. [Microsoft.Extensions.AI - Tích Hợp AI](#8-microsoftextensionsai---tích-hợp-ai)
9. [.NET Aspire - Cloud-Native Orchestration](#9-net-aspire---cloud-native-orchestration)

---

## 1. C# 14 - Tính Năng Ngôn Ngữ Mới

### 1.1. Tổng Quan

C# 14 đi kèm .NET 10 mang đến nhiều tính năng giúp code ngắn gọn, an toàn và biểu cảm hơn:

- **`field` keyword** — truy cập backing field trực tiếp trong property
- **Extension Members** — mở rộng property, event, không chỉ method
- **Null-conditional assignment mở rộng** — `??=` cho property/indexer
- **Unbound generic types trong `nameof`**
- **Implicit span conversions**

### 1.2. `field` Keyword — Truy Cập Backing Field Trực Tiếp

**Vấn đề cũ:** Muốn thêm logic vào property getter/setter, bạn phải khai báo riêng backing field:

```csharp
// ❌ Cách cũ - phải khai báo _name riêng
private string _name;
public string Name
{
    get => _name;
    set
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Name cannot be empty");
        _name = value;
    }
}
```

**C# 14 — Dùng `field` keyword:**

```csharp
// ✅ C# 14 - không cần khai báo backing field riêng
public string Name
{
    get => field;
    set
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Name cannot be empty");
        field = value;
    }
}
```

**Ví dụ thực tế — ViewModel với INotifyPropertyChanged:**

```csharp
public class ProductViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProductName
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProductName)));
            }
        }
    } = string.Empty; // ← khởi tạo giá trị mặc định cho backing field

    public decimal Price
    {
        get => field;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(Price), "Price cannot be negative");
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Price)));
            }
        }
    }

    // Lazy-initialized property
    public List<string> Tags
    {
        get => field ??= [];
    }
}
```

> **Giải thích:** `field` là từ khóa ngữ cảnh (contextual keyword) — compiler tự tạo backing field. Bạn không cần khai báo `_productName`, `_price` riêng nữa. Code sạch hơn nhiều!

### 1.3. Extension Members — Mở Rộng Property & Event

**Cách cũ:** Chỉ có extension methods:

```csharp
// ❌ Cũ: chỉ extension method
public static class StringExtensions
{
    public static bool IsValidEmail(this string s) =>
        s.Contains('@') && s.Contains('.');
}
```

**C# 14 — Extension Everything:**

```csharp
// ✅ C# 14: Extension property, method, và static member
public implicit extension StringValidationExtensions for string
{
    // Extension Property
    public bool IsValidEmail =>
        this.Contains('@') && this.Contains('.');

    // Extension Method
    public string Truncate(int maxLength) =>
        this.Length <= maxLength ? this : this[..maxLength] + "...";

    // Extension Static Method
    public static string Empty => string.Empty;
}

// Sử dụng:
string email = "user@example.com";
bool isValid = email.IsValidEmail;       // Extension Property!
string short = email.Truncate(10);       // Extension Method
```

**Ví dụ thực tế — Domain Extensions:**

```csharp
public implicit extension DateTimeWebExtensions for DateTime
{
    // Extension property cho ISO string
    public string ToIsoString => this.ToString("yyyy-MM-ddTHH:mm:ssZ");

    // Extension property kiểm tra ngày làm việc
    public bool IsBusinessDay =>
        this.DayOfWeek != DayOfWeek.Saturday &&
        this.DayOfWeek != DayOfWeek.Sunday;

    // Extension method tính tuổi
    public int CalculateAge(DateTime birthDate)
    {
        var age = this.Year - birthDate.Year;
        if (this.DayOfYear < birthDate.DayOfYear) age--;
        return age;
    }
}

// Sử dụng trong API:
app.MapGet("/today", () => new
{
    Date = DateTime.UtcNow.ToIsoString,        // Extension Property
    IsBusinessDay = DateTime.UtcNow.IsBusinessDay  // Extension Property
});
```

### 1.4. Null-Conditional Assignment Mở Rộng

```csharp
// ✅ C# 14: ??= cho property, indexer, expression
var config = GetConfiguration();

// Property assignment
config.Settings ??= new AppSettings();

// Indexer assignment
config.Features["dark-mode"] ??= new FeatureFlag { Enabled = false };

// Dictionary initialization
var cache = new Dictionary<string, List<string>>();
cache["users"] ??= new List<string>();
cache["users"].Add("Alice");

// Chained null-conditional
GetWidget()?.Settings ??= new WidgetSettings();
```

### 1.5. Project Setup Cho C# 14

```xml
<!-- File: YourProject.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

---

## 2. OpenAPI 3.1 - Tích Hợp Native

### 2.1. Tổng Quan

.NET 10 hỗ trợ **OpenAPI 3.1 native** — không cần Swashbuckle hay NSwag. ASP.NET Core tự tạo OpenAPI document chuẩn 3.1, tương thích JSON Schema 2020-12, hỗ trợ `nullable`, discriminator, và tạo client code tốt hơn.

### 2.2. Cài Đặt

```bash
dotnet new webapi -n OpenApiDemo --framework net10.0
cd OpenApiDemo
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore  # UI đẹp hơn Swagger
```

### 2.3. Code Mẫu Hoàn Chỉnh

```csharp
// File: Program.cs
using Microsoft.AspNetCore.OpenApi;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// ✅ Đăng ký OpenAPI 3.1
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new()
        {
            Title = "Product API",
            Version = "v1.0",
            Description = "API quản lý sản phẩm - .NET 10 với OpenAPI 3.1",
            Contact = new()
            {
                Name = "Dev Team",
                Email = "dev@company.com"
            }
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// ✅ Map OpenAPI endpoint
app.MapOpenApi(); // Tạo /openapi/v1.json

// ✅ Scalar UI (thay thế Swagger UI, đẹp hơn)
app.MapScalarApiReference(options =>
{
    options.Title = "Product API Explorer";
    options.Theme = ScalarTheme.BluePlanet;
});

// ── Models ──
var products = new List<Product>
{
    new(1, "Laptop Dell XPS 15", 35_000_000, "Electronics", ["hot", "bestseller"]),
    new(2, "Chuột Logitech MX Master", 2_500_000, "Accessories", ["popular"]),
    new(3, "Bàn phím Keychron K2", 2_200_000, "Accessories", ["new"])
};

// ── Endpoints ──

// GET tất cả sản phẩm
app.MapGet("/api/products", () => TypedResults.Ok(products))
    .WithName("GetAllProducts")
    .WithTags("Products")
    .WithSummary("Lấy danh sách tất cả sản phẩm")
    .WithDescription("Trả về toàn bộ danh sách sản phẩm trong hệ thống")
    .WithOpenApi();

// GET sản phẩm theo ID
app.MapGet("/api/products/{id:int}", (int id) =>
{
    var product = products.FirstOrDefault(p => p.Id == id);
    return product is not null
        ? Results.Ok(product)
        : Results.NotFound(new ProblemDetails
        {
            Title = "Product Not Found",
            Detail = $"Không tìm thấy sản phẩm với ID = {id}",
            Status = 404
        });
})
.WithName("GetProductById")
.WithTags("Products")
.Produces<Product>(200)
.ProducesProblem(404)
.WithOpenApi();

// POST tạo sản phẩm mới
app.MapPost("/api/products", (CreateProductRequest request) =>
{
    var product = new Product(
        products.Max(p => p.Id) + 1,
        request.Name,
        request.Price,
        request.Category,
        request.Tags
    );
    products.Add(product);
    return TypedResults.Created($"/api/products/{product.Id}", product);
})
.WithName("CreateProduct")
.WithTags("Products")
.Accepts<CreateProductRequest>("application/json")
.Produces<Product>(201)
.ProducesValidationProblem()
.WithOpenApi();

// DELETE xóa sản phẩm
app.MapDelete("/api/products/{id:int}", (int id) =>
{
    var product = products.FirstOrDefault(p => p.Id == id);
    if (product is null) return Results.NotFound();
    products.Remove(product);
    return Results.NoContent();
})
.WithName("DeleteProduct")
.WithTags("Products")
.Produces(204)
.ProducesProblem(404)
.WithOpenApi();

app.Run();

// ── Record Types ──
public record Product(
    int Id,
    string Name,
    decimal Price,
    string Category,
    List<string> Tags
);

public record CreateProductRequest(
    [Required, MinLength(1)] string Name,
    [Range(0, double.MaxValue)] decimal Price,
    [Required] string Category,
    List<string> Tags
);
```

### 2.4. Kết Quả

- Truy cập `/openapi/v1.json` → OpenAPI 3.1 JSON document đầy đủ
- Truy cập `/scalar/v1` → UI interactive đẹp với theme BluePlanet
- Tự động tạo schema cho `Product`, `CreateProductRequest` với validation
- Hỗ trợ `nullable`, `oneOf`, `discriminator` chuẩn JSON Schema 2020-12

---

## 3. Minimal API Enhancements - SSE & Validation

### 3.1. Tổng Quan

.NET 10 nâng cấp Minimal API với:
- **Server-Sent Events (SSE)** native — `TypedResults.ServerSentEvents()`
- **Built-in Model Validation** — tự động validate không cần FluentValidation
- **Improved ProblemDetails** — error response chuẩn RFC 9457

### 3.2. Cài Đặt

```bash
dotnet new webapi -n MinimalApiDemo --framework net10.0
cd MinimalApiDemo
```

### 3.3. Code Mẫu Hoàn Chỉnh — Server-Sent Events

```csharp
// File: Program.cs
using System.Runtime.CompilerServices;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký service
builder.Services.AddSingleton<StockPriceService>();

var app = builder.Build();

// ── SSE Endpoint: Real-time Stock Prices ──
app.MapGet("/api/stocks/stream", (StockPriceService stockService, CancellationToken ct) =>
{
    // ✅ .NET 10: TypedResults.ServerSentEvents() native!
    return TypedResults.ServerSentEvents(
        stockService.StreamPricesAsync(ct),
        eventType: "stock-update"
    );
})
.WithName("StreamStockPrices")
.WithSummary("Stream giá cổ phiếu real-time qua SSE")
.WithOpenApi();

// ── SSE Endpoint: Notification Stream ──
app.MapGet("/api/notifications/stream", async (HttpContext context, CancellationToken ct) =>
{
    // Cách thủ công (vẫn hoạt động)
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var notifications = new[]
    {
        "Đơn hàng #1234 đã được xác nhận",
        "Sản phẩm 'Laptop Dell' sắp hết hàng",
        "Khách hàng mới đăng ký",
        "Báo cáo doanh thu tháng 4 đã sẵn sàng"
    };

    foreach (var notification in notifications)
    {
        if (ct.IsCancellationRequested) break;

        var data = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = notification,
            timestamp = DateTime.UtcNow,
            id = Guid.NewGuid()
        });

        await context.Response.WriteAsync($"id: {Guid.NewGuid()}\n");
        await context.Response.WriteAsync($"event: notification\n");
        await context.Response.WriteAsync($"data: {data}\n\n");
        await context.Response.Body.FlushAsync(ct);
        await Task.Delay(2000, ct); // Gửi mỗi 2 giây
    }
});

// ── SSE Endpoint: IAsyncEnumerable approach ──
app.MapGet("/api/chat/stream", (string prompt, CancellationToken ct) =>
{
    return TypedResults.ServerSentEvents(
        GenerateChatResponseAsync(prompt, ct),
        eventType: "chat-token"
    );
});

app.Run();

// ── Services ──
public class StockPriceService
{
    private readonly string[] _symbols = ["AAPL", "GOOGL", "MSFT", "AMZN", "TSLA"];
    private readonly Random _random = new();

    public async IAsyncEnumerable<StockPrice> StreamPricesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var symbol = _symbols[_random.Next(_symbols.Length)];
            yield return new StockPrice(
                Symbol: symbol,
                Price: Math.Round(100 + _random.NextDouble() * 200, 2),
                Change: Math.Round((_random.NextDouble() - 0.5) * 10, 2),
                Timestamp: DateTime.UtcNow
            );
            await Task.Delay(1000, ct); // Mỗi giây 1 lần
        }
    }
}

public record StockPrice(string Symbol, double Price, double Change, DateTime Timestamp);

// ── Helper: Simulate AI Chat Streaming ──
static async IAsyncEnumerable<string> GenerateChatResponseAsync(
    string prompt,
    [EnumeratorCancellation] CancellationToken ct)
{
    var words = $"Xin chào! Bạn đã hỏi: '{prompt}'. Đây là câu trả lời từ AI streaming...".Split(' ');
    foreach (var word in words)
    {
        if (ct.IsCancellationRequested) yield break;
        yield return word + " ";
        await Task.Delay(200, ct); // Giả lập delay AI
    }
}
```

### 3.4. Code Mẫu — Built-in Validation

```csharp
// File: Program.cs (phần validation)
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// ✅ .NET 10: Bật validation tự động
builder.Services.AddValidation();

var app = builder.Build();

// ── POST với validation tự động ──
app.MapPost("/api/orders", (CreateOrderRequest order) =>
{
    // ✅ Validation tự động! Nếu order không hợp lệ → 400 ProblemDetails
    return TypedResults.Created($"/api/orders/{Guid.NewGuid()}", new
    {
        OrderId = Guid.NewGuid(),
        order.CustomerEmail,
        order.Items,
        order.ShippingAddress,
        Status = "Pending",
        CreatedAt = DateTime.UtcNow
    });
})
.WithName("CreateOrder")
.WithOpenApi();

// ── PUT với validation complex ──
app.MapPut("/api/orders/{id:guid}", (Guid id, UpdateOrderRequest request) =>
{
    return TypedResults.Ok(new { id, request.Status, UpdatedAt = DateTime.UtcNow });
})
.WithName("UpdateOrder")
.WithOpenApi();

app.Run();

// ── Request Models với DataAnnotations ──
public class CreateOrderRequest
{
    [Required(ErrorMessage = "Email khách hàng là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string CustomerEmail { get; set; } = default!;

    [Required(ErrorMessage = "Phải có ít nhất 1 sản phẩm")]
    [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất 1 sản phẩm")]
    public List<OrderItem> Items { get; set; } = [];

    [Required(ErrorMessage = "Địa chỉ giao hàng là bắt buộc")]
    public ShippingAddress ShippingAddress { get; set; } = default!;

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự")]
    public string? Notes { get; set; }
}

public class OrderItem
{
    [Required]
    public string ProductId { get; set; } = default!;

    [Range(1, 100, ErrorMessage = "Số lượng từ 1 đến 100")]
    public int Quantity { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Giá phải lớn hơn 0")]
    public decimal UnitPrice { get; set; }
}

public class ShippingAddress
{
    [Required, MinLength(5)]
    public string Street { get; set; } = default!;

    [Required]
    public string City { get; set; } = default!;

    [Required, RegularExpression(@"^\d{5,6}$", ErrorMessage = "Mã bưu điện không hợp lệ")]
    public string PostalCode { get; set; } = default!;
}

public class UpdateOrderRequest
{
    [Required]
    [AllowedValues("Confirmed", "Shipped", "Delivered", "Cancelled",
        ErrorMessage = "Trạng thái không hợp lệ")]
    public string Status { get; set; } = default!;
}
```

### 3.5. Client HTML Để Test SSE

```html
<!-- File: wwwroot/sse-test.html -->
<!DOCTYPE html>
<html lang="vi">
<head>
    <meta charset="UTF-8">
    <title>SSE Demo - .NET 10</title>
    <style>
        body { font-family: 'Segoe UI', sans-serif; max-width: 800px; margin: 50px auto; }
        .stock { padding: 10px; margin: 5px 0; border-radius: 8px; background: #f0f0f0; }
        .positive { color: green; } .negative { color: red; }
        #log { max-height: 400px; overflow-y: auto; }
    </style>
</head>
<body>
    <h1>📈 Real-time Stock Prices (SSE)</h1>
    <button onclick="startStream()">Bắt đầu Stream</button>
    <button onclick="stopStream()">Dừng Stream</button>
    <div id="log"></div>

    <script>
        let eventSource;
        function startStream() {
            eventSource = new EventSource('/api/stocks/stream');
            eventSource.addEventListener('stock-update', (e) => {
                const stock = JSON.parse(e.data);
                const changeClass = stock.change >= 0 ? 'positive' : 'negative';
                const arrow = stock.change >= 0 ? '▲' : '▼';
                document.getElementById('log').innerHTML =
                    `<div class="stock">
                        <strong>${stock.symbol}</strong>: $${stock.price}
                        <span class="${changeClass}">${arrow} ${stock.change}%</span>
                        <small>${new Date(stock.timestamp).toLocaleTimeString()}</small>
                    </div>` + document.getElementById('log').innerHTML;
            });
            eventSource.onerror = () => console.log('SSE connection error');
        }
        function stopStream() { eventSource?.close(); }
    </script>
</body>
</html>
```

---

## 4. HybridCache - Caching Thế Hệ Mới

### 4.1. Tổng Quan

`HybridCache` là API caching mới trong .NET 10, kết hợp:
- **L1 (In-Memory)** — nhanh cực, cùng process
- **L2 (Distributed — Redis/SQL)** — shared across instances
- **Stampede Protection** — chỉ 1 request fetch data, các request khác chờ
- **Tag-based Invalidation** — xóa cache theo nhóm
- **Serialization tự động** — không cần tự serialize/deserialize

### 4.2. Cài Đặt

```bash
dotnet new webapi -n HybridCacheDemo --framework net10.0
cd HybridCacheDemo
dotnet add package Microsoft.Extensions.Caching.Hybrid
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

### 4.3. Code Mẫu Hoàn Chỉnh

```csharp
// File: Program.cs
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

// ✅ Đăng ký Redis làm L2 cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "myapp_";
});

// ✅ Đăng ký HybridCache với cấu hình
builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 1024 * 1024;    // 1 MB max
    options.MaximumKeyLength = 256;
    options.ReportTagMetrics = true;
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(30),           // L2 TTL
        LocalCacheExpiration = TimeSpan.FromMinutes(5),  // L1 TTL
        Flags = HybridCacheEntryFlags.None
    };
});

// Đăng ký services
builder.Services.AddSingleton<ProductRepository>();
builder.Services.AddScoped<CachedProductService>();

var app = builder.Build();

// ── Endpoints ──

// GET sản phẩm (có cache)
app.MapGet("/api/products/{id:int}", async (int id, CachedProductService service) =>
{
    var product = await service.GetProductAsync(id);
    return product is not null ? Results.Ok(product) : Results.NotFound();
})
.WithName("GetProduct");

// GET sản phẩm theo category (có cache + tag)
app.MapGet("/api/products/category/{category}", async (
    string category, CachedProductService service) =>
{
    var products = await service.GetProductsByCategoryAsync(category);
    return Results.Ok(products);
})
.WithName("GetProductsByCategory");

// POST tạo sản phẩm (invalidate cache)
app.MapPost("/api/products", async (Product product, CachedProductService service) =>
{
    await service.CreateProductAsync(product);
    return TypedResults.Created($"/api/products/{product.Id}", product);
})
.WithName("CreateProduct");

// DELETE invalidate cache theo tag
app.MapDelete("/api/cache/invalidate/{tag}", async (string tag, HybridCache cache) =>
{
    // ✅ Tag-based invalidation!
    await cache.RemoveByTagAsync(tag);
    return Results.Ok(new { Message = $"Cache invalidated for tag: {tag}" });
})
.WithName("InvalidateCache");

app.Run();

// ── Models ──
public record Product(int Id, string Name, decimal Price, string Category);

// ── Repository (giả lập database) ──
public class ProductRepository
{
    private readonly List<Product> _products =
    [
        new(1, "iPhone 16 Pro", 28_000_000, "Electronics"),
        new(2, "MacBook Air M4", 32_000_000, "Electronics"),
        new(3, "Sony WH-1000XM5", 8_000_000, "Audio"),
        new(4, "Samsung Galaxy S25", 22_000_000, "Electronics"),
        new(5, "AirPods Pro 3", 6_500_000, "Audio")
    ];

    public async Task<Product?> GetByIdAsync(int id)
    {
        await Task.Delay(100); // Giả lập latency database
        Console.WriteLine($"📀 [DB HIT] Fetching product {id} from database...");
        return _products.FirstOrDefault(p => p.Id == id);
    }

    public async Task<List<Product>> GetByCategoryAsync(string category)
    {
        await Task.Delay(150);
        Console.WriteLine($"📀 [DB HIT] Fetching products for category '{category}'...");
        return _products.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public Task AddAsync(Product product)
    {
        _products.Add(product);
        return Task.CompletedTask;
    }
}

// ── Service Layer với HybridCache ──
public class CachedProductService
{
    private readonly HybridCache _cache;
    private readonly ProductRepository _repo;

    public CachedProductService(HybridCache cache, ProductRepository repo)
    {
        _cache = cache;
        _repo = repo;
    }

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct = default)
    {
        // ✅ GetOrCreateAsync: nếu cache miss → gọi factory → lưu cache → trả về
        // Nếu nhiều request cùng lúc → chỉ 1 request gọi factory (stampede protection)
        return await _cache.GetOrCreateAsync(
            key: $"product:{id}",
            factory: async cancel => await _repo.GetByIdAsync(id),
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(15),
                LocalCacheExpiration = TimeSpan.FromMinutes(3)
            },
            tags: [$"product:{id}", "products"],  // ✅ Tags cho invalidation
            cancellationToken: ct
        );
    }

    public async Task<List<Product>> GetProductsByCategoryAsync(
        string category, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            key: $"products:category:{category.ToLower()}",
            factory: async cancel => await _repo.GetByCategoryAsync(category),
            tags: [$"category:{category.ToLower()}", "products"],
            cancellationToken: ct
        ) ?? [];
    }

    public async Task CreateProductAsync(Product product, CancellationToken ct = default)
    {
        await _repo.AddAsync(product);

        // ✅ Invalidate cache liên quan bằng tag
        await _cache.RemoveByTagAsync("products", ct);       // Xóa all products cache
        await _cache.RemoveByTagAsync($"category:{product.Category.ToLower()}", ct);
    }
}
```

### 4.4. So Sánh Với Cách Cũ

| Tính năng | `IDistributedCache` (cũ) | `HybridCache` (mới) |
|-----------|--------------------------|----------------------|
| L1 + L2 | ❌ Phải tự code | ✅ Tự động |
| Stampede Protection | ❌ Không có | ✅ Built-in |
| Serialization | ❌ Tự serialize | ✅ Tự động |
| Tag Invalidation | ❌ Không có | ✅ `RemoveByTagAsync` |
| API | `GetAsync/SetAsync` (bytes) | `GetOrCreateAsync<T>` (typed) |

---

## 5. Native AOT - Biên Dịch Trước

### 5.1. Tổng Quan

**Native AOT (Ahead-of-Time)** biên dịch ứng dụng .NET thành native binary:
- ⚡ **Khởi động ~10x nhanh hơn** (< 50ms thay vì 500ms+)
- 💾 **Memory ~5x ít hơn** (< 20MB thay vì 100MB+)
- 📦 **Binary size nhỏ** (~15MB self-contained)
- ☁️ **Lý tưởng cho serverless, container, edge**

### 5.2. Cài Đặt

```bash
dotnet new webapi -n NativeAotDemo --framework net10.0
cd NativeAotDemo
```

### 5.3. Code Mẫu Hoàn Chỉnh

```csharp
// File: Program.cs
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args); // ✅ SlimBuilder cho AOT

// ✅ Source-generated JSON serializer (bắt buộc cho AOT)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var app = builder.Build();

// ── In-memory data store ──
var todos = new List<TodoItem>
{
    new(1, "Học .NET 10 Native AOT", false, DateTime.UtcNow.AddDays(-1)),
    new(2, "Triển khai HybridCache", false, DateTime.UtcNow),
    new(3, "Test SSE endpoints", true, DateTime.UtcNow.AddDays(-2))
};

var nextId = 4;

// ── API Endpoints (AOT-compatible) ──

// GET all
app.MapGet("/api/todos", () => todos);

// GET by ID
app.MapGet("/api/todos/{id:int}", (int id) =>
    todos.FirstOrDefault(t => t.Id == id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

// POST
app.MapPost("/api/todos", (CreateTodoRequest request) =>
{
    var todo = new TodoItem(nextId++, request.Title, false, DateTime.UtcNow);
    todos.Add(todo);
    return TypedResults.Created($"/api/todos/{todo.Id}", todo);
});

// PUT toggle complete
app.MapPut("/api/todos/{id:int}/toggle", (int id) =>
{
    var todo = todos.FirstOrDefault(t => t.Id == id);
    if (todo is null) return Results.NotFound();
    var updated = todo with { IsCompleted = !todo.IsCompleted };
    todos[todos.IndexOf(todo)] = updated;
    return Results.Ok(updated);
});

// DELETE
app.MapDelete("/api/todos/{id:int}", (int id) =>
{
    var todo = todos.FirstOrDefault(t => t.Id == id);
    if (todo is null) return Results.NotFound();
    todos.Remove(todo);
    return Results.NoContent();
});

// Health check
app.MapGet("/health", () => new
{
    Status = "Healthy",
    Runtime = "Native AOT",
    StartTime = DateTime.UtcNow,
    WorkingSet = $"{Environment.WorkingSet / 1024 / 1024} MB"
});

app.Run();

// ── Models ──
public record TodoItem(int Id, string Title, bool IsCompleted, DateTime CreatedAt);
public record CreateTodoRequest(string Title);

// ✅ Source Generator cho JSON (bắt buộc cho Native AOT)
[JsonSerializable(typeof(List<TodoItem>))]
[JsonSerializable(typeof(TodoItem))]
[JsonSerializable(typeof(CreateTodoRequest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true
)]
internal partial class AppJsonContext : JsonSerializerContext { }
```

### 5.4. Project File Cho AOT

```xml
<!-- File: NativeAotDemo.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- ✅ Bật Native AOT -->
    <PublishAot>true</PublishAot>

    <!-- Tối ưu size -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>true</StripSymbols>

    <!-- AOT warnings -->
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

### 5.5. Build & So Sánh

```bash
# Build bình thường
dotnet publish -c Release -o ./publish-normal

# Build Native AOT
dotnet publish -c Release -p:PublishAot=true -o ./publish-aot

# So sánh size
ls -lh ./publish-normal/
ls -lh ./publish-aot/

# Đo startup time
time ./publish-aot/NativeAotDemo
```

**Kết quả điển hình:**

| Metric | Regular JIT | Native AOT |
|--------|------------|------------|
| Startup Time | ~500ms | ~30ms |
| Memory (idle) | ~80MB | ~15MB |
| Binary Size | ~90MB (self-contained) | ~15MB |
| Cold Start (Lambda) | ~2s | ~100ms |

---

## 6. Blazor 10 - Interactive Web UI

### 6.1. Tổng Quan

Blazor 10 cải tiến lớn:
- **Render mode linh hoạt** — Static SSR, Server, WebAssembly, Auto
- **Static Asset fingerprinting** — cache tốt hơn
- **Reconnection UI** — UX tốt hơn khi mất kết nối
- **QuickGrid RowClass** — styling có điều kiện
- **Persistent Component State** — giữ state khi chuyển render mode

### 6.2. Cài Đặt

```bash
dotnet new blazor -n BlazorDemo --framework net10.0 --interactivity Auto
cd BlazorDemo
```

### 6.3. Code Mẫu — Interactive Dashboard

```razor
@* File: Components/Pages/Dashboard.razor *@
@page "/dashboard"
@rendermode InteractiveServer
@using System.Timers
@implements IDisposable

<PageTitle>Dashboard - Blazor 10</PageTitle>

<h1>📊 Real-time Dashboard</h1>

<div class="dashboard-grid">
    @* ── KPI Cards ── *@
    <div class="kpi-card">
        <h3>Doanh Thu Hôm Nay</h3>
        <p class="kpi-value">@totalRevenue.ToString("N0") VNĐ</p>
        <span class="kpi-change positive">▲ @revenueChange%</span>
    </div>

    <div class="kpi-card">
        <h3>Đơn Hàng</h3>
        <p class="kpi-value">@orderCount</p>
        <span class="kpi-change positive">▲ @orderChange%</span>
    </div>

    <div class="kpi-card">
        <h3>Khách Truy Cập</h3>
        <p class="kpi-value">@visitorCount</p>
        <span class="kpi-change @(visitorChange >= 0 ? "positive" : "negative")">
            @(visitorChange >= 0 ? "▲" : "▼") @Math.Abs(visitorChange)%
        </span>
    </div>

    @* ── Real-time Order Table với QuickGrid ── *@
    <div class="order-table">
        <h3>📋 Đơn Hàng Gần Đây</h3>
        <QuickGrid Items="@orders.AsQueryable()" Pagination="@pagination"
                    RowClass="@GetRowClass">
            <PropertyColumn Property="@(o => o.Id)" Title="Mã ĐH" Sortable="true" />
            <PropertyColumn Property="@(o => o.Customer)" Title="Khách Hàng" Sortable="true" />
            <TemplateColumn Title="Tổng Tiền">
                <span>@context.Total.ToString("N0") VNĐ</span>
            </TemplateColumn>
            <TemplateColumn Title="Trạng Thái">
                <span class="badge @GetBadgeClass(context.Status)">@context.Status</span>
            </TemplateColumn>
            <PropertyColumn Property="@(o => o.CreatedAt)" Title="Thời Gian"
                            Format="HH:mm:ss" Sortable="true" />
        </QuickGrid>
        <Paginator State="@pagination" />
    </div>
</div>

@code {
    private Timer? timer;
    private Random random = new();
    private PaginationState pagination = new() { ItemsPerPage = 5 };

    // KPI data
    private decimal totalRevenue = 156_000_000;
    private int orderCount = 234;
    private int visitorCount = 1_847;
    private double revenueChange = 12.5;
    private double orderChange = 8.3;
    private double visitorChange = -2.1;

    // Orders
    private List<OrderViewModel> orders = [];

    protected override void OnInitialized()
    {
        // Khởi tạo dữ liệu mẫu
        orders = Enumerable.Range(1, 20).Select(i => new OrderViewModel
        {
            Id = $"ORD-{1000 + i}",
            Customer = $"Khách hàng {i}",
            Total = random.Next(100_000, 50_000_000),
            Status = GetRandomStatus(),
            CreatedAt = DateTime.Now.AddMinutes(-random.Next(0, 120))
        }).ToList();

        // ✅ Timer cập nhật real-time
        timer = new Timer(3000); // Mỗi 3 giây
        timer.Elapsed += async (s, e) => await UpdateDashboard();
        timer.Start();
    }

    private async Task UpdateDashboard()
    {
        // Simulate real-time updates
        totalRevenue += random.Next(50_000, 5_000_000);
        orderCount += random.Next(0, 3);
        visitorCount += random.Next(-5, 15);

        // Thêm đơn hàng mới
        orders.Insert(0, new OrderViewModel
        {
            Id = $"ORD-{1000 + orders.Count + 1}",
            Customer = $"Khách hàng mới {random.Next(100, 999)}",
            Total = random.Next(100_000, 50_000_000),
            Status = "Pending",
            CreatedAt = DateTime.Now
        });

        await InvokeAsync(StateHasChanged);
    }

    // ✅ QuickGrid RowClass - Styling có điều kiện
    private string? GetRowClass(OrderViewModel order) => order.Status switch
    {
        "Cancelled" => "row-cancelled",
        "Pending" => "row-pending",
        "Completed" => "row-completed",
        _ => null
    };

    private string GetBadgeClass(string status) => status switch
    {
        "Pending" => "badge-warning",
        "Confirmed" => "badge-info",
        "Shipped" => "badge-primary",
        "Completed" => "badge-success",
        "Cancelled" => "badge-danger",
        _ => "badge-secondary"
    };

    private string GetRandomStatus()
    {
        var statuses = new[] { "Pending", "Confirmed", "Shipped", "Completed", "Cancelled" };
        return statuses[random.Next(statuses.Length)];
    }

    public void Dispose() => timer?.Dispose();
}

@* ── ViewModel ── *@
@code {
    public class OrderViewModel
    {
        public string Id { get; set; } = "";
        public string Customer { get; set; } = "";
        public decimal Total { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
```

### 6.4. CSS Cho Dashboard

```css
/* File: Components/Pages/Dashboard.razor.css */
.dashboard-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 20px;
    margin-top: 20px;
}

.kpi-card {
    background: white;
    border-radius: 12px;
    padding: 24px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    text-align: center;
}

.kpi-value {
    font-size: 2rem;
    font-weight: bold;
    color: #1a1a2e;
    margin: 10px 0;
}

.kpi-change { font-size: 0.9rem; font-weight: 600; }
.positive { color: #10b981; }
.negative { color: #ef4444; }

.order-table {
    grid-column: 1 / -1;
    background: white;
    border-radius: 12px;
    padding: 24px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

::deep .row-cancelled { background-color: #fee2e2 !important; }
::deep .row-pending { background-color: #fef3c7 !important; }
::deep .row-completed { background-color: #d1fae5 !important; }

.badge {
    padding: 4px 12px;
    border-radius: 20px;
    font-size: 0.8rem;
    font-weight: 600;
}
.badge-warning { background: #fbbf24; color: #92400e; }
.badge-info { background: #60a5fa; color: #1e3a5f; }
.badge-primary { background: #818cf8; color: white; }
.badge-success { background: #34d399; color: #065f46; }
.badge-danger { background: #f87171; color: #7f1d1d; }
```

### 6.5. Persistent Component State

```razor
@* File: Components/Pages/Settings.razor *@
@page "/settings"
@rendermode InteractiveAuto
@inject PersistentComponentState ApplicationState

<h1>⚙️ Cài Đặt</h1>

<div>
    <label>Theme:</label>
    <select @bind="currentTheme">
        <option value="light">Light</option>
        <option value="dark">Dark</option>
        <option value="auto">Auto</option>
    </select>
</div>

<div>
    <label>Ngôn ngữ:</label>
    <select @bind="language">
        <option value="vi">Tiếng Việt</option>
        <option value="en">English</option>
    </select>
</div>

<p>Cài đặt hiện tại: Theme=@currentTheme, Language=@language</p>

@code {
    private string currentTheme = "light";
    private string language = "vi";
    private PersistingComponentStateSubscription _subscription;

    protected override void OnInitialized()
    {
        // ✅ Persist state khi chuyển từ SSR → Interactive
        _subscription = ApplicationState.RegisterOnPersisting(() =>
        {
            ApplicationState.PersistAsJson("theme", currentTheme);
            ApplicationState.PersistAsJson("language", language);
            return Task.CompletedTask;
        });

        // ✅ Restore state
        if (ApplicationState.TryTakeFromJson<string>("theme", out var savedTheme))
            currentTheme = savedTheme!;
        if (ApplicationState.TryTakeFromJson<string>("language", out var savedLang))
            language = savedLang!;
    }
}
```

---

## 7. Passkey/FIDO2 Authentication - Đăng Nhập Không Mật Khẩu

### 7.1. Tổng Quan

.NET 10 tích hợp **WebAuthn/FIDO2 Passkey** trực tiếp vào ASP.NET Core Identity:
- 🔑 **Không cần mật khẩu** — dùng vân tay, Face ID, Windows Hello
- 🛡️ **Chống phishing** — private key không bao giờ rời thiết bị
- 🌐 **Cross-platform** — Chrome, Safari, Edge, mobile
- ⚡ **Tích hợp 1 dòng** — `options.Passkeys.Enabled = true`

### 7.2. Cài Đặt

```bash
dotnet new mvc --auth Individual -n PasskeyDemo --framework net10.0
cd PasskeyDemo
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet ef database update
```

### 7.3. Code Mẫu Hoàn Chỉnh

```csharp
// File: Program.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PasskeyDemo.Data;

var builder = WebApplication.CreateBuilder(args);

// ✅ Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=passkey.db"));

// ✅ Identity với Passkey
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;

    // ✅ .NET 10: Bật Passkey/WebAuthn
    options.Passkeys.Enabled = true;

    // Cấu hình passkey
    options.Passkeys.ServerDomain = "localhost";
    options.Passkeys.AllowedOrigins.Add("https://localhost:5001");
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddDefaultUI(); // Tự động có UI đăng ký/đăng nhập passkey

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// ✅ Tự động migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
```

### 7.4. Custom Passkey Registration Page

```razor
@* File: Pages/Account/RegisterPasskey.cshtml *@
@page
@model RegisterPasskeyModel
@{
    ViewData["Title"] = "Đăng Ký Passkey";
}

<h2>🔑 Đăng Ký Với Passkey</h2>
<p>Sử dụng vân tay, Face ID, hoặc Windows Hello để tạo tài khoản không mật khẩu.</p>

<div id="passkey-register">
    <form id="registerForm">
        <div class="form-group">
            <label>Tên hiển thị:</label>
            <input type="text" id="displayName" class="form-control" required />
        </div>
        <div class="form-group">
            <label>Email:</label>
            <input type="email" id="email" class="form-control" required />
        </div>
        <button type="button" onclick="registerPasskey()" class="btn btn-primary mt-3">
            🔐 Đăng Ký Với Passkey
        </button>
    </form>
    <div id="status" class="mt-3"></div>
</div>

<script>
async function registerPasskey() {
    const displayName = document.getElementById('displayName').value;
    const email = document.getElementById('email').value;
    const status = document.getElementById('status');

    try {
        // Bước 1: Lấy challenge từ server
        const optionsRes = await fetch('/api/passkey/register/options', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ displayName, email })
        });
        const options = await optionsRes.json();

        // Bước 2: Browser tạo credential với WebAuthn
        options.challenge = base64ToBuffer(options.challenge);
        options.user.id = base64ToBuffer(options.user.id);

        const credential = await navigator.credentials.create({ publicKey: options });

        // Bước 3: Gửi credential về server để lưu
        const verifyRes = await fetch('/api/passkey/register/verify', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                id: credential.id,
                rawId: bufferToBase64(credential.rawId),
                response: {
                    attestationObject: bufferToBase64(credential.response.attestationObject),
                    clientDataJSON: bufferToBase64(credential.response.clientDataJSON)
                },
                type: credential.type
            })
        });

        if (verifyRes.ok) {
            status.innerHTML = '<div class="alert alert-success">✅ Đăng ký thành công!</div>';
        } else {
            status.innerHTML = '<div class="alert alert-danger">❌ Đăng ký thất bại</div>';
        }
    } catch (err) {
        status.innerHTML = `<div class="alert alert-danger">Lỗi: ${err.message}</div>`;
    }
}

// Helper functions
function base64ToBuffer(base64) {
    const binary = atob(base64.replace(/-/g, '+').replace(/_/g, '/'));
    return Uint8Array.from(binary, c => c.charCodeAt(0)).buffer;
}
function bufferToBase64(buffer) {
    return btoa(String.fromCharCode(...new Uint8Array(buffer)))
        .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}
</script>
```

### 7.5. API Endpoints Cho Passkey

```csharp
// File: Controllers/PasskeyApiController.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/passkey")]
public class PasskeyApiController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public PasskeyApiController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /// <summary>
    /// Bước 1: Tạo registration options cho WebAuthn
    /// </summary>
    [HttpPost("register/options")]
    public async Task<IActionResult> GetRegistrationOptions([FromBody] PasskeyRegisterRequest request)
    {
        // .NET 10 Identity xử lý nội bộ thông qua WebAuthn ceremony
        // Đây là ví dụ flow thủ công nếu bạn muốn custom

        var user = new IdentityUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        // Tạo challenge ngẫu nhiên
        var challenge = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

        var options = new
        {
            challenge = Convert.ToBase64String(challenge),
            rp = new
            {
                name = "My App",
                id = "localhost"
            },
            user = new
            {
                id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(request.Email)),
                name = request.Email,
                displayName = request.DisplayName
            },
            pubKeyCredParams = new[]
            {
                new { type = "public-key", alg = -7 },   // ES256
                new { type = "public-key", alg = -257 }  // RS256
            },
            authenticatorSelection = new
            {
                authenticatorAttachment = "platform",
                requireResidentKey = true,
                userVerification = "required"
            },
            timeout = 60000,
            attestation = "none"
        };

        // Lưu challenge vào session/cache để verify sau
        HttpContext.Session.SetString("passkey_challenge", Convert.ToBase64String(challenge));

        return Ok(options);
    }

    /// <summary>
    /// Bước 2: Verify và lưu credential
    /// </summary>
    [HttpPost("register/verify")]
    public async Task<IActionResult> VerifyRegistration([FromBody] PasskeyCredential credential)
    {
        // Verify attestation response
        // .NET 10 Identity có PasskeyStore xử lý việc này
        // Ở đây minh họa flow

        var user = new IdentityUser
        {
            UserName = credential.Id,
            Email = credential.Id
        };

        var result = await _userManager.CreateAsync(user);
        if (result.Succeeded)
        {
            // Lưu public key credential vào database
            // .NET 10 Identity tự xử lý qua PasskeyStore
            return Ok(new { success = true, message = "Passkey registered!" });
        }

        return BadRequest(result.Errors);
    }

    /// <summary>
    /// Đăng nhập với Passkey
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> LoginWithPasskey([FromBody] PasskeyLoginRequest request)
    {
        // .NET 10: SignInManager hỗ trợ passkey authentication
        // var result = await _signInManager.PasskeySignInAsync(request.Credential);

        return Ok(new { success = true, token = "jwt-token-here" });
    }
}

// Request/Response models
public record PasskeyRegisterRequest(string DisplayName, string Email);
public record PasskeyCredential(string Id, string RawId, object Response, string Type);
public record PasskeyLoginRequest(object Credential);
```

> **Lưu ý:** Với .NET 10 Identity templates, phần lớn flow WebAuthn đã được xử lý tự động. Code trên minh họa cách hoạt động bên dưới. Trong thực tế, chỉ cần `options.Passkeys.Enabled = true` và dùng Identity UI scaffold.

---

## 8. Microsoft.Extensions.AI - Tích Hợp AI

### 8.1. Tổng Quan

`Microsoft.Extensions.AI` là abstraction layer thống nhất cho AI trong .NET:
- 🤖 **IChatClient** — interface chung cho mọi LLM (OpenAI, Azure, Ollama, Anthropic)
- 📊 **IEmbeddingGenerator** — tạo embeddings cho vector search
- 🔌 **DI-friendly** — tích hợp hoàn hảo với ASP.NET Core
- 🔄 **Middleware pipeline** — logging, caching, rate limiting cho AI calls
- 🧩 **Tương thích Semantic Kernel** — dùng chung hoặc riêng

### 8.2. Cài Đặt

```bash
dotnet new webapi -n AiDemo --framework net10.0
cd AiDemo

# Core abstractions
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI.OpenAI

# Hoặc cho Azure OpenAI
dotnet add package Microsoft.Extensions.AI.AzureAIInference

# Hoặc cho Ollama (local)
dotnet add package Microsoft.Extensions.AI.Ollama
```

### 8.3. Code Mẫu Hoàn Chỉnh — AI-Powered Web API

```csharp
// File: Program.cs
using Microsoft.Extensions.AI;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

// ✅ Đăng ký AI Chat Client (chọn 1 trong 3 provider)

// Option 1: OpenAI
builder.Services.AddChatClient(sp =>
    new OpenAI.OpenAIClient("your-api-key")
        .GetChatClient("gpt-4o")
        .AsIChatClient()
);

// // Option 2: Azure OpenAI
// builder.Services.AddChatClient(sp =>
//     new Azure.AI.OpenAI.AzureOpenAIClient(
//         new Uri("https://your-resource.openai.azure.com/"),
//         new Azure.AzureKeyCredential("your-key"))
//         .GetChatClient("gpt-4o")
//         .AsIChatClient()
// );

// // Option 3: Ollama (chạy local, miễn phí)
// builder.Services.AddChatClient(sp =>
//     new OllamaChatClient("http://localhost:11434", "llama3.2")
// );

// ✅ Đăng ký middleware pipeline cho AI
builder.Services.AddChatClient(pipeline => pipeline
    .UseLogging()                    // Log mọi AI call
    .UseFunctionInvocation()         // Cho phép AI gọi function
    .UseOpenTelemetry()              // Tracing
    .Use(inner => inner)             // Custom middleware
);

// Đăng ký services
builder.Services.AddSingleton<ProductSearchService>();
builder.Services.AddSingleton<AiChatService>();

var app = builder.Build();

// ── AI Chat Endpoint ──
app.MapPost("/api/ai/chat", async (
    ChatRequest request,
    AiChatService chatService) =>
{
    var response = await chatService.ChatAsync(request.Message, request.SystemPrompt);
    return Results.Ok(new { response });
})
.WithName("AiChat")
.WithOpenApi();

// ── AI Streaming Chat ──
app.MapPost("/api/ai/chat/stream", (
    ChatRequest request,
    AiChatService chatService,
    CancellationToken ct) =>
{
    return TypedResults.ServerSentEvents(
        chatService.StreamChatAsync(request.Message, request.SystemPrompt, ct),
        eventType: "ai-token"
    );
})
.WithName("AiChatStream")
.WithOpenApi();

// ── AI với Function Calling ──
app.MapPost("/api/ai/assistant", async (
    ChatRequest request,
    IChatClient chatClient,
    ProductSearchService productService) =>
{
    // ✅ Định nghĩa tools mà AI có thể gọi
    var chatOptions = new ChatOptions
    {
        Tools =
        [
            AIFunctionFactory.Create(
                (string query) => productService.SearchProducts(query),
                "searchProducts",
                "Tìm kiếm sản phẩm theo tên hoặc category"
            ),
            AIFunctionFactory.Create(
                (int productId) => productService.GetProductDetails(productId),
                "getProductDetails",
                "Lấy chi tiết sản phẩm theo ID"
            ),
            AIFunctionFactory.Create(
                () => productService.GetTopProducts(5),
                "getTopProducts",
                "Lấy top sản phẩm bán chạy"
            )
        ]
    };

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, """
            Bạn là trợ lý bán hàng thông minh.
            Sử dụng các tools có sẵn để tìm kiếm và giới thiệu sản phẩm cho khách hàng.
            Trả lời bằng tiếng Việt, thân thiện và chuyên nghiệp.
            """),
        new(ChatRole.User, request.Message)
    };

    var response = await chatClient.GetResponseAsync(messages, chatOptions);
    return Results.Ok(new { response = response.Text });
})
.WithName("AiAssistant")
.WithOpenApi();

// ── AI Summarize ──
app.MapPost("/api/ai/summarize", async (
    SummarizeRequest request,
    IChatClient chatClient) =>
{
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "Tóm tắt nội dung sau đây bằng tiếng Việt, ngắn gọn và đầy đủ ý chính:"),
        new(ChatRole.User, request.Content)
    };

    var response = await chatClient.GetResponseAsync(messages, new ChatOptions
    {
        MaxOutputTokens = 500,
        Temperature = 0.3f
    });

    return Results.Ok(new
    {
        summary = response.Text,
        originalLength = request.Content.Length,
        summaryLength = response.Text?.Length ?? 0
    });
})
.WithName("AiSummarize")
.WithOpenApi();

app.Run();

// ── Models ──
public record ChatRequest(string Message, string? SystemPrompt = null);
public record SummarizeRequest(string Content);

// ── AI Chat Service ──
public class AiChatService
{
    private readonly IChatClient _chatClient;

    public AiChatService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> ChatAsync(string message, string? systemPrompt = null)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new(ChatRole.System, systemPrompt));

        messages.Add(new(ChatRole.User, message));

        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0.7f,
            MaxOutputTokens = 1000
        });

        return response.Text ?? "Không có phản hồi";
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string message,
        string? systemPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new(ChatRole.System, systemPrompt));

        messages.Add(new(ChatRole.User, message));

        // ✅ Streaming response
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is not null)
                yield return update.Text;
        }
    }
}

// ── Product Service (cho Function Calling) ──
public class ProductSearchService
{
    private readonly List<ProductInfo> _products =
    [
        new(1, "iPhone 16 Pro Max", "Electronics", 34_990_000, 4.8, 1250),
        new(2, "MacBook Air M4", "Electronics", 32_490_000, 4.9, 890),
        new(3, "Samsung Galaxy S25 Ultra", "Electronics", 31_990_000, 4.7, 750),
        new(4, "Sony WH-1000XM5", "Audio", 7_990_000, 4.8, 2100),
        new(5, "iPad Pro M4", "Electronics", 28_990_000, 4.9, 650),
        new(6, "AirPods Pro 3", "Audio", 6_490_000, 4.7, 3200),
        new(7, "Dell XPS 15", "Electronics", 35_990_000, 4.6, 420),
        new(8, "Keychron K2 Pro", "Accessories", 2_290_000, 4.5, 1800),
    ];

    [Description("Tìm kiếm sản phẩm theo từ khóa")]
    public List<ProductInfo> SearchProducts(string query) =>
        _products.Where(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        .ToList();

    [Description("Lấy chi tiết một sản phẩm")]
    public ProductInfo? GetProductDetails(int productId) =>
        _products.FirstOrDefault(p => p.Id == productId);

    [Description("Lấy top sản phẩm bán chạy")]
    public List<ProductInfo> GetTopProducts(int count) =>
        _products.OrderByDescending(p => p.SoldCount).Take(count).ToList();
}

public record ProductInfo(
    int Id, string Name, string Category,
    decimal Price, double Rating, int SoldCount);
```

### 8.4. Test AI Chat Client

```bash
# Chat đơn giản
curl -X POST http://localhost:5000/api/ai/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Giới thiệu về .NET 10", "systemPrompt": "Trả lời ngắn gọn bằng tiếng Việt"}'

# AI Assistant với Function Calling
curl -X POST http://localhost:5000/api/ai/assistant \
  -H "Content-Type: application/json" \
  -d '{"message": "Tìm cho tôi tai nghe tốt nhất dưới 10 triệu"}'

# Streaming (EventSource)
curl -N http://localhost:5000/api/ai/chat/stream \
  -H "Content-Type: application/json" \
  -d '{"message": "Viết một bài thơ ngắn về lập trình"}'
```

---

## 9. .NET Aspire - Cloud-Native Orchestration

### 9.1. Tổng Quan

.NET Aspire là framework orchestration cho ứng dụng phân tán:
- 🎯 **Orchestration bằng C#** — không cần Docker Compose, YAML
- 📊 **Dashboard tích hợp** — logs, traces, metrics, topology
- 🔌 **Integrations** — Redis, PostgreSQL, RabbitMQ, Kafka, Azure...
- 🚀 **F5 experience** — chạy toàn bộ microservices với 1 lệnh
- ☁️ **Deploy-ready** — xuất Docker Compose, Kubernetes manifest

### 9.2. Cài Đặt

```bash
# Cài .NET Aspire workload
dotnet workload install aspire

# Tạo solution Aspire
dotnet new aspire -n MyShopPlatform
cd MyShopPlatform
```

### 9.3. Cấu Trúc Project

```
MyShopPlatform/
├── MyShopPlatform.AppHost/          # ⭐ Orchestrator
│   └── Program.cs
├── MyShopPlatform.ServiceDefaults/  # Shared config (OpenTelemetry, Health)
│   └── Extensions.cs
├── MyShopPlatform.ApiService/       # API Gateway / BFF
│   └── Program.cs
└── MyShopPlatform.Web/              # Blazor Frontend
    └── Program.cs
```

### 9.4. Code Mẫu Hoàn Chỉnh

#### AppHost — Orchestrator (trái tim của Aspire)

```csharp
// File: MyShopPlatform.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure ──

// 🗃️ PostgreSQL + 2 databases
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()                    // Tự động thêm pgAdmin UI
    .WithDataVolume("postgres-data"); // Persist data

var catalogDb = postgres.AddDatabase("catalogdb");
var orderDb = postgres.AddDatabase("orderdb");

// 🔴 Redis cho caching
var redis = builder.AddRedis("cache")
    .WithRedisInsight();              // Redis Insight UI

// 🐰 RabbitMQ cho messaging
var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();          // RabbitMQ Management UI

// ── Microservices ──

// 📦 Catalog Service
var catalogService = builder.AddProject<Projects.CatalogService>("catalog-service")
    .WithReference(catalogDb)         // Inject DB connection
    .WithReference(redis)             // Inject Redis
    .WithHttpEndpoint(port: 5010)
    .WithReplicas(2);                 // ✅ 2 replicas cho HA

// 🛒 Order Service
var orderService = builder.AddProject<Projects.OrderService>("order-service")
    .WithReference(orderDb)
    .WithReference(rabbitmq)          // Inject RabbitMQ
    .WithReference(redis)
    .WithHttpEndpoint(port: 5020);

// 💳 Payment Service
var paymentService = builder.AddProject<Projects.PaymentService>("payment-service")
    .WithReference(rabbitmq)
    .WithHttpEndpoint(port: 5030);

// 🌐 API Gateway (BFF)
var apiGateway = builder.AddProject<Projects.ApiGateway>("api-gateway")
    .WithReference(catalogService)    // Service discovery tự động!
    .WithReference(orderService)
    .WithReference(redis)
    .WithExternalHttpEndpoints()      // Expose ra ngoài
    .WithHttpEndpoint(port: 5000);

// 🖥️ Blazor Frontend
builder.AddProject<Projects.WebFrontend>("web-frontend")
    .WithReference(apiGateway)        // Connect to API Gateway
    .WithExternalHttpEndpoints()
    .WithHttpEndpoint(port: 5100);

// 🏗️ Build & Run
builder.Build().Run();
```

#### ServiceDefaults — Shared Configuration

```csharp
// File: MyShopPlatform.ServiceDefaults/Extensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MyShopPlatform.ServiceDefaults;

public static class Extensions
{
    /// <summary>
    /// Thêm tất cả service defaults: OpenTelemetry, Health Checks, Service Discovery
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // ✅ OpenTelemetry
        builder.ConfigureOpenTelemetry();

        // ✅ Health Checks
        builder.AddDefaultHealthChecks();

        // ✅ Service Discovery
        builder.Services.AddServiceDiscovery();

        // ✅ HTTP Client với service discovery + resilience
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(); // Retry, Circuit Breaker, Timeout
            http.AddServiceDiscovery();          // Tự resolve tên service → URL
        });

        return builder;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
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
                tracing.AddSource(builder.Environment.ApplicationName)
                       .AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlp = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Map health check endpoints
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness
        app.MapHealthChecks("/health");

        // Readiness (kiểm tra dependencies)
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
```

#### Catalog Service — Microservice Mẫu

```csharp
// File: CatalogService/Program.cs
using MyShopPlatform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ✅ Thêm service defaults (OpenTelemetry, Health, Discovery)
builder.AddServiceDefaults();

// ✅ Thêm PostgreSQL từ Aspire
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

// ✅ Thêm Redis từ Aspire
builder.AddRedisDistributedCache("cache");

// HybridCache
builder.Services.AddHybridCache();

var app = builder.Build();

// ✅ Map health check endpoints
app.MapDefaultEndpoints();

// ── API Endpoints ──
var catalog = app.MapGroup("/api/catalog");

catalog.MapGet("/products", async (CatalogDbContext db) =>
{
    var products = await db.Products.ToListAsync();
    return Results.Ok(products);
});

catalog.MapGet("/products/{id:int}", async (int id, CatalogDbContext db, HybridCache cache) =>
{
    var product = await cache.GetOrCreateAsync(
        $"product:{id}",
        async ct => await db.Products.FindAsync(id),
        tags: ["products"]);

    return product is not null ? Results.Ok(product) : Results.NotFound();
});

catalog.MapPost("/products", async (Product product, CatalogDbContext db, HybridCache cache) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    await cache.RemoveByTagAsync("products");
    return TypedResults.Created($"/api/catalog/products/{product.Id}", product);
});

app.Run();

// ── DbContext ──
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }
    public DbSet<Product> Products => Set<Product>();
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public int Stock { get; set; }
}
```

### 9.5. Chạy Toàn Bộ Hệ Thống

```bash
# Chạy AppHost → tự động khởi động TẤT CẢ services + infrastructure
cd MyShopPlatform.AppHost
dotnet run

# Output:
# ✅ Dashboard: https://localhost:17262
# ✅ API Gateway: http://localhost:5000
# ✅ Web Frontend: http://localhost:5100
# ✅ PostgreSQL: localhost:5432
# ✅ Redis: localhost:6379
# ✅ RabbitMQ: localhost:5672
```

### 9.6. Aspire Dashboard

Truy cập `https://localhost:17262` để xem:
- 🗺️ **Topology** — sơ đồ kết nối giữa các services
- 📊 **Metrics** — request rate, latency, error rate
- 📝 **Logs** — structured logs tập trung
- 🔍 **Traces** — distributed tracing xuyên suốt services
- ❤️ **Health** — trạng thái health check mỗi service

---

## 📊 Tổng Kết & So Sánh

| Công nghệ | Mục đích | Độ khó | Ưu tiên |
|-----------|---------|--------|---------|
| C# 14 `field` keyword | Code sạch hơn | ⭐ Dễ | Áp dụng ngay |
| OpenAPI 3.1 | API documentation | ⭐ Dễ | Áp dụng ngay |
| Minimal API + SSE | Real-time streaming | ⭐⭐ TB | Rất nên dùng |
| HybridCache | Caching hiệu quả | ⭐⭐ TB | Thay thế IDistributedCache |
| Native AOT | Performance | ⭐⭐ TB | Serverless/Container |
| Blazor 10 | Interactive UI | ⭐⭐ TB | Nếu dùng Blazor |
| Passkey Auth | Security | ⭐⭐⭐ Khó | Tương lai gần |
| Microsoft.Extensions.AI | AI integration | ⭐⭐⭐ Khó | Nếu cần AI |
| .NET Aspire | Microservices | ⭐⭐⭐ Khó | Hệ thống lớn |

---

## 🚀 Lộ Trình Tích Hợp Đề Xuất

```
Tuần 1-2: C# 14 features + OpenAPI 3.1 + Minimal API enhancements
     ↓
Tuần 3-4: HybridCache + Native AOT cho API services
     ↓
Tuần 5-6: Blazor 10 + Passkey Authentication
     ↓
Tuần 7-8: Microsoft.Extensions.AI integration
     ↓
Tuần 9-12: .NET Aspire cho microservices architecture
```

---

## 📚 Tài Liệu Tham Khảo

- [What's New in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [What's New in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [C# 14 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- [HybridCache Documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid)
- [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai/)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Passkey/WebAuthn in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys)
- [Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
