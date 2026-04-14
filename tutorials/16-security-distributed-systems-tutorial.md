# Security in Distributed Systems: Zero Trust, OAuth2/OIDC, mTLS và Secret Management

## 1. Tại sao Security trong Distributed System phức tạp hơn Monolith

Trong monolith, security thường đơn giản: một auth layer ở cổng vào, data trong một database, ít attack surface. Khi chuyển sang microservices, attack surface mở rộng đáng kể:

- Nhiều service giao tiếp qua network nội bộ → cần bảo vệ cả network nội bộ
- Nhiều database và storage → data breach risk cao hơn
- Service-to-service communication → cần xác thực giữa service, không chỉ user-to-service
- Secret management phức tạp hơn: mỗi service cần credentials của riêng nó
- Third-party dependencies nhiều hơn → supply chain risk
- Container và Kubernetes → cần bảo vệ container runtime

**Zero Trust** là nguyên tắc cốt lõi: không tin tưởng bất cứ ai hoặc bất cứ thứ gì mặc định, kể cả traffic từ bên trong network nội bộ. Mọi request đều phải được xác thực và phân quyền.

---

## 2. OAuth2 và OIDC: xác thực người dùng

### 2.1 Authentication Flow cơ bản

```text
User ──► Web App ──► Authorization Server (Keycloak/Auth0/Azure AD)
              │                    │
              │◄──── ID Token ─────┘
              │◄── Access Token ───┘
              │
              └──► API Gateway (validate token)
                        │
                        └──► Microservices (trust gateway headers)
```

### 2.2 Setup JWT Bearer Authentication

```csharp
// API Gateway: validate token với OIDC discovery
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["Auth:Authority"]; // https://auth.myapp.com
        opts.Audience = builder.Configuration["Auth:Audience"];   // myapp-api
        
        // Validate token đầy đủ
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30) // Cho phép clock skew 30 giây
        };
        
        // Support cho JWT trong query string (WebSocket, SignalR)
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Authentication failed: {Error}", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
        
        // Cache OIDC discovery document
        opts.BackchannelTimeout = TimeSpan.FromSeconds(10);
        opts.RefreshOnIssuerKeyNotFound = true;
    });
```

### 2.3 Authorization Policies phức tạp

```csharp
builder.Services.AddAuthorization(opts =>
{
    // Policy dựa trên scope
    opts.AddPolicy("orders:read", policy =>
        policy.RequireClaim("scope", "orders:read", "orders:admin"));
    
    opts.AddPolicy("orders:write", policy =>
        policy.RequireClaim("scope", "orders:write", "orders:admin"));
    
    // Policy dựa trên role VÀ scope
    opts.AddPolicy("OrdersAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", "orders-admin");
        policy.RequireClaim("scope", "orders:admin");
    });
    
    // Custom policy: chỉ owner hoặc admin
    opts.AddPolicy("OrderOwnerOrAdmin", policy =>
        policy.AddRequirements(new OrderOwnerOrAdminRequirement()));
    
    // Resource-based authorization
    opts.AddPolicy("SameTenant", policy =>
        policy.AddRequirements(new SameTenantRequirement()));
});

// Custom authorization handler
public class OrderOwnerOrAdminHandler : AuthorizationHandler<OrderOwnerOrAdminRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        OrderOwnerOrAdminRequirement requirement,
        Order resource)
    {
        var userId = ctx.User.FindFirst("sub")?.Value;
        var isAdmin = ctx.User.IsInRole("orders-admin");
        
        if (isAdmin || resource.CustomerId.ToString() == userId)
        {
            ctx.Succeed(requirement);
        }
        
        return Task.CompletedTask;
    }
}

// Sử dụng resource-based authorization
app.MapGet("/orders/{id}", async (
    Guid id,
    IAuthorizationService authz,
    HttpContext ctx,
    IOrderRepository repo,
    CancellationToken ct) =>
{
    var order = await repo.FindByIdAsync(id, ct);
    if (order is null) return Results.NotFound();
    
    var result = await authz.AuthorizeAsync(ctx.User, order, "OrderOwnerOrAdmin");
    if (!result.Succeeded) return Results.Forbid();
    
    return Results.Ok(order);
});
```

---

## 3. Service-to-Service Authentication

### 3.1 Client Credentials Flow

```csharp
// Service A gọi Service B bằng Client Credentials (machine-to-machine)
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>()
    .AddClientCredentialsTokenHandler("inventory-service");

builder.Services.AddClientCredentials(opts =>
{
    opts.AddClient("inventory-service", client =>
    {
        client.TokenEndpoint = new Uri($"{builder.Configuration["Auth:Authority"]}/connect/token");
        client.ClientId = builder.Configuration["Auth:ClientId"]!;
        client.ClientSecret = builder.Configuration["Auth:ClientSecret"]!;
        client.Scope = "inventory:read inventory:write";
    });
});

// Downstream service: validate token
builder.Services.AddAuthentication()
    .AddJwtBearer("ServiceAuth", opts =>
    {
        opts.Authority = builder.Configuration["Auth:Authority"];
        opts.Audience = "inventory-service";
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true
        };
    });
```

### 3.2 mTLS: Mutual TLS cho service-to-service

mTLS yêu cầu cả hai phía đều phải xuất trình certificate hợp lệ.

```csharp
// Service A: gửi client certificate khi gọi Service B
builder.Services.AddHttpClient<IPaymentClient, PaymentClient>()
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var cert = LoadCertificate("payment-client-cert.pfx", "password");
        
        return new HttpClientHandler
        {
            ClientCertificates = { cert },
            ClientCertificateOptions = ClientCertificateOption.Manual,
            // Validate server certificate
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Validate cert từ internal CA
                return ValidateInternalCertificate(cert, chain, errors);
            }
        };
    });

// Service B: require client certificate
builder.Services.AddAuthentication()
    .AddCertificate(opts =>
    {
        opts.AllowedCertificateTypes = CertificateTypes.Chained;
        opts.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
        opts.CustomTrustStore.Add(LoadInternalCACertificate());
        
        opts.Events = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = ctx =>
            {
                // Extract service identity từ cert
                var serviceId = ctx.ClientCertificate.GetNameInfo(
                    X509NameType.SimpleName, false);
                
                var claims = new[] 
                {
                    new Claim(ClaimTypes.Name, serviceId),
                    new Claim("service-id", serviceId)
                };
                
                ctx.Principal = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, CertificateAuthenticationDefaults.AuthenticationScheme));
                ctx.Success();
                return Task.CompletedTask;
            }
        };
    });
```

---

## 4. Secret Management: không hardcode secrets

### 4.1 Azure Key Vault

```csharp
// Đọc secrets từ Azure Key Vault
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    new DefaultAzureCredential()); // Dùng Managed Identity trong production

// Secrets tự động refresh
builder.Configuration.AddAzureKeyVault(
    vaultUri: new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    credential: new DefaultAzureCredential(),
    new AzureKeyVaultConfigurationOptions
    {
        ReloadInterval = TimeSpan.FromMinutes(15) // Refresh mỗi 15 phút
    });
```

### 4.2 HashiCorp Vault

```csharp
// VaultSharp để đọc từ HashiCorp Vault
var vaultClientSettings = new VaultClientSettings(
    builder.Configuration["Vault:Address"],
    new KubernetesAuthMethodInfo(
        roleName: builder.Configuration["Vault:RoleName"],
        jwt: File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/token")));

var vaultClient = new VaultClient(vaultClientSettings);

// Đọc database credentials
var secret = await vaultClient.V1.Secrets.KeyValue.V2
    .ReadSecretAsync(path: "database/order-service", mountPoint: "kv");

var dbConnectionString = $"Host={secret.Data.Data["host"]};" +
                          $"Username={secret.Data.Data["username"]};" +
                          $"Password={secret.Data.Data["password"]}";

// Dynamic credentials: Vault tạo DB credentials tạm thời
var dynamicSecret = await vaultClient.V1.Secrets.Database
    .GetCredentialsAsync(roleName: "order-service-role");
// Credentials này tự expire sau lease_duration
```

### 4.3 Kubernetes Secrets với External Secrets Operator

```yaml
# ExternalSecret object - sync từ AWS Secrets Manager vào K8s Secret
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
metadata:
  name: order-service-secrets
spec:
  refreshInterval: 15m
  secretStoreRef:
    name: aws-secrets-manager
    kind: SecretStore
  target:
    name: order-service-secrets
    creationPolicy: Owner
  data:
    - secretKey: database-password
      remoteRef:
        key: order-service/database
        property: password
    - secretKey: redis-password
      remoteRef:
        key: order-service/redis
        property: password
```

---

## 5. Input Validation và Injection Prevention

```csharp
// Validate tất cả input với FluentValidation
public class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer ID is required");
        
        RuleFor(x => x.ShippingAddress)
            .NotEmpty().MaximumLength(500)
            .Matches(@"^[a-zA-Z0-9\s,.\-]+$")  // Chỉ cho phép safe characters
            .WithMessage("Invalid shipping address");
        
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Order must have at least one item")
            .Must(lines => lines.Count <= 100)
            .WithMessage("Order cannot have more than 100 items");
        
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku)
                .NotEmpty()
                .Matches(@"^[A-Z0-9\-]+$") // Safe SKU format
                .MaximumLength(50);
            
            line.RuleFor(l => l.Quantity)
                .GreaterThan(0)
                .LessThanOrEqualTo(1000);
        });
    }
}

// Middleware tự động validate
app.MapPost("/orders", async (
    PlaceOrderCommand cmd,
    IValidator<PlaceOrderCommand> validator,
    PlaceOrderHandler handler,
    CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(cmd, ct);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }
    
    var orderId = await handler.Handle(cmd, ct);
    return Results.Accepted($"/orders/{orderId}", new { orderId });
});
```

---

## 6. SQL Injection Prevention

```csharp
// Xấu: SQL injection vulnerable
var sql = $"SELECT * FROM orders WHERE customer_id = '{customerId}'";
await conn.QueryAsync<Order>(sql);

// Tốt: Parameterized query
await conn.QueryAsync<Order>(
    "SELECT * FROM orders WHERE customer_id = @customerId",
    new { customerId });

// Với EF Core: luôn safe nếu dùng LINQ (không string interpolation trong FromSql)
var orders = await _db.Orders
    .Where(o => o.CustomerId == customerId)
    .ToListAsync(ct);

// Khi cần raw SQL với EF Core: dùng FromSqlInterpolated (safe)
var orders = await _db.Orders
    .FromSqlInterpolated($"SELECT * FROM orders WHERE customer_id = {customerId}")
    .ToListAsync(ct);

// KHÔNG dùng FromSqlRaw với string concatenation:
// .FromSqlRaw($"SELECT * FROM orders WHERE id = '{orderId}'")  // VULNERABLE!
```

---

## 7. Rate Limiting và DDoS Protection

```csharp
// Rate limiting theo multiple dimensions
builder.Services.AddRateLimiter(opts =>
{
    // Per-user rate limit
    opts.AddPolicy("PerUser", ctx =>
    {
        var userId = ctx.User.FindFirst("sub")?.Value;
        
        if (userId is not null)
        {
            return RateLimitPartition.GetTokenBucketLimiter(userId, _ =>
                new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    TokensPerPeriod = 100,
                    AutoReplenishment = true
                });
        }
        
        // Anonymous: stricter limit
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            });
    });
    
    // Per-endpoint rate limit cho sensitive endpoints
    opts.AddPolicy("StrictPerIp", ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15) // 5 attempts per 15 minutes
            });
    });
    
    opts.OnRejected = async (ctx, ct) =>
    {
        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Rate limit exceeded for {IP} on {Path}",
            ctx.HttpContext.Connection.RemoteIpAddress,
            ctx.HttpContext.Request.Path);
        
        ctx.HttpContext.Response.StatusCode = 429;
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsync("Rate limit exceeded", ct);
    };
});

// Áp dụng cho login/register endpoints
app.MapPost("/auth/login", ...)
    .RequireRateLimiting("StrictPerIp");

app.MapPost("/auth/forgot-password", ...)
    .RequireRateLimiting("StrictPerIp");
```

---

## 8. Data Encryption: at-rest và in-transit

```csharp
// Encrypt sensitive fields trước khi lưu vào database
public class EncryptedFieldConverter : ValueConverter<string, string>
{
    public EncryptedFieldConverter(IDataProtector protector)
        : base(
            v => protector.Protect(v),        // Encrypt khi save
            v => protector.Unprotect(v))      // Decrypt khi read
    { }
}

// EF Core configuration
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var protector = _dataProtectionProvider.CreateProtector("SensitiveData");
    var converter = new EncryptedFieldConverter(protector);
    
    modelBuilder.Entity<Customer>()
        .Property(c => c.TaxId)
        .HasConversion(converter);
    
    modelBuilder.Entity<PaymentMethod>()
        .Property(p => p.CardLast4)
        .HasConversion(converter);
}

// Data Protection setup
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(
        connectionString: builder.Configuration.GetConnectionString("Storage")!,
        containerName: "data-protection",
        blobName: "keys.xml")
    .ProtectKeysWithAzureKeyVault(
        keyIdentifier: new Uri(builder.Configuration["KeyVault:DataProtectionKey"]!),
        credential: new DefaultAzureCredential())
    .SetApplicationName("MyApp")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
```

---

## 9. Security Headers

```csharp
// Middleware thêm security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    ctx.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'";
    
    await next();
});
```

---

## 10. Checklist Security cho Distributed System

- [ ] Authentication tập trung tại API Gateway - downstream service không validate token trực tiếp
- [ ] Service-to-service auth: client credentials hoặc mTLS
- [ ] Không bao giờ hardcode secrets - dùng Key Vault hoặc Kubernetes Secrets
- [ ] Rotate secrets định kỳ (database passwords, API keys)
- [ ] Validate tất cả input ở API layer
- [ ] Parameterized queries - không bao giờ string concat SQL
- [ ] Encrypt sensitive data at rest
- [ ] TLS cho tất cả in-transit data (kể cả internal network)
- [ ] Rate limiting: per-user và per-IP
- [ ] Security headers: HSTS, CSP, X-Frame-Options
- [ ] OWASP Top 10 review cho mỗi new feature
- [ ] Dependency scanning: check CVE trong packages
- [ ] Audit log cho tất cả sensitive operations
- [ ] Principle of least privilege: service chỉ có quyền tối thiểu cần thiết
- [ ] Penetration testing định kỳ
- [ ] Incident response plan
