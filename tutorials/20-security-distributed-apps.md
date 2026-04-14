# Bài 20: Security trong Distributed .NET Applications

## Mục tiêu bài học

- Hiểu authentication vs authorization trong microservices
- Triển khai JWT-based authentication
- Service-to-service authentication với API Key và Client Credentials
- Quản lý secrets an toàn
- Bảo vệ API với HTTPS, CORS, security headers

---

## 1. Tổng quan Security trong Distributed Apps

```
┌──────────────────────────────────────────────────────┐
│                  Security Layers                      │
├──────────────────────────────────────────────────────┤
│  Identity Layer  │  IdentityService (JWT issuer)     │
│  Transport Layer │  HTTPS / TLS / mTLS               │
│  API Layer       │  JWT Bearer, API Keys, CORS       │
│  Data Layer      │  Encryption at rest, Secrets Mgmt │
└──────────────────────────────────────────────────────┘
```

**Authentication** = Xác minh bạn là ai (Who are you?)
**Authorization** = Xác minh bạn được làm gì (What can you do?)

Trong microservices, mỗi service cần:
1. Xác thực request đến (inbound)
2. Gắn identity khi gọi service khác (outbound)
3. Bảo vệ secrets (connection strings, API keys)

---

## 2. JWT Authentication cơ bản

### 2.1 Cấu trúc JWT

```
Header.Payload.Signature

eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
.eyJzdWIiOiJ1c2VyMTIzIiwibmFtZSI6IkpvaG4iLCJpYXQiOjE2MDAwMDAwMDB9
.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

- **Header**: thuật toán ký (HS256, RS256)
- **Payload**: claims (sub, name, roles, exp...)
- **Signature**: đảm bảo token không bị tamper

### 2.2 Tạo IdentityService phát JWT

Tạo project:
```bash
dotnet new webapi -n IdentityService
cd IdentityService
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
```

`appsettings.json`:
```json
{
  "Jwt": {
    "Key": "super-secret-key-at-least-32-characters-long!!",
    "Issuer": "IdentityService",
    "Audience": "DistributedApp",
    "ExpiryMinutes": 60
  }
}
```

`Models/LoginRequest.cs`:
```csharp
namespace IdentityService.Models;

public record LoginRequest(string Username, string Password);

public record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
```

`Services/TokenService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public class TokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(string userId, string username, IEnumerable<string> roles)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Name, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        // Thêm roles
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var expiry = int.Parse(_config["Jwt:ExpiryMinutes"]!);
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiry),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

`Controllers/AuthController.cs`:
```csharp
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly TokenService _tokenService;

    // Demo: hardcoded users - production dùng DB
    private static readonly Dictionary<string, (string Password, string[] Roles)> Users = new()
    {
        ["alice"] = ("password123", ["user", "admin"]),
        ["bob"]   = ("password456", ["user"])
    };

    public AuthController(TokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (!Users.TryGetValue(request.Username, out var info) ||
            info.Password != request.Password)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var userId = Guid.NewGuid().ToString();
        var token = _tokenService.GenerateToken(userId, request.Username, info.Roles);
        var expiry = int.Parse(HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Jwt:ExpiryMinutes"]!) * 60;

        return Ok(new TokenResponse(token, "Bearer", expiry));
    }
}
```

`Program.cs` (IdentityService):
```csharp
using System.Text;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();
```

---

## 3. Bảo vệ API với JWT Bearer

### 3.1 OrderService với JWT validation

Tạo project:
```bash
dotnet new webapi -n OrderService
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
```

`Program.cs` (OrderService):
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtConfig = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtConfig["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtConfig["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtConfig["Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Logging JWT errors
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.Response.Headers.Append("Token-Error", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("user", "admin"));
});

builder.Services.AddControllers();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

`Controllers/OrderController.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Controllers;

public record OrderDto(Guid Id, string UserId, string Product, decimal Total);

[ApiController]
[Route("api/[controller]")]
[Authorize] // Tất cả endpoints yêu cầu auth
public class OrderController : ControllerBase
{
    // Lấy thông tin user từ JWT claims
    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private string CurrentUsername =>
        User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    [HttpGet]
    public IActionResult GetMyOrders()
    {
        // Chỉ trả về orders của user hiện tại
        var orders = new[]
        {
            new OrderDto(Guid.NewGuid(), CurrentUserId, "Product A", 99.99m),
            new OrderDto(Guid.NewGuid(), CurrentUserId, "Product B", 49.99m)
        };

        return Ok(new
        {
            UserId = CurrentUserId,
            Username = CurrentUsername,
            Orders = orders
        });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")] // Chỉ admin mới xóa được
    public IActionResult DeleteOrder(Guid id)
    {
        return Ok(new { message = $"Order {id} deleted by admin {CurrentUsername}" });
    }

    [HttpGet("public")]
    [AllowAnonymous] // Endpoint công khai
    public IActionResult PublicInfo()
    {
        return Ok(new { message = "This is public info" });
    }
}
```

---

## 4. Service-to-Service Authentication

### 4.1 API Key cho inter-service calls

`Middleware/ApiKeyMiddleware.cs`:
```csharp
namespace OrderService.Middleware;

public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bỏ qua middleware này cho user-facing routes
        if (!context.Request.Path.StartsWithSegments("/api/internal"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API Key required" });
            return;
        }

        var validKey = _config["ServiceApiKey"];
        if (apiKey != validKey)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API Key" });
            return;
        }

        await _next(context);
    }
}
```

`Controllers/InternalController.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Controllers;

// Endpoints nội bộ - chỉ services khác gọi, dùng API Key
[ApiController]
[Route("api/internal/[controller]")]
public class InternalOrderController : ControllerBase
{
    [HttpGet("{userId}/summary")]
    public IActionResult GetUserOrderSummary(string userId)
    {
        return Ok(new
        {
            UserId = userId,
            TotalOrders = 5,
            TotalSpent = 499.95m
        });
    }
}
```

### 4.2 HttpClient với API Key

`Services/ProductServiceClient.cs`:
```csharp
namespace OrderService.Services;

public class ProductServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductServiceClient> _logger;

    public ProductServiceClient(HttpClient httpClient, ILogger<ProductServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(Guid productId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/internal/products/{productId}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch product {ProductId}", productId);
            return null;
        }
    }
}

public record ProductDto(Guid Id, string Name, decimal Price, int Stock);
```

Đăng ký trong `Program.cs`:
```csharp
// Inject API Key vào mọi request đến ProductService
builder.Services.AddHttpClient<ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ProductService"]!);
    client.DefaultRequestHeaders.Add("X-Api-Key",
        builder.Configuration["ServiceApiKey"]);
});
```

---

## 5. Secrets Management

### 5.1 User Secrets (Development)

```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "my-local-dev-secret-key-32chars!!"
dotnet user-secrets set "ServiceApiKey" "dev-internal-api-key-12345"
```

Đọc trong code:
```csharp
// Tự động đọc user secrets trong Development
var builder = WebApplication.CreateBuilder(args);
// builder.Configuration đã có user secrets
var jwtKey = builder.Configuration["Jwt:Key"]; // từ user secrets
```

### 5.2 Environment Variables (Production)

```bash
# Docker / Kubernetes
export Jwt__Key="prod-secret-key-at-least-32-chars!!"
export ServiceApiKey="prod-internal-api-key-xyz"
```

`appsettings.Production.json`:
```json
{
  "Jwt": {
    "Key": "${JWT_KEY}",
    "Issuer": "IdentityService",
    "Audience": "DistributedApp",
    "ExpiryMinutes": 30
  }
}
```

### 5.3 Azure Key Vault (Enterprise)

```bash
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
dotnet add package Azure.Identity
```

```csharp
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    var keyVaultUri = new Uri(builder.Configuration["KeyVault:Uri"]!);

    // Dùng Managed Identity trong Azure (không cần credentials hardcode)
    builder.Configuration.AddAzureKeyVault(
        keyVaultUri,
        new DefaultAzureCredential());
}
```

Trong Azure Key Vault, secret tên `Jwt--Key` (dùng `--` thay `/`) được tự động map thành `Jwt:Key`.

### 5.4 Kubernetes Secrets

```yaml
# secret.yaml
apiVersion: v1
kind: Secret
metadata:
  name: app-secrets
type: Opaque
stringData:
  jwt-key: "prod-secret-key-at-least-32-chars!!"
  service-api-key: "prod-internal-api-key-xyz"
```

```yaml
# deployment.yaml
env:
  - name: Jwt__Key
    valueFrom:
      secretKeyRef:
        name: app-secrets
        key: jwt-key
  - name: ServiceApiKey
    valueFrom:
      secretKeyRef:
        name: app-secrets
        key: service-api-key
```

---

## 6. HTTPS, CORS và Security Headers

### 6.1 HTTPS Enforcement

```csharp
var builder = WebApplication.CreateBuilder(args);

// Redirect HTTP -> HTTPS
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
});

// HSTS (chỉ production)
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
```

### 6.2 CORS Configuration

```csharp
builder.Services.AddCors(options =>
{
    // Policy cho frontend
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(
                "https://myapp.com",
                "https://admin.myapp.com")
              .WithMethods("GET", "POST", "PUT", "DELETE")
              .WithHeaders("Content-Type", "Authorization")
              .AllowCredentials();
    });

    // Policy cho development
    options.AddPolicy("DevPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors(app.Environment.IsDevelopment() ? "DevPolicy" : "FrontendPolicy");
```

### 6.3 Security Headers Middleware

```csharp
namespace OrderService.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Ngăn clickjacking
        headers.Append("X-Frame-Options", "DENY");

        // Ngăn MIME sniffing
        headers.Append("X-Content-Type-Options", "nosniff");

        // XSS protection (legacy browsers)
        headers.Append("X-XSS-Protection", "1; mode=block");

        // Content Security Policy
        headers.Append("Content-Security-Policy",
            "default-src 'self'; script-src 'self'; style-src 'self'");

        // Referrer policy
        headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Không lộ server info
        context.Response.Headers.Remove("Server");

        await _next(context);
    }
}
```

Đăng ký:
```csharp
app.UseMiddleware<SecurityHeadersMiddleware>();
```

---

## 7. Rate Limiting để chống brute force

```csharp
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    // Login endpoint: tối đa 5 request/phút per IP
    options.AddPolicy("LoginPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // API chung: 100 request/phút per user
    options.AddPolicy("ApiPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please slow down." }, token);
    };
});
```

Áp dụng lên controller:
```csharp
[HttpPost("login")]
[EnableRateLimiting("LoginPolicy")]
public IActionResult Login([FromBody] LoginRequest request) { ... }
```

---

## 8. OWASP Top Risks cho APIs

| Risk | Mô tả | Cách phòng |
|------|-------|-----------|
| Broken Object Level Auth | Truy cập resource của user khác | Luôn filter theo userId từ JWT |
| Broken Authentication | JWT yếu, không expire | Dùng strong key, set exp |
| Excessive Data Exposure | Trả về quá nhiều field | Dùng DTO thay vì entity trực tiếp |
| Rate Limiting | Brute force | AddRateLimiter |
| Mass Assignment | Bind toàn bộ properties | Dùng [Bind] hoặc DTO |
| Security Misconfiguration | Debug mode, default passwords | Cấu hình theo môi trường |
| Injection | SQL/Command injection | Parameterized queries, input validation |

### Ví dụ Broken Object Level Auth và cách fix:

```csharp
// ❌ SAI - user có thể xem order của người khác
[HttpGet("{orderId}")]
public async Task<IActionResult> GetOrder(Guid orderId)
{
    var order = await _db.Orders.FindAsync(orderId);
    return Ok(order);
}

// ✅ ĐÚNG - filter theo userId từ JWT
[HttpGet("{orderId}")]
public async Task<IActionResult> GetOrder(Guid orderId)
{
    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var order = await _db.Orders
        .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == currentUserId);

    if (order is null)
        return NotFound();

    return Ok(order);
}
```

---

## 9. Full Integration Flow

```
[Browser/App]
     │  POST /api/auth/login (username + password)
     ▼
[IdentityService]
     │  Validate credentials → Generate JWT
     │  Return: { accessToken, expiresIn }
     ▼
[Browser/App]
     │  GET /api/orders  (Authorization: Bearer <token>)
     ▼
[OrderService]
     │  ValidateToken (signature, issuer, audience, exp)
     │  Extract userId từ claims
     │  Query DB for userId's orders
     │  Internally: GET /api/internal/products/{id}
     │              (X-Api-Key: <service-api-key>)
     ▼
[ProductService]
     │  Validate API Key
     │  Return product info
     ▼
[OrderService → Browser]
     │  Return orders with product details
```

---

## 10. Checklist Security trước khi go-live

- [ ] JWT key dài ≥ 32 ký tự, không hardcode trong source code
- [ ] JWT có expiry (exp claim), access token ≤ 60 phút
- [ ] HTTPS bắt buộc, HSTS enabled trong production
- [ ] CORS chỉ allow origins cần thiết
- [ ] Rate limiting cho authentication endpoints
- [ ] Security headers đã được set
- [ ] Secrets lưu trong Key Vault / Secrets Manager, không trong appsettings
- [ ] Authorization check theo userId (không chỉ theo role)
- [ ] Input validation trên tất cả endpoints
- [ ] Logging không ghi nhận sensitive data (password, token)
- [ ] Dependencies được cập nhật, không có known CVEs

---

## Tổng kết

Bài học này đã trình bày các lớp bảo mật cần thiết cho distributed .NET app:

1. **JWT Authentication** - IdentityService phát token, các service validate
2. **Role-based Authorization** - `[Authorize(Roles = "admin")]`
3. **Service-to-Service** - API Key cho internal endpoints
4. **Secrets Management** - User Secrets → Env Vars → Key Vault
5. **Transport Security** - HTTPS + HSTS
6. **CORS** - Chỉ allow origins cần thiết
7. **Rate Limiting** - Chống brute force
8. **Security Headers** - Defense in depth
9. **OWASP** - Filter theo userId, dùng DTO, validate input

Security không phải là một tính năng thêm vào sau - nó phải được thiết kế từ đầu trong mọi layer của distributed application.
