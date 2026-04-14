# Database Sharding, Multi-tenancy & Read Replicas: Scale database cho hàng tỉ người dùng

## 1. Tại sao một database không thể scale mãi

PostgreSQL và SQL Server là rất mạnh, nhưng chúng có giới hạn vật lý:

- Một node tốt nhất có thể handle ~100K-200K TPS (transactions per second)
- Storage limit của một server khoảng vài chục TB
- Write throughput bị giới hạn bởi single-master architecture
- Khi table có hàng tỉ rows, index scan trở nên chậm

Khi vượt qua giới hạn này, bạn cần các chiến lược scale database.

---

## 2. Các chiến lược scale database

### 2.1 Read Replicas: Scale read workload

Cách dễ nhất để scale read. Primary xử lý write, replica nhận replication log và serve read.

```csharp
// Cấu hình connection routing
public class DatabaseConnectionFactory
{
    private readonly string _primaryConnectionString;
    private readonly string[] _replicaConnectionStrings;
    private int _replicaIndex;

    public DatabaseConnectionFactory(IConfiguration config)
    {
        _primaryConnectionString = config["Database:Primary"]!;
        _replicaConnectionStrings = config.GetSection("Database:Replicas").Get<string[]>() ?? [];
    }

    public string GetWriteConnection() => _primaryConnectionString;

    public string GetReadConnection()
    {
        if (_replicaConnectionStrings.Length == 0)
            return _primaryConnectionString;

        // Round-robin qua các replicas
        var index = Interlocked.Increment(ref _replicaIndex) % _replicaConnectionStrings.Length;
        return _replicaConnectionStrings[Math.Abs(index)];
    }
}

// DbContext riêng cho read và write
public class WriteDbContext : AppDbContext
{
    public WriteDbContext(DatabaseConnectionFactory factory) 
        : base(factory.GetWriteConnection()) { }
}

public class ReadDbContext : AppDbContext
{
    public ReadDbContext(DatabaseConnectionFactory factory) 
        : base(factory.GetReadConnection())
    {
        // Read context không cần track changes
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }
}

// DI setup
builder.Services.AddScoped<WriteDbContext>();
builder.Services.AddScoped<ReadDbContext>();
```

### 2.2 Connection Pooling tối ưu

```csharp
// appsettings.json
{
  "Database": {
    "Primary": "Host=primary-db;Database=app;Username=app;Password=secret;Maximum Pool Size=100;Minimum Pool Size=10;Connection Idle Lifetime=300;Connection Pruning Interval=10",
    "Replicas": [
      "Host=replica-1-db;Database=app;Username=app;Password=secret;Maximum Pool Size=200;Minimum Pool Size=20",
      "Host=replica-2-db;Database=app;Username=app;Password=secret;Maximum Pool Size=200;Minimum Pool Size=20"
    ]
  }
}
```

```csharp
// PgBouncer thêm một tầng pooling bên ngoài app
// Kết hợp PgBouncer (transaction pooling) + Npgsql connection pooling
public static class DatabaseExtensions
{
    public static IServiceCollection AddOptimizedDatabase(this IServiceCollection services, IConfiguration config)
    {
        // Write pool
        services.AddNpgsqlDataSource(
            config["Database:Primary"]!,
            builder =>
            {
                builder.ConnectionStringBuilder.MaxPoolSize = 50;
                builder.ConnectionStringBuilder.MinPoolSize = 5;
                builder.EnableParameterLogging(false);
            },
            serviceKey: "write");

        // Read pool
        services.AddNpgsqlDataSource(
            config["Database:Replicas:0"]!,
            builder =>
            {
                builder.ConnectionStringBuilder.MaxPoolSize = 100;
            },
            serviceKey: "read");

        return services;
    }
}
```

---

## 3. Database Sharding: chia data thành nhiều shard

Sharding chia data theo một shard key thành nhiều database độc lập. Ví dụ: user 1-1M vào shard 1, user 1M-2M vào shard 2.

### 3.1 Hash-based Sharding

```csharp
public class ShardRouter
{
    private readonly Dictionary<int, string> _shardConnections;
    private const int ShardCount = 8; // Số shard, thường là power of 2

    public ShardRouter(IConfiguration config)
    {
        _shardConnections = new Dictionary<int, string>();
        for (int i = 0; i < ShardCount; i++)
        {
            _shardConnections[i] = config[$"Database:Shards:{i}"]!;
        }
    }

    public int GetShardId(Guid entityId)
    {
        // Consistent hash: stable kể cả khi shard count thay đổi
        var hash = MurmurHash3.Hash128(entityId.ToByteArray());
        return (int)(hash % (ulong)ShardCount);
    }

    public string GetConnectionString(Guid entityId)
    {
        var shardId = GetShardId(entityId);
        return _shardConnections[shardId];
    }

    public IEnumerable<string> GetAllConnectionStrings() => _shardConnections.Values;
}

// Repository với shard routing
public class ShardedOrderRepository : IOrderRepository
{
    private readonly ShardRouter _router;

    public async Task<Order?> FindByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var connectionString = _router.GetConnectionString(orderId);
        
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        
        return await conn.QuerySingleOrDefaultAsync<Order>(
            "SELECT * FROM orders WHERE id = @id",
            new { id = orderId });
    }

    public async Task SaveAsync(Order order, CancellationToken ct = default)
    {
        var connectionString = _router.GetConnectionString(order.Id);
        
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        
        await conn.ExecuteAsync(
            "INSERT INTO orders (id, customer_id, total_amount, status, created_at) " +
            "VALUES (@id, @customerId, @totalAmount, @status, @createdAt) " +
            "ON CONFLICT (id) DO UPDATE SET status = @status",
            new { order.Id, order.CustomerId, order.TotalAmount, order.Status, order.CreatedAt });
    }

    // Cross-shard query: cần fan-out tới tất cả shards
    public async Task<List<Order>> SearchAcrossShardsAsync(
        OrderSearchCriteria criteria,
        CancellationToken ct = default)
    {
        var tasks = _router.GetAllConnectionStrings()
            .Select(async connStr =>
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);
                
                return await conn.QueryAsync<Order>(
                    "SELECT * FROM orders WHERE status = @status AND created_at > @from ORDER BY created_at DESC LIMIT 100",
                    new { criteria.Status, criteria.From });
            });

        var results = await Task.WhenAll(tasks);
        
        // Merge và sort kết quả từ tất cả shards
        return results
            .SelectMany(r => r)
            .OrderByDescending(o => o.CreatedAt)
            .Take(criteria.PageSize)
            .ToList();
    }
}
```

### 3.2 Range-based Sharding

```csharp
public class RangeShardRouter
{
    // Chia theo date range - tốt cho time-series data
    private readonly List<(DateTimeOffset From, DateTimeOffset To, string Connection)> _ranges;

    public RangeShardRouter(IConfiguration config)
    {
        _ranges = new List<(DateTimeOffset, DateTimeOffset, string)>
        {
            (DateTimeOffset.MinValue, new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), config["Database:Shard:Archive"]!),
            (new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), config["Database:Shard:2023"]!),
            (new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue, config["Database:Shard:Current"]!)
        };
    }

    public string GetConnectionForDate(DateTimeOffset date)
    {
        return _ranges.First(r => date >= r.From && date < r.To).Connection;
    }
}
```

---

## 4. Multi-tenancy: một hệ thống phục vụ nhiều tenant

Multi-tenancy là pattern cho phép một ứng dụng phục vụ nhiều customer (tenant) với data isolation.

### 4.1 Schema-per-Tenant

Mỗi tenant có schema riêng trong cùng database. Isolation tốt, quản lý dễ hơn database-per-tenant.

```csharp
// Tenant context middleware
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantCtx)
    {
        // Extract tenant từ nhiều nguồn
        string? tenantId = null;
        
        // Option 1: Từ subdomain (tenant1.myapp.com)
        var host = ctx.Request.Host.Host;
        if (host.Contains('.'))
            tenantId = host.Split('.')[0];
        
        // Option 2: Từ JWT claim
        tenantId ??= ctx.User.FindFirst("tenant_id")?.Value;
        
        // Option 3: Từ header
        tenantId ??= ctx.Request.Headers["X-Tenant-Id"].ToString();

        if (tenantId is null)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("Tenant not specified");
            return;
        }

        tenantCtx.SetTenant(tenantId);
        await _next(ctx);
    }
}

// Tenant-aware DbContext
public class TenantDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Đặt schema theo tenant
        modelBuilder.HasDefaultSchema(_tenantContext.TenantId);
        
        // Hoặc dùng global query filter
        modelBuilder.Entity<Order>().HasQueryFilter(o => o.TenantId == _tenantContext.TenantId);
    }
}
```

### 4.2 Row-level Security trong PostgreSQL

```sql
-- Enable RLS
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;

-- Policy: user chỉ thấy data của tenant mình
CREATE POLICY tenant_isolation ON orders
    USING (tenant_id = current_setting('app.current_tenant')::uuid);

-- App set tenant khi mở connection
SET app.current_tenant = 'tenant-uuid-here';
```

```csharp
// Set PostgreSQL setting khi mở connection
public class TenantAwareConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, 
        ConnectionEndEventData eventData, 
        CancellationToken ct = default)
    {
        if (connection is NpgsqlConnection npgsql && _tenantContext.TenantId is not null)
        {
            await using var cmd = npgsql.CreateCommand();
            cmd.CommandText = "SET app.current_tenant = @tenant";
            cmd.Parameters.AddWithValue("tenant", _tenantContext.TenantId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
```

### 4.3 Database-per-Tenant cho maximum isolation

```csharp
// Tenant database registry
public class TenantDatabaseRegistry
{
    private readonly IDatabase _redis; // Cache tenant → connection string
    private readonly ITenantRepository _repo;

    public async Task<string> GetConnectionStringAsync(string tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"tenant:conn:{tenantId}";
        var cached = await _redis.StringGetAsync(cacheKey);
        
        if (cached.HasValue)
            return cached!;

        var tenant = await _repo.FindByIdAsync(tenantId, ct)
            ?? throw new TenantNotFoundException(tenantId);

        await _redis.StringSetAsync(cacheKey, tenant.DatabaseConnectionString, TimeSpan.FromHours(1));
        return tenant.DatabaseConnectionString;
    }
}

// Tenant-aware DbContext factory
public class TenantDbContextFactory
{
    private readonly TenantDatabaseRegistry _registry;
    private readonly ITenantContext _tenantContext;

    public async Task<AppDbContext> CreateAsync(CancellationToken ct = default)
    {
        var connectionString = await _registry.GetConnectionStringAsync(
            _tenantContext.TenantId!, ct);
        
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString);
        
        return new AppDbContext(optionsBuilder.Options);
    }
}
```

---

## 5. PostgreSQL Partitioning: scale trong một database

Partitioning chia table lớn thành nhiều partition nhỏ, cùng database nhưng access pattern hiệu quả hơn.

```sql
-- Range partitioning theo thời gian (time-series data)
CREATE TABLE orders (
    id UUID NOT NULL,
    customer_id UUID NOT NULL,
    total_amount DECIMAL NOT NULL,
    status VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
) PARTITION BY RANGE (created_at);

-- Tạo partitions theo tháng
CREATE TABLE orders_2024_01 PARTITION OF orders
    FOR VALUES FROM ('2024-01-01') TO ('2024-02-01');

CREATE TABLE orders_2024_02 PARTITION OF orders
    FOR VALUES FROM ('2024-02-01') TO ('2024-03-01');

-- Index trên từng partition
CREATE INDEX idx_orders_2024_01_customer ON orders_2024_01(customer_id);
CREATE INDEX idx_orders_2024_02_customer ON orders_2024_02(customer_id);

-- Query planner sẽ tự prune partitions không cần thiết
-- Query này chỉ scan partition tháng 1
SELECT * FROM orders
WHERE created_at BETWEEN '2024-01-01' AND '2024-01-31';
```

```csharp
// Auto-create partition trước khi cần
public class PartitionManager : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Chạy đầu mỗi tháng để tạo partition tháng tới
        while (!ct.IsCancellationRequested)
        {
            var nextMonth = DateTimeOffset.UtcNow.AddMonths(1);
            
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS orders_{ym} PARTITION OF orders
                FOR VALUES FROM ('{from}') TO ('{to}')
                """.Replace("{ym}", nextMonth.ToString("yyyy_MM"))
                   .Replace("{from}", nextMonth.ToString("yyyy-MM-01"))
                   .Replace("{to}", nextMonth.AddMonths(1).ToString("yyyy-MM-01")));

            // Đợi đến đầu tháng tới
            var daysUntilNextMonth = (nextMonth - DateTimeOffset.UtcNow).Days;
            await Task.Delay(TimeSpan.FromDays(Math.Max(daysUntilNextMonth - 5, 1)), ct);
        }
    }
}
```

---

## 6. CQRS với Database Separation

```text
                     Write Side                          Read Side
                    ┌──────────┐                       ┌──────────┐
                    │ Command  │                       │  Query   │
                    │ Handler  │                       │ Handler  │
                    └────┬─────┘                       └────┬─────┘
                         │                                  │
                    ┌────▼─────┐                       ┌────▼─────┐
                    │ Primary  │──replication──────────►│ Replica  │
                    │  Write   │                        │   Read   │
                    │    DB    │                        │    DB    │
                    └──────────┘                        └──────────┘
                    (Normalized,                        (Denormalized,
                     ACID, low                          query-optimized,
                     latency writes)                    high read throughput)
```

```csharp
// Tự động route theo command/query
public class DatabaseSelector
{
    private readonly IServiceProvider _services;

    public AppDbContext GetContext(bool isWrite = false)
    {
        return isWrite
            ? _services.GetRequiredKeyedService<AppDbContext>("write")
            : _services.GetRequiredKeyedService<AppDbContext>("read");
    }
}

// Repository tự động chọn đúng database
public class OrderRepository : IOrderRepository
{
    private readonly DatabaseSelector _selector;

    public Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        // Read: dùng replica
        using var ctx = _selector.GetContext(isWrite: false);
        return ctx.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task SaveAsync(Order order, CancellationToken ct = default)
    {
        // Write: dùng primary
        using var ctx = _selector.GetContext(isWrite: true);
        ctx.Orders.Update(order);
        await ctx.SaveChangesAsync(ct);
    }
}
```

---

## 7. Global Distribution với CockroachDB / YugabyteDB

Cho hệ thống truly global, cần distributed SQL database.

```csharp
// CockroachDB dùng Npgsql driver như PostgreSQL
var builder = new NpgsqlConnectionStringBuilder
{
    Host = "cockroach-node1,cockroach-node2,cockroach-node3",
    Port = 26257,
    Database = "myapp",
    Username = "app",
    Password = "secret",
    SslMode = SslMode.Require,
    // Locality: ưu tiên node gần nhất
    TargetSessionAttributes = "any"
};

// Follow-the-Sun: route đến region gần nhất
public class GeoAwareConnectionRouter
{
    public string GetConnectionString(string userRegion)
    {
        return userRegion switch
        {
            "us-east" => "Host=us-east-db;...",
            "eu-west" => "Host=eu-west-db;...",
            "ap-southeast" => "Host=ap-db;...",
            _ => "Host=us-east-db;..." // default
        };
    }
}
```

---

## 8. Checklist production cho Database Scaling

- [ ] Setup read replicas và route read queries tới replica
- [ ] Connection pooling phù hợp: PgBouncer + Npgsql pool
- [ ] Partition lớn tables theo time hoặc business key
- [ ] Multi-tenancy strategy rõ ràng từ đầu (schema/row/database)
- [ ] Sharding strategy: hash-based cho uniform distribution, range-based cho time-series
- [ ] Cross-shard queries phải được giới hạn và optimize
- [ ] Monitor replication lag - alert khi replica tụt hậu quá nhiều
- [ ] Schema migration phải backward-compatible (không bao giờ drop column trực tiếp)
- [ ] Connection string không được hardcode - dùng secret manager
- [ ] Test failover: app hoạt động khi primary down (kết nối tới replica read)
- [ ] Backup và point-in-time recovery cho tất cả shards
- [ ] Database-level encryption at rest
