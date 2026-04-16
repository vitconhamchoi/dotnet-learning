# Bài 15: Distributed Caching với Redis trong .NET

> **Mục tiêu**: Hiểu và áp dụng distributed caching với Redis trong các ứng dụng .NET production-ready, bao gồm các pattern caching phổ biến, cache invalidation, session caching, và rate limiting.

---

## Mục lục

1. [Tại sao cần Distributed Caching?](#1-tại-sao-cần-distributed-caching)
2. [Redis Concepts - Các kiểu dữ liệu cơ bản](#2-redis-concepts---các-kiểu-dữ-liệu-cơ-bản)
3. [StackExchange.Redis trong .NET](#3-stackexchangeredis-trong-net)
4. [IDistributedCache Abstraction](#4-idistributedcache-abstraction)
5. [Cache Patterns](#5-cache-patterns)
6. [Cache Invalidation Strategies](#6-cache-invalidation-strategies)
7. [ProductService hoàn chỉnh với Redis Caching](#7-productservice-hoàn-chỉnh-với-redis-caching)
8. [Session Caching với Redis](#8-session-caching-với-redis)
9. [Rate Limiting với Redis](#9-rate-limiting-với-redis)
10. [Best Practices & Production Checklist](#10-best-practices--production-checklist)

---

## 1. Tại sao cần Distributed Caching?

### 1.1 Vấn đề với In-Memory Cache

Khi ứng dụng còn nhỏ và chạy trên **một server duy nhất**, in-memory cache (như `IMemoryCache` của .NET) hoạt động rất tốt. Tuy nhiên, khi hệ thống scale lên nhiều instance, in-memory cache bộc lộ nhiều vấn đề nghiêm trọng:

```
TRƯỚC KHI SCALE (1 instance) - In-Memory Cache hoạt động tốt:

┌─────────────────────────────────────────┐
│              API Server                  │
│                                         │
│  ┌──────────────┐   ┌────────────────┐  │
│  │  IMemoryCache│   │   App Logic    │  │
│  │              │   │                │  │
│  │  product:1   │◀──│  GET /products │  │
│  │  product:2   │   │                │  │
│  └──────────────┘   └────────────────┘  │
└─────────────────────────────────────────┘
                    │
              ┌─────▼─────┐
              │ Database   │
              └───────────┘
```

```
SAU KHI SCALE (nhiều instance) - In-Memory Cache GÂY VẤN ĐỀ:

┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│   API Server 1   │     │   API Server 2   │     │   API Server 3   │
│                  │     │                  │     │                  │
│  ┌────────────┐  │     │  ┌────────────┐  │     │  ┌────────────┐  │
│  │MemoryCache │  │     │  │MemoryCache │  │     │  │MemoryCache │  │
│  │ product:1  │  │     │  │ product:1  │  │     │  │ product:1  │  │
│  │ (version A)│  │     │  │ (version B)│  │     │  │ (stale!)   │  │
│  └────────────┘  │     │  └────────────┘  │     │  └────────────┘  │
└──────────────────┘     └──────────────────┘     └──────────────────┘
         ▲                        ▲                        ▲
         └────────────────────────┴────────────────────────┘
                                  │
                    Load Balancer phân tán request
                    → Mỗi server có cache KHÁC NHAU!
                    → Dữ liệu KHÔNG NHẤT QUÁN!
```

**Các vấn đề cụ thể:**

| Vấn đề | In-Memory Cache | Distributed Cache |
|--------|----------------|-------------------|
| **Consistency** | ❌ Mỗi instance có bản sao riêng | ✅ Tất cả instance dùng chung |
| **Scale** | ❌ Tăng instance = tăng memory | ✅ Cache độc lập với app server |
| **Restart** | ❌ Mất cache khi restart | ✅ Cache tồn tại độc lập |
| **Memory limit** | ❌ Bị giới hạn bởi RAM server | ✅ Có thể scale Redis riêng |
| **Monitoring** | ❌ Khó monitor | ✅ Redis có dashboard đầy đủ |

### 1.2 Distributed Caching giải quyết vấn đề như thế nào?

```
DISTRIBUTED CACHING - Giải pháp đúng đắn:

┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│  API Server 1│   │  API Server 2│   │  API Server 3│
│              │   │              │   │              │
│  App Logic   │   │  App Logic   │   │  App Logic   │
└──────┬───────┘   └──────┬───────┘   └──────┬───────┘
       │                  │                  │
       └──────────────────┼──────────────────┘
                          │  Tất cả cùng connect
                          ▼  đến Redis duy nhất
                ┌─────────────────┐
                │                 │
                │   Redis Cache   │
                │                 │
                │  product:1 → {} │
                │  product:2 → {} │
                │  search:xyz → []│
                └────────┬────────┘
                         │
                ┌────────▼────────┐
                │    Database     │
                │   (PostgreSQL)  │
                └─────────────────┘
```

### 1.3 Khi nào nên dùng Distributed Caching?

Sử dụng distributed caching khi:
- Ứng dụng chạy nhiều hơn 1 instance (horizontal scaling)
- Cần chia sẻ session giữa các server
- Dữ liệu được đọc thường xuyên nhưng ít thay đổi (catalog sản phẩm, cấu hình)
- Cần giảm tải database cho các query tốn kém
- Implement rate limiting, distributed locks

---

## 2. Redis Concepts - Các kiểu dữ liệu cơ bản

Redis không chỉ là một key-value store đơn giản. Nó hỗ trợ nhiều **cấu trúc dữ liệu** phong phú, mỗi loại phù hợp với một use case cụ thể.

### 2.1 Strings - Kiểu dữ liệu cơ bản nhất

String trong Redis có thể chứa text, số nguyên, hoặc dữ liệu binary (JSON, protobuf...).

```bash
# Lưu một giá trị đơn giản
SET product:1 "Laptop Dell XPS"

# Lấy giá trị
GET product:1
# → "Laptop Dell XPS"

# Lưu với TTL (Time-To-Live) - tự động xóa sau 3600 giây
SET product:1 "Laptop Dell XPS" EX 3600

# Lưu JSON
SET product:1 '{"id":1,"name":"Laptop Dell XPS","price":25000000}'

# Atomic increment - dùng cho counter, rate limiting
SET page_views 0
INCR page_views      # → 1
INCR page_views      # → 2
INCRBY page_views 5  # → 7

# Set nếu chưa tồn tại (dùng cho distributed lock)
SET lock:resource1 "server1" NX EX 30
```

**Use case phổ biến**: Cache đối tượng đơn lẻ (product, user, config), counters, feature flags.

### 2.2 Hashes - Lưu trữ đối tượng có cấu trúc

Hash trong Redis giống như một dictionary lồng bên trong một key. Phù hợp để lưu các đối tượng mà bạn cần đọc/cập nhật từng field riêng lẻ.

```bash
# Lưu thông tin user dạng hash
HSET user:1001 name "Nguyễn Văn An" email "an@example.com" role "admin" age 28

# Lấy một field
HGET user:1001 name
# → "Nguyễn Văn An"

# Lấy nhiều field
HMGET user:1001 name email
# → 1) "Nguyễn Văn An"
# → 2) "an@example.com"

# Lấy toàn bộ
HGETALL user:1001
# → 1) "name"
# → 2) "Nguyễn Văn An"
# → 3) "email"
# → 4) "an@example.com"

# Cập nhật chỉ một field (không cần đọc-ghi toàn bộ object)
HSET user:1001 age 29

# Tăng giá trị số trong hash
HINCRBY user:1001 loginCount 1

# Kiểm tra field tồn tại
HEXISTS user:1001 email
# → 1 (true)
```

**Ưu điểm của Hash so với String+JSON**:
- Cập nhật một field không cần đọc toàn bộ object
- Tiết kiệm memory hơn khi có nhiều objects nhỏ
- Có thể set TTL trên từng field (Redis 7.4+)

### 2.3 Lists - Danh sách có thứ tự

List trong Redis là linked list hai chiều, hỗ trợ push/pop từ cả hai đầu.

```bash
# Thêm vào đầu danh sách (LPUSH)
LPUSH recent_orders "order:1001"
LPUSH recent_orders "order:1002"
LPUSH recent_orders "order:1003"

# Danh sách hiện tại: [order:1003, order:1002, order:1001]

# Lấy range (0 đến 4 = 5 phần tử đầu)
LRANGE recent_orders 0 4

# Thêm vào cuối (RPUSH)
RPUSH notification_queue "notif:abc"
RPUSH notification_queue "notif:def"

# Pop từ bên trái (blocking pop - dùng cho queue processing)
BLPOP notification_queue 0

# Giới hạn độ dài list (giữ chỉ 100 phần tử mới nhất)
LTRIM recent_orders 0 99

# Độ dài list
LLEN recent_orders
```

**Use case**: Activity feed, notification queue, recent items, chat history.

### 2.4 Sets - Tập hợp không trùng lặp

Set là tập hợp các string không có thứ tự và không trùng lặp. Hỗ trợ các phép toán tập hợp (union, intersection, difference).

```bash
# Thêm tag vào sản phẩm
SADD product:1:tags "electronics" "laptop" "dell" "gaming"
SADD product:2:tags "electronics" "laptop" "apple"

# Kiểm tra membership (O(1))
SISMEMBER product:1:tags "gaming"
# → 1 (true)

# Lấy tất cả members
SMEMBERS product:1:tags
# → 1) "electronics"
# → 2) "laptop"
# → 3) "dell"
# → 4) "gaming"

# Intersection - tags chung của 2 sản phẩm
SINTER product:1:tags product:2:tags
# → 1) "electronics"
# → 2) "laptop"

# Union - tất cả tags
SUNION product:1:tags product:2:tags

# Difference
SDIFF product:1:tags product:2:tags
# → 1) "dell"
# → 2) "gaming"

# Số lượng members
SCARD product:1:tags
# → 4
```

**Use case**: Tags, permissions, unique visitors, friend lists, "who liked this post".

### 2.5 Sorted Sets - Tập hợp có điểm số

Sorted Set giống Set nhưng mỗi member có kèm một **score** (số thực). Dữ liệu được sắp xếp theo score tự động.

```bash
# Thêm sản phẩm vào leaderboard theo doanh số
ZADD top_products 1250000 "product:laptop-dell"
ZADD top_products 980000  "product:iphone-15"
ZADD top_products 2100000 "product:macbook-pro"
ZADD top_products 450000  "product:mouse-logitech"

# Lấy top 3 (thứ hạng cao nhất = score cao nhất)
ZREVRANGE top_products 0 2 WITHSCORES
# → 1) "product:macbook-pro"
# → 2) "2100000"
# → 3) "product:laptop-dell"
# → 4) "1250000"
# → 5) "product:iphone-15"
# → 6) "980000"

# Lấy rank của một sản phẩm (0-based, từ cao đến thấp)
ZREVRANK top_products "product:laptop-dell"
# → 1 (hạng 2)

# Lấy score
ZSCORE top_products "product:macbook-pro"
# → "2100000"

# Rate limiting: sliding window với sorted set
ZADD rate:user:1001 1704067200 "req:abc"  # timestamp làm score
ZREMRANGEBYSCORE rate:user:1001 0 1704067140  # Xóa request cũ hơn 60s
ZCARD rate:user:1001  # Đếm request trong 60s gần đây
```

**Use case**: Leaderboard, rate limiting (sliding window), priority queue, top-N queries.

### 2.6 Pub/Sub - Message Broadcasting

Redis Pub/Sub cho phép publish message đến nhiều subscriber đồng thời.

```bash
# Terminal 1 - Subscriber
SUBSCRIBE cache:invalidation

# Terminal 2 - Publisher (khi có data thay đổi)
PUBLISH cache:invalidation '{"type":"product","id":1,"action":"update"}'

# Terminal 1 sẽ nhận được:
# 1) "message"
# 2) "cache:invalidation"
# 3) '{"type":"product","id":1,"action":"update"}'
```

---

## 3. StackExchange.Redis trong .NET

### 3.1 Cài đặt và cấu hình

```bash
# Cài đặt package
dotnet add package StackExchange.Redis
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

### 3.2 ConnectionMultiplexer - Kết nối trung tâm

`ConnectionMultiplexer` là trái tim của StackExchange.Redis. Nó quản lý connection pool và nên được đăng ký như **singleton**.

```csharp
// RedisConnectionFactory.cs
using StackExchange.Redis;

namespace MyApp.Infrastructure.Cache;

public class RedisConnectionFactory : IDisposable
{
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private bool _disposed;

    public RedisConnectionFactory(IConfiguration configuration)
    {
        var options = ConfigureOptions(configuration);
        _connection = new Lazy<ConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(options));
    }

    private ConfigurationOptions ConfigureOptions(IConfiguration configuration)
    {
        var redisConfig = configuration.GetSection("Redis");
        
        return new ConfigurationOptions
        {
            // Hỗ trợ nhiều endpoint (Redis Cluster hoặc Sentinel)
            EndPoints =
            {
                { redisConfig["Host"] ?? "localhost", int.Parse(redisConfig["Port"] ?? "6379") }
            },
            Password = redisConfig["Password"],
            
            // Timeout settings
            ConnectTimeout = 5000,       // 5 giây để connect
            SyncTimeout = 3000,          // 3 giây cho synchronous operations
            AsyncTimeout = 3000,         // 3 giây cho async operations
            
            // Retry policy
            ConnectRetry = 3,            // Thử lại 3 lần khi connect thất bại
            ReconnectRetryPolicy = new ExponentialRetry(
                deltaBackOffMilliseconds: 5000,
                maxDeltaBackOffMilliseconds: 30000),
            
            // Resilience
            AbortOnConnectFail = false,  // Không throw exception khi connect fail
            KeepAlive = 60,              // Gửi keepalive mỗi 60 giây
            
            // Database (0-15, mặc định 0)
            DefaultDatabase = int.Parse(redisConfig["Database"] ?? "0"),
            
            // SSL (bắt buộc cho production)
            Ssl = bool.Parse(redisConfig["UseSsl"] ?? "false"),
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
            
            // Logging
            AllowAdmin = false, // Không cho phép FLUSHDB, CONFIG... trong production
        };
    }

    public ConnectionMultiplexer GetConnection() => _connection.Value;
    
    public IDatabase GetDatabase() => _connection.Value.GetDatabase();
    
    public ISubscriber GetSubscriber() => _connection.Value.GetSubscriber();

    public void Dispose()
    {
        if (!_disposed && _connection.IsValueCreated)
        {
            _connection.Value.Dispose();
            _disposed = true;
        }
    }
}
```

### 3.3 Đăng ký dependency injection

```csharp
// Program.cs hoặc ServiceCollectionExtensions.cs
using StackExchange.Redis;

namespace MyApp.Infrastructure.Cache;

public static class CacheServiceExtensions
{
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Đăng ký ConnectionMultiplexer như singleton
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisConfig = configuration.GetSection("Redis");
            var options = ConfigurationOptions.Parse(
                $"{redisConfig["Host"]}:{redisConfig["Port"]}");
            
            options.Password = redisConfig["Password"];
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            
            return ConnectionMultiplexer.Connect(options);
        });

        // Đăng ký IDistributedCache với Redis
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "myapp:"; // Prefix cho tất cả keys
        });

        // Đăng ký cache service tùy chỉnh
        services.AddSingleton<IRedisCacheService, RedisCacheService>();
        services.AddSingleton<RedisConnectionFactory>();

        return services;
    }
}
```

### 3.4 IDatabase - Thực hiện các lệnh Redis

```csharp
// RedisCacheService.cs
using System.Text.Json;
using StackExchange.Redis;

namespace MyApp.Infrastructure.Cache;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, long value = 1, CancellationToken ct = default);
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default);
    Task PublishAsync(string channel, string message, CancellationToken ct = default);
    Task SubscribeAsync(string channel, Action<string> handler);
}

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;
    
    // Options cho JSON serialization
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisCacheService(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisCacheService> logger)
    {
        _multiplexer = multiplexer;
        _db = multiplexer.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// Lấy giá trị từ cache, tự động deserialize từ JSON
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            
            if (!value.HasValue)
            {
                _logger.LogDebug("Cache MISS: {Key}", key);
                return default;
            }

            _logger.LogDebug("Cache HIT: {Key}", key);
            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (RedisException ex)
        {
            // Không để Redis failure làm sập ứng dụng
            _logger.LogError(ex, "Redis error getting key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Lưu giá trị vào cache với TTL tùy chọn
    /// </summary>
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value, JsonOptions);
            await _db.StringSetAsync(key, serialized, expiry);
            _logger.LogDebug("Cache SET: {Key}, TTL: {Expiry}", key, expiry);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error setting key: {Key}", key);
            // Không throw - cache failure không nên block business logic
        }
    }

    /// <summary>
    /// Xóa một key
    /// </summary>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _db.KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error deleting key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error checking key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Atomic increment - dùng cho counter và rate limiting
    /// </summary>
    public async Task<long> IncrementAsync(string key, long value = 1, CancellationToken ct = default)
    {
        try
        {
            return await _db.StringIncrementAsync(key, value);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error incrementing key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// SET key value NX EX seconds - Atomic set-if-not-exists (dùng cho distributed lock)
    /// </summary>
    public async Task<bool> SetIfNotExistsAsync(
        string key,
        string value,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        try
        {
            return await _db.StringSetAsync(key, value, expiry, When.NotExists);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error setting NX key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Publish message đến channel (dùng cho cache invalidation)
    /// </summary>
    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        try
        {
            var subscriber = _multiplexer.GetSubscriber();
            await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error publishing to channel: {Channel}", channel);
        }
    }

    /// <summary>
    /// Subscribe nhận message từ channel
    /// </summary>
    public async Task SubscribeAsync(string channel, Action<string> handler)
    {
        var subscriber = _multiplexer.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(channel),
            (_, message) =>
            {
                if (message.HasValue)
                    handler(message!);
            });
    }
}
```

---

## 4. IDistributedCache Abstraction

`IDistributedCache` là interface chuẩn của .NET cho distributed cache. Ưu điểm là dễ dàng swap implementation (Redis, SQL Server, NCache) mà không cần thay đổi business code.

```csharp
// ProductCacheRepository.cs - Dùng IDistributedCache (abstraction)
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace MyApp.Infrastructure.Cache;

public class ProductCacheRepository
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductCacheRepository> _logger;

    // Cache key prefix để tránh conflict
    private const string KeyPrefix = "product:";
    
    // Default TTL
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public ProductCacheRepository(
        IDistributedCache cache,
        ILogger<ProductCacheRepository> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{id}";
        
        // IDistributedCache trả về byte[]
        var bytes = await _cache.GetAsync(key, ct);
        
        if (bytes is null)
        {
            _logger.LogDebug("Cache MISS for product {Id}", id);
            return null;
        }

        _logger.LogDebug("Cache HIT for product {Id}", id);
        return JsonSerializer.Deserialize<ProductDto>(bytes);
    }

    public async Task SetProductAsync(ProductDto product, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{product.Id}";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(product);
        
        var options = new DistributedCacheEntryOptions
        {
            // Absolute expiration: key xóa sau đúng 1 giờ
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            
            // Sliding expiration: key được gia hạn mỗi khi được truy cập
            // Nếu không ai truy cập trong 15 phút, key bị xóa
            SlidingExpiration = TimeSpan.FromMinutes(15)
            
            // Khi cả hai được đặt: key xóa khi một trong hai hết hạn
        };
        
        await _cache.SetAsync(key, bytes, options, ct);
    }

    public async Task InvalidateProductAsync(int id, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{id}";
        await _cache.RemoveAsync(key, ct);
        _logger.LogInformation("Cache invalidated for product {Id}", id);
    }
}
```

### 4.1 Extension Methods cho IDistributedCache

```csharp
// DistributedCacheExtensions.cs
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace MyApp.Infrastructure.Cache;

public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Get-or-set pattern: lấy từ cache, nếu miss thì gọi factory function
    /// </summary>
    public static async Task<T> GetOrSetAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<CancellationToken, Task<T>> factory,
        DistributedCacheEntryOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        // Bước 1: Thử lấy từ cache
        var bytes = await cache.GetAsync(key, ct);
        
        if (bytes is not null)
        {
            return JsonSerializer.Deserialize<T>(bytes, DefaultOptions)!;
        }

        // Bước 2: Cache MISS - gọi factory function
        var value = await factory(ct);
        
        // Bước 3: Lưu vào cache
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);
        await cache.SetAsync(key, serialized, options ?? DefaultOptions(), ct);
        
        return value;
    }

    /// <summary>
    /// Get và deserialize từ JSON
    /// </summary>
    public static async Task<T?> GetObjectAsync<T>(
        this IDistributedCache cache,
        string key,
        CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null
            ? default
            : JsonSerializer.Deserialize<T>(bytes, DefaultOptions);
    }

    /// <summary>
    /// Serialize và set vào cache
    /// </summary>
    public static async Task SetObjectAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        DistributedCacheEntryOptions? options = null,
        CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);
        await cache.SetAsync(key, bytes, options ?? DefaultOptions(), ct);
    }

    private static DistributedCacheEntryOptions DefaultOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };
}
```

---

## 5. Cache Patterns

### 5.1 Cache-Aside Pattern (Lazy Loading)

Đây là pattern phổ biến nhất. Application tự kiểm soát cache: kiểm tra cache trước, nếu miss thì load từ DB và lưu vào cache.

```
Cache-Aside Flow:

┌─────────┐          ┌─────────────┐          ┌────────────┐
│  Client │          │    Redis    │          │  Database  │
└────┬────┘          └──────┬──────┘          └─────┬──────┘
     │                      │                       │
     │  1. GET product:1    │                       │
     │─────────────────────▶│                       │
     │                      │                       │
     │  2. (nil) - MISS     │                       │
     │◀─────────────────────│                       │
     │                      │                       │
     │  3. SELECT * FROM products WHERE id=1        │
     │──────────────────────────────────────────────▶
     │                      │                       │
     │  4. {id:1, name:...} │                       │
     │◀──────────────────────────────────────────────
     │                      │                       │
     │  5. SET product:1 {...} EX 3600              │
     │─────────────────────▶│                       │
     │                      │                       │
     │  6. OK               │                       │
     │◀─────────────────────│                       │
     │                      │                       │
     │  (Lần sau sẽ là HIT) │                       │
```

```csharp
// CacheAsideService.cs
public class ProductService
{
    private readonly IProductRepository _repository;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<ProductService> _logger;

    private static string ProductKey(int id) => $"product:{id}";
    private static readonly TimeSpan ProductTtl = TimeSpan.FromHours(1);

    public ProductService(
        IProductRepository repository,
        IRedisCacheService cache,
        ILogger<ProductService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Cache-Aside: Get product với caching
    /// </summary>
    public async Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = ProductKey(id);
        
        // Bước 1: Kiểm tra cache
        var cached = await _cache.GetAsync<ProductDto>(key, ct);
        if (cached is not null)
            return cached;

        // Bước 2: Cache MISS - load từ database
        var product = await _repository.GetByIdAsync(id, ct);
        if (product is null)
            return null;

        // Bước 3: Lưu vào cache
        await _cache.SetAsync(key, product, ProductTtl, ct);
        
        return product;
    }

    /// <summary>
    /// Cập nhật product và invalidate cache
    /// </summary>
    public async Task<ProductDto> UpdateProductAsync(
        int id, 
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        // Cập nhật database trước
        var updated = await _repository.UpdateAsync(id, request, ct);
        
        // Invalidate cache để lần sau load lại từ DB
        await _cache.DeleteAsync(ProductKey(id), ct);
        
        _logger.LogInformation("Product {Id} updated, cache invalidated", id);
        return updated;
    }
}
```

### 5.2 Write-Through Pattern

Khi ghi dữ liệu, đồng thời cập nhật cả cache. Đảm bảo cache luôn có data mới nhất nhưng tốn thêm latency khi write.

```
Write-Through Flow:

┌─────────┐     ┌────────────────┐     ┌─────────────┐     ┌──────────┐
│  Client │     │   App Service  │     │    Redis    │     │ Database │
└────┬────┘     └───────┬────────┘     └──────┬──────┘     └────┬─────┘
     │                  │                     │                  │
     │  POST /products  │                     │                  │
     │─────────────────▶│                     │                  │
     │                  │                     │                  │
     │                  │  INSERT INTO...      │                  │
     │                  │──────────────────────────────────────▶│
     │                  │                     │                  │
     │                  │  product created     │                  │
     │                  │◀──────────────────────────────────────│
     │                  │                     │                  │
     │                  │  SET product:123 EX │                  │
     │                  │────────────────────▶│                  │
     │                  │                     │                  │
     │                  │  OK                 │                  │
     │                  │◀────────────────────│                  │
     │                  │                     │                  │
     │  201 Created     │                     │                  │
     │◀─────────────────│                     │                  │
```

```csharp
// WriteThroughService.cs
public class WriteThroughProductService
{
    private readonly IProductRepository _repository;
    private readonly IRedisCacheService _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

    public WriteThroughProductService(
        IProductRepository repository,
        IRedisCacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ProductDto> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken ct = default)
    {
        // Bước 1: Lưu vào database
        var product = await _repository.CreateAsync(request, ct);
        
        // Bước 2: Đồng thời cache lại ngay (Write-Through)
        var key = $"product:{product.Id}";
        await _cache.SetAsync(key, product, CacheTtl, ct);
        
        return product;
    }

    public async Task<ProductDto> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        // Bước 1: Cập nhật database
        var updated = await _repository.UpdateAsync(id, request, ct);
        
        // Bước 2: Cập nhật cache với data mới nhất
        var key = $"product:{updated.Id}";
        await _cache.SetAsync(key, updated, CacheTtl, ct);
        
        return updated;
    }
}
```

### 5.3 Write-Behind (Write-Back) Pattern

Ghi vào cache trước, sau đó **bất đồng bộ** ghi vào database. Tốc độ ghi nhanh nhất nhưng có rủi ro mất data nếu cache crash.

```
Write-Behind Flow:

┌─────────┐     ┌────────────┐     ┌─────────┐     ┌──────────┐
│  Client │     │   Service  │     │  Redis  │     │ Database │
└────┬────┘     └──────┬─────┘     └────┬────┘     └────┬─────┘
     │                 │                │               │
     │  POST /products │                │               │
     │────────────────▶│                │               │
     │                 │  SET product   │               │
     │                 │───────────────▶│               │
     │                 │  (immediate)   │               │
     │  201 Created    │                │               │
     │◀────────────────│                │               │
     │                 │                │               │
     │                 │   (Async background job)       │
     │                 │                │  INSERT INTO  │
     │                 │                │──────────────▶│
     │                 │                │               │
     │                 │                │  OK           │
     │                 │                │◀──────────────│
```

```csharp
// WriteBehindService.cs
using System.Threading.Channels;

public class WriteBehindProductService : IHostedService
{
    private readonly IRedisCacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WriteBehindProductService> _logger;
    
    // Channel để buffering write operations
    private readonly Channel<WriteOperation> _writeChannel;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    public WriteBehindProductService(
        IRedisCacheService cache,
        IServiceScopeFactory scopeFactory,
        ILogger<WriteBehindProductService> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _writeChannel = Channel.CreateBounded<WriteOperation>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <summary>
    /// Ghi vào cache ngay lập tức, enqueue để ghi DB sau
    /// </summary>
    public async Task WriteAsync(ProductDto product, CancellationToken ct = default)
    {
        // 1. Ghi vào cache ngay (fast!)
        await _cache.SetAsync($"product:{product.Id}", product, TimeSpan.FromHours(1), ct);
        
        // 2. Enqueue để ghi vào DB bất đồng bộ
        await _writeChannel.Writer.WriteAsync(new WriteOperation
        {
            ProductId = product.Id,
            Product = product,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);
    }

    // IHostedService - chạy background worker
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessWritesAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _writeChannel.Writer.Complete();
        if (_processingTask is not null)
            await _processingTask;
    }

    private async Task ProcessWritesAsync(CancellationToken ct)
    {
        await foreach (var operation in _writeChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
                await repository.UpsertAsync(operation.Product, ct);
                _logger.LogDebug("Write-behind: persisted product {Id}", operation.ProductId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Write-behind failed for product {Id}", operation.ProductId);
                // Implement retry logic hoặc dead letter queue ở đây
            }
        }
    }

    private record WriteOperation
    {
        public int ProductId { get; init; }
        public ProductDto Product { get; init; } = null!;
        public DateTimeOffset Timestamp { get; init; }
    }
}
```

---

## 6. Cache Invalidation Strategies

> *"There are only two hard things in Computer Science: cache invalidation and naming things."* — Phil Karlton

### 6.1 TTL-based Expiration

Đơn giản nhất: cache tự động hết hạn sau một khoảng thời gian.

```csharp
// TTLStrategy.cs
public class TtlCacheStrategy
{
    private readonly IRedisCacheService _cache;

    // Các TTL khác nhau cho các loại data khác nhau
    private static class Ttl
    {
        public static readonly TimeSpan ProductDetail = TimeSpan.FromHours(1);
        public static readonly TimeSpan ProductList = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan UserProfile = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan Configuration = TimeSpan.FromHours(24);
        public static readonly TimeSpan SearchResults = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HotData = TimeSpan.FromSeconds(30);
    }

    public TtlCacheStrategy(IRedisCacheService cache) => _cache = cache;

    public async Task CacheProductAsync(ProductDto product)
    {
        // Data ít thay đổi → TTL dài hơn
        await _cache.SetAsync($"product:{product.Id}", product, Ttl.ProductDetail);
    }

    public async Task CacheSearchResultsAsync(string query, List<ProductDto> results)
    {
        // Search results có thể thay đổi thường xuyên → TTL ngắn
        var key = $"search:{HashQuery(query)}";
        await _cache.SetAsync(key, results, Ttl.SearchResults);
    }

    private static string HashQuery(string query)
    {
        // Hash để tránh key quá dài
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(query.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }
}
```

### 6.2 Event-based Invalidation với Pub/Sub

Khi data thay đổi, publish event để các service invalidate cache liên quan.

```
Event-based Invalidation:

┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│ Product Svc  │         │  Redis       │         │  Other Svc   │
│ (Publisher)  │         │  (Broker)    │         │ (Subscriber) │
└──────┬───────┘         └──────┬───────┘         └──────┬───────┘
       │                        │                        │
       │ Product updated in DB  │                        │
       │                        │                        │
       │ PUBLISH cache:invalidation                      │
       │ {"type":"product","id":1}                       │
       │───────────────────────▶│                        │
       │                        │                        │
       │                        │  Message delivered     │
       │                        │───────────────────────▶│
       │                        │                        │
       │                        │  DEL product:1         │
       │                        │  (Local cache clear)   │
       │                        │                        │
```

```csharp
// EventBasedInvalidation.cs
using System.Text.Json;

namespace MyApp.Infrastructure.Cache;

public record CacheInvalidationMessage
{
    public string Type { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty; // "update", "delete", "create"
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class CacheInvalidationPublisher
{
    private readonly IRedisCacheService _cache;
    private const string InvalidationChannel = "cache:invalidation";

    public CacheInvalidationPublisher(IRedisCacheService cache) => _cache = cache;

    public async Task InvalidateProductAsync(int productId, string action = "update")
    {
        var message = new CacheInvalidationMessage
        {
            Type = "product",
            Id = productId.ToString(),
            Action = action
        };
        
        // Publish event đến tất cả subscriber
        await _cache.PublishAsync(
            InvalidationChannel, 
            JsonSerializer.Serialize(message));
    }

    public async Task InvalidateCategoryAsync(int categoryId)
    {
        var message = new CacheInvalidationMessage
        {
            Type = "category",
            Id = categoryId.ToString(),
            Action = "update"
        };
        await _cache.PublishAsync(InvalidationChannel, JsonSerializer.Serialize(message));
    }
}

public class CacheInvalidationSubscriber : IHostedService
{
    private readonly IRedisCacheService _cache;
    private readonly ILogger<CacheInvalidationSubscriber> _logger;
    private const string InvalidationChannel = "cache:invalidation";

    public CacheInvalidationSubscriber(
        IRedisCacheService cache,
        ILogger<CacheInvalidationSubscriber> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe và xử lý message invalidation
        await _cache.SubscribeAsync(InvalidationChannel, HandleInvalidationMessage);
        _logger.LogInformation("Cache invalidation subscriber started");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void HandleInvalidationMessage(string messageJson)
    {
        try
        {
            var message = JsonSerializer.Deserialize<CacheInvalidationMessage>(messageJson);
            if (message is null) return;

            _logger.LogDebug(
                "Received invalidation: {Type}:{Id} ({Action})", 
                message.Type, message.Id, message.Action);

            // Xử lý bất đồng bộ nhưng không block thread
            _ = HandleAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling invalidation message: {Message}", messageJson);
        }
    }

    private async Task HandleAsync(CacheInvalidationMessage message)
    {
        switch (message.Type.ToLower())
        {
            case "product":
                await _cache.DeleteAsync($"product:{message.Id}");
                // Xóa cả các related caches
                await _cache.DeleteAsync($"product:{message.Id}:details");
                break;

            case "category":
                // Xóa tất cả product caches trong category này
                await InvalidateCategoryProductsAsync(message.Id);
                break;
        }
    }

    private async Task InvalidateCategoryProductsAsync(string categoryId)
    {
        // Lấy danh sách product IDs trong category từ cache
        var key = $"category:{categoryId}:products";
        var productIds = await _cache.GetAsync<List<int>>(key);
        
        if (productIds is not null)
        {
            var deleteTasks = productIds.Select(id => _cache.DeleteAsync($"product:{id}"));
            await Task.WhenAll(deleteTasks);
        }
        
        await _cache.DeleteAsync(key);
    }
}
```

### 6.3 Tag-based Invalidation

Nhóm các cache keys theo "tag", khi invalidate một tag thì xóa tất cả keys liên quan.

```csharp
// TagBasedCacheService.cs
using StackExchange.Redis;

namespace MyApp.Infrastructure.Cache;

/// <summary>
/// Tag-based cache: nhóm keys theo tag để invalidate theo nhóm
/// 
/// Cách hoạt động:
/// - Khi SET key, đồng thời thêm key vào các Set của từng tag
/// - Khi INVALIDATE tag, lấy tất cả keys trong tag's Set và DELETE
/// 
/// Ví dụ:
///   SET product:1 ...          → tag-set:product:{1,2,3,...}
///   SADD tag:category:5 "product:1"
///   SADD tag:brand:dell "product:1"
///   
///   Khi category 5 thay đổi:
///   SMEMBERS tag:category:5    → ["product:1", "product:3", ...]
///   DEL product:1 product:3 ...
/// </summary>
public class TagBasedCacheService
{
    private readonly IDatabase _db;
    private readonly ILogger<TagBasedCacheService> _logger;

    public TagBasedCacheService(
        IConnectionMultiplexer multiplexer,
        ILogger<TagBasedCacheService> logger)
    {
        _db = multiplexer.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// Lưu cache với tags. Sử dụng Redis Pipeline để tối ưu round trips
    /// </summary>
    public async Task SetWithTagsAsync<T>(
        string key,
        T value,
        string[] tags,
        TimeSpan expiry)
    {
        var serialized = System.Text.Json.JsonSerializer.Serialize(value);
        
        // Dùng pipeline để gom nhiều lệnh thành 1 round trip
        var batch = _db.CreateBatch();
        
        var tasks = new List<Task>
        {
            // Set giá trị chính
            batch.StringSetAsync(key, serialized, expiry)
        };

        // Thêm key vào mỗi tag set
        foreach (var tag in tags)
        {
            var tagKey = $"tag:{tag}";
            tasks.Add(batch.SetAddAsync(tagKey, key));
            // Tag set cũng cần TTL (đặt lâu hơn data TTL)
            tasks.Add(batch.KeyExpireAsync(tagKey, expiry * 2));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("Cached {Key} with tags: {Tags}", key, string.Join(", ", tags));
    }

    /// <summary>
    /// Invalidate tất cả cache keys có chứa tag này
    /// </summary>
    public async Task InvalidateByTagAsync(string tag)
    {
        var tagKey = $"tag:{tag}";
        
        // Lấy tất cả keys thuộc tag này
        var members = await _db.SetMembersAsync(tagKey);
        
        if (members.Length == 0)
        {
            _logger.LogDebug("No cached keys found for tag: {Tag}", tag);
            return;
        }

        // Xóa tất cả keys (dùng pipeline)
        var keysToDelete = members.Select(m => (RedisKey)m.ToString()).ToArray();
        
        var batch = _db.CreateBatch();
        var deleteTasks = keysToDelete.Select(k => batch.KeyDeleteAsync(k)).ToList();
        deleteTasks.Add(batch.KeyDeleteAsync(tagKey)); // Xóa cả tag set
        
        batch.Execute();
        await Task.WhenAll(deleteTasks);
        
        _logger.LogInformation(
            "Invalidated {Count} cache keys for tag: {Tag}", 
            members.Length, tag);
    }
}
```

---

## 7. ProductService hoàn chỉnh với Redis Caching

Đây là ví dụ thực tế production-ready kết hợp tất cả các pattern đã học.

### 7.1 Domain Models và DTOs

```csharp
// Models/Product.cs
namespace MyApp.Domain;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int CategoryId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public record ProductDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string BrandName,
    List<string> Tags,
    bool IsActive
);

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string BrandName,
    List<string> Tags
);

public record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Price,
    int? Stock,
    bool? IsActive
);

public record ProductSearchRequest(
    string? Query,
    int? CategoryId,
    string? BrandName,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page = 1,
    int PageSize = 20
);

public record PagedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
```

### 7.2 Repository Interface

```csharp
// Repositories/IProductRepository.cs
namespace MyApp.Infrastructure.Repositories;

public interface IProductRepository
{
    Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<ProductDto>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<PagedResult<ProductDto>> SearchAsync(ProductSearchRequest request, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task UpsertAsync(ProductDto product, CancellationToken ct = default);
}
```

### 7.3 Cache Keys Convention

```csharp
// Cache/CacheKeys.cs
namespace MyApp.Infrastructure.Cache;

/// <summary>
/// Tập trung quản lý tất cả cache keys để dễ maintain
/// </summary>
public static class CacheKeys
{
    // Single product
    public static string Product(int id) => $"product:{id}";
    
    // Product list by category
    public static string ProductsByCategory(int categoryId) => $"products:category:{categoryId}";
    
    // Search results (hash của query parameters)
    public static string SearchResults(string queryHash) => $"search:{queryHash}";
    
    // Tags for tag-based invalidation
    public static string TagForProduct(int productId) => $"product:{productId}";
    public static string TagForCategory(int categoryId) => $"category:{categoryId}";
    public static string TagForBrand(string brand) => $"brand:{brand.ToLower()}";
    
    // Session
    public static string UserSession(string sessionId) => $"session:{sessionId}";
    
    // Rate limiting
    public static string RateLimit(string identifier, string endpoint) 
        => $"rl:{endpoint}:{identifier}";
}
```

### 7.4 ProductCacheService - Toàn bộ caching logic

```csharp
// Cache/ProductCacheService.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyApp.Infrastructure.Cache;

public interface IProductCacheService
{
    Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default);
    Task SetProductAsync(ProductDto product, CancellationToken ct = default);
    Task InvalidateProductAsync(int id, CancellationToken ct = default);
    
    Task<PagedResult<ProductDto>?> GetSearchResultsAsync(
        ProductSearchRequest request, CancellationToken ct = default);
    Task SetSearchResultsAsync(
        ProductSearchRequest request, PagedResult<ProductDto> results, CancellationToken ct = default);
    Task InvalidateSearchResultsForCategoryAsync(int categoryId, CancellationToken ct = default);
}

public class ProductCacheService : IProductCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly TagBasedCacheService _tagCache;
    private readonly CacheInvalidationPublisher _publisher;
    private readonly ILogger<ProductCacheService> _logger;

    // TTL constants
    private static class Ttl
    {
        public static readonly TimeSpan Product = TimeSpan.FromHours(2);
        public static readonly TimeSpan SearchResults = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan CategoryProducts = TimeSpan.FromMinutes(30);
    }

    public ProductCacheService(
        IRedisCacheService cache,
        TagBasedCacheService tagCache,
        CacheInvalidationPublisher publisher,
        ILogger<ProductCacheService> logger)
    {
        _cache = cache;
        _tagCache = tagCache;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = CacheKeys.Product(id);
        return await _cache.GetAsync<ProductDto>(key, ct);
    }

    public async Task SetProductAsync(ProductDto product, CancellationToken ct = default)
    {
        var key = CacheKeys.Product(product.Id);
        
        // Cache với tags để dễ invalidate theo category hoặc brand
        var tags = new[]
        {
            CacheKeys.TagForProduct(product.Id),
            CacheKeys.TagForCategory(product.CategoryId),
            CacheKeys.TagForBrand(product.BrandName)
        };
        
        await _tagCache.SetWithTagsAsync(key, product, tags, Ttl.Product);
    }

    public async Task InvalidateProductAsync(int id, CancellationToken ct = default)
    {
        // Invalidate cache trực tiếp
        await _cache.DeleteAsync(CacheKeys.Product(id), ct);
        
        // Publish event để các service khác cũng invalidate
        await _publisher.InvalidateProductAsync(id, "delete");
        
        _logger.LogInformation("Product {Id} cache invalidated", id);
    }

    public async Task<PagedResult<ProductDto>?> GetSearchResultsAsync(
        ProductSearchRequest request,
        CancellationToken ct = default)
    {
        var key = CacheKeys.SearchResults(HashRequest(request));
        return await _cache.GetAsync<PagedResult<ProductDto>>(key, ct);
    }

    public async Task SetSearchResultsAsync(
        ProductSearchRequest request,
        PagedResult<ProductDto> results,
        CancellationToken ct = default)
    {
        var key = CacheKeys.SearchResults(HashRequest(request));
        
        // Tag search results theo category nếu có filter category
        var tags = new List<string>();
        if (request.CategoryId.HasValue)
            tags.Add(CacheKeys.TagForCategory(request.CategoryId.Value));
        if (!string.IsNullOrEmpty(request.BrandName))
            tags.Add(CacheKeys.TagForBrand(request.BrandName));

        if (tags.Count > 0)
            await _tagCache.SetWithTagsAsync(key, results, [.. tags], Ttl.SearchResults);
        else
            await _cache.SetAsync(key, results, Ttl.SearchResults, ct);
    }

    public async Task InvalidateSearchResultsForCategoryAsync(
        int categoryId,
        CancellationToken ct = default)
    {
        // Dùng tag-based invalidation để xóa tất cả search results liên quan
        await _tagCache.InvalidateByTagAsync(CacheKeys.TagForCategory(categoryId));
    }

    private static string HashRequest(ProductSearchRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16];
    }
}
```

### 7.5 ProductService - Business Logic với Caching

```csharp
// Services/ProductService.cs
namespace MyApp.Application.Services;

public interface IProductService
{
    Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<ProductDto>> SearchAsync(ProductSearchRequest request, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class ProductService : IProductService
{
    private readonly IProductRepository _repository;
    private readonly IProductCacheService _cacheService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository repository,
        IProductCacheService cacheService,
        ILogger<ProductService> logger)
    {
        _repository = repository;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Cache-aside pattern cho single product
    /// </summary>
    public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        // 1. Check cache
        var cached = await _cacheService.GetProductAsync(id, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Product {Id} served from cache", id);
            return cached;
        }

        // 2. Cache MISS → load from DB
        _logger.LogDebug("Product {Id} cache miss, loading from DB", id);
        var product = await _repository.GetByIdAsync(id, ct);
        
        if (product is null)
            return null;

        // 3. Cache for future requests
        await _cacheService.SetProductAsync(product, ct);
        return product;
    }

    /// <summary>
    /// Cache-aside pattern cho search results
    /// </summary>
    public async Task<PagedResult<ProductDto>> SearchAsync(
        ProductSearchRequest request,
        CancellationToken ct = default)
    {
        // 1. Check cache
        var cached = await _cacheService.GetSearchResultsAsync(request, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Search results served from cache");
            return cached;
        }

        // 2. Cache MISS → query DB
        _logger.LogDebug("Search cache miss, querying DB");
        var results = await _repository.SearchAsync(request, ct);
        
        // 3. Cache results (không cache empty results để tránh negative caching issues)
        if (results.TotalCount > 0)
            await _cacheService.SetSearchResultsAsync(request, results, ct);
        
        return results;
    }

    /// <summary>
    /// Write-through: tạo product và cache ngay
    /// </summary>
    public async Task<ProductDto> CreateAsync(
        CreateProductRequest request,
        CancellationToken ct = default)
    {
        // 1. Insert vào DB
        var product = await _repository.CreateAsync(request, ct);
        
        // 2. Cache ngay (Write-Through)
        await _cacheService.SetProductAsync(product, ct);
        
        // 3. Invalidate category cache vì có product mới
        await _cacheService.InvalidateSearchResultsForCategoryAsync(product.CategoryId, ct);
        
        _logger.LogInformation("Product {Id} created and cached", product.Id);
        return product;
    }

    /// <summary>
    /// Update product: cập nhật DB, sau đó update cache
    /// </summary>
    public async Task<ProductDto> UpdateAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        // 1. Update trong DB
        var updated = await _repository.UpdateAsync(id, request, ct);
        
        // 2. Update cache với data mới (Write-Through)
        await _cacheService.SetProductAsync(updated, ct);
        
        // 3. Invalidate search results liên quan
        await _cacheService.InvalidateSearchResultsForCategoryAsync(updated.CategoryId, ct);
        
        _logger.LogInformation("Product {Id} updated, cache refreshed", id);
        return updated;
    }

    /// <summary>
    /// Delete product: xóa DB và invalidate cache
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        // Lấy product info trước khi xóa (cần categoryId để invalidate search cache)
        var product = await GetByIdAsync(id, ct);
        
        // 1. Xóa khỏi DB
        await _repository.DeleteAsync(id, ct);
        
        // 2. Invalidate cache
        await _cacheService.InvalidateProductAsync(id, ct);
        
        // 3. Invalidate search results liên quan
        if (product is not null)
            await _cacheService.InvalidateSearchResultsForCategoryAsync(product.CategoryId, ct);
        
        _logger.LogInformation("Product {Id} deleted, cache invalidated", id);
    }
}
```

### 7.6 API Controller

```csharp
// Controllers/ProductsController.cs
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var product = await _productService.GetByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), 200)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] int? categoryId,
        [FromQuery] string? brand,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var request = new ProductSearchRequest(
            query, categoryId, brand, minPrice, maxPrice, page, pageSize);
        
        var results = await _productService.SearchAsync(request, ct);
        return Ok(results);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), 201)]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken ct)
    {
        var product = await _productService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProductRequest request,
        CancellationToken ct)
    {
        var updated = await _productService.UpdateAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _productService.DeleteAsync(id, ct);
        return NoContent();
    }
}
```

### 7.7 Dependency Injection Setup hoàn chỉnh

```csharp
// Program.cs
using MyApp.Infrastructure.Cache;
using MyApp.Application.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Redis Connection
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(config);
});

// 2. IDistributedCache với Redis backend
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "myapp:";
});

// 3. Cache services
builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();
builder.Services.AddSingleton<TagBasedCacheService>();
builder.Services.AddSingleton<CacheInvalidationPublisher>();
builder.Services.AddSingleton<IProductCacheService, ProductCacheService>();

// 4. Background services
builder.Services.AddHostedService<CacheInvalidationSubscriber>();

// 5. Business services
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();

// 6. Session với Redis
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseSession();
app.MapControllers();
app.Run();
```

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,password=secret,ssl=false,abortConnect=false",
    "DefaultConnection": "Server=localhost;Database=MyApp;..."
  },
  "Redis": {
    "Host": "localhost",
    "Port": "6379",
    "Password": "secret",
    "Database": "0",
    "UseSsl": "false"
  }
}
```

---

## 8. Session Caching với Redis

Trong ứng dụng nhiều instance, session phải được lưu trên distributed cache để bất kỳ instance nào cũng có thể phục vụ request của user.

```
Session Flow với Redis:

┌──────────┐    ┌──────────┐    ┌──────────┐
│ Server 1 │    │ Server 2 │    │ Server 3 │
└────┬─────┘    └────┬─────┘    └────┬─────┘
     │               │               │
     └───────────────┼───────────────┘
                     │  Tất cả đọc/ghi session vào Redis
                     ▼
            ┌─────────────────┐
            │  Redis Session  │
            │                 │
            │ session:abc123  │
            │ session:def456  │
            └─────────────────┘
```

```csharp
// Session/UserSessionService.cs
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace MyApp.Infrastructure.Session;

public record UserSession
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = [];
    public Dictionary<string, string> Claims { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivity { get; init; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; init; }
}

public interface IUserSessionService
{
    Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<string> CreateSessionAsync(UserSession session, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionId, CancellationToken ct = default);
    Task DestroySessionAsync(string sessionId, CancellationToken ct = default);
    Task DestroyAllUserSessionsAsync(string userId, CancellationToken ct = default);
}

public class RedisUserSessionService : IUserSessionService
{
    private readonly IRedisCacheService _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisUserSessionService> _logger;
    
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbsoluteSessionTtl = TimeSpan.FromHours(8);

    public RedisUserSessionService(
        IRedisCacheService cache,
        IConnectionMultiplexer multiplexer,
        ILogger<RedisUserSessionService> logger)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task<UserSession?> GetSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var key = CacheKeys.UserSession(sessionId);
        var session = await _cache.GetAsync<UserSession>(key, ct);
        
        if (session is not null)
        {
            // Sliding expiration: gia hạn TTL mỗi khi session được truy cập
            await _cache.SetAsync(key, session, SessionTtl, ct);
        }
        
        return session;
    }

    public async Task<string> CreateSessionAsync(
        UserSession session,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N"); // 32 ký tự hex
        var key = CacheKeys.UserSession(sessionId);
        
        await _cache.SetAsync(key, session, SessionTtl, ct);
        
        // Track tất cả sessions của user để có thể force-logout
        var userSessionsKey = $"user:{session.UserId}:sessions";
        var db = _multiplexer.GetDatabase();
        await db.SetAddAsync(userSessionsKey, sessionId);
        await db.KeyExpireAsync(userSessionsKey, AbsoluteSessionTtl);
        
        _logger.LogInformation(
            "Session created for user {UserId}, sessionId: {SessionId}",
            session.UserId, sessionId);
        
        return sessionId;
    }

    public async Task RefreshSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var key = CacheKeys.UserSession(sessionId);
        var session = await _cache.GetAsync<UserSession>(key, ct);
        
        if (session is not null)
        {
            // Cập nhật LastActivity và gia hạn TTL
            var refreshed = session with { LastActivity = DateTimeOffset.UtcNow };
            await _cache.SetAsync(key, refreshed, SessionTtl, ct);
        }
    }

    public async Task DestroySessionAsync(string sessionId, CancellationToken ct = default)
    {
        var key = CacheKeys.UserSession(sessionId);
        await _cache.DeleteAsync(key, ct);
        _logger.LogInformation("Session {SessionId} destroyed", sessionId);
    }

    /// <summary>
    /// Force logout tất cả sessions của user (dùng khi đổi mật khẩu, bị block)
    /// </summary>
    public async Task DestroyAllUserSessionsAsync(string userId, CancellationToken ct = default)
    {
        var db = _multiplexer.GetDatabase();
        var userSessionsKey = $"user:{userId}:sessions";
        
        var sessionIds = await db.SetMembersAsync(userSessionsKey);
        
        var deleteTasks = sessionIds
            .Select(sid => _cache.DeleteAsync(CacheKeys.UserSession(sid!), ct))
            .ToList();
        deleteTasks.Add(_cache.DeleteAsync(userSessionsKey, ct));
        
        await Task.WhenAll(deleteTasks);
        
        _logger.LogWarning(
            "All {Count} sessions destroyed for user {UserId}",
            sessionIds.Length, userId);
    }
}
```

### 8.1 ASP.NET Core Session Middleware

```csharp
// Middleware/SessionMiddleware.cs
namespace MyApp.Api.Middleware;

public class SessionAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IUserSessionService sessionService)
    {
        // Lấy session ID từ cookie
        var sessionId = context.Request.Cookies["session_id"];
        
        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await sessionService.GetSessionAsync(sessionId);
            
            if (session is not null)
            {
                // Gắn session vào HttpContext.Items để controller có thể dùng
                context.Items["UserSession"] = session;
                context.Items["UserId"] = session.UserId;
            }
        }

        await _next(context);
    }
}
```

---

## 9. Rate Limiting với Redis

### 9.1 Fixed Window Rate Limiting

Đơn giản nhất: đếm requests trong một cửa sổ thời gian cố định.

```csharp
// RateLimiting/FixedWindowRateLimiter.cs
namespace MyApp.Infrastructure.RateLimiting;

public class FixedWindowRateLimiter
{
    private readonly IRedisCacheService _cache;

    public FixedWindowRateLimiter(IRedisCacheService cache) => _cache = cache;

    /// <summary>
    /// Kiểm tra xem request có được phép không (Fixed Window)
    /// </summary>
    /// <param name="identifier">IP address hoặc user ID</param>
    /// <param name="limit">Số requests tối đa</param>
    /// <param name="window">Khoảng thời gian (ví dụ: 1 phút)</param>
    public async Task<RateLimitResult> CheckAsync(
        string identifier,
        int limit,
        TimeSpan window)
    {
        var key = $"rl:fixed:{identifier}:{GetWindowKey(window)}";
        
        var count = await _cache.IncrementAsync(key);
        
        if (count == 1)
        {
            // Key vừa được tạo, set TTL
            // Note: Trong thực tế, nên dùng Lua script để atomic
            // Ở đây đơn giản hóa
        }

        var remaining = Math.Max(0, limit - (int)count);
        var allowed = count <= limit;

        return new RateLimitResult
        {
            IsAllowed = allowed,
            CurrentCount = (int)count,
            Limit = limit,
            Remaining = remaining,
            ResetAt = GetWindowResetTime(window)
        };
    }

    private static string GetWindowKey(TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = (long)window.TotalSeconds;
        var windowStart = now.ToUnixTimeSeconds() / windowSeconds * windowSeconds;
        return windowStart.ToString();
    }

    private static DateTimeOffset GetWindowResetTime(TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = (long)window.TotalSeconds;
        var windowStart = now.ToUnixTimeSeconds() / windowSeconds * windowSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(windowStart + windowSeconds);
    }
}

public record RateLimitResult
{
    public bool IsAllowed { get; init; }
    public int CurrentCount { get; init; }
    public int Limit { get; init; }
    public int Remaining { get; init; }
    public DateTimeOffset ResetAt { get; init; }
}
```

### 9.2 Sliding Window Rate Limiting (Redis Sorted Set)

Chính xác hơn Fixed Window: không có spike khi cửa sổ reset.

```csharp
// RateLimiting/SlidingWindowRateLimiter.cs
using StackExchange.Redis;

namespace MyApp.Infrastructure.RateLimiting;

public class SlidingWindowRateLimiter
{
    private readonly IDatabase _db;

    public SlidingWindowRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }

    /// <summary>
    /// Sliding Window dùng Sorted Set:
    /// - Score = timestamp của request
    /// - Xóa entries cũ hơn window
    /// - Đếm entries còn lại
    /// 
    /// Toàn bộ operation là ATOMIC nhờ Lua script
    /// </summary>
    public async Task<RateLimitResult> CheckAndIncrementAsync(
        string identifier,
        int limit,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var key = $"rl:sliding:{identifier}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)window.TotalMilliseconds;
        var windowStart = now - windowMs;
        var requestId = Guid.NewGuid().ToString("N");

        // Lua script để đảm bảo atomic execution
        // Redis sẽ thực thi toàn bộ script này như một transaction
        const string luaScript = @"
            local key = KEYS[1]
            local now = tonumber(ARGV[1])
            local window_start = tonumber(ARGV[2])
            local limit = tonumber(ARGV[3])
            local request_id = ARGV[4]
            local window_ms = tonumber(ARGV[5])
            
            -- Xóa các entries cũ hơn window
            redis.call('ZREMRANGEBYSCORE', key, 0, window_start)
            
            -- Đếm số requests hiện tại trong window
            local count = redis.call('ZCARD', key)
            
            local allowed = 0
            if count < limit then
                -- Thêm request hiện tại vào sorted set
                redis.call('ZADD', key, now, request_id)
                allowed = 1
                count = count + 1
            end
            
            -- Set TTL cho key (cleanup tự động)
            redis.call('PEXPIRE', key, window_ms * 2)
            
            return {allowed, count}
        ";

        var result = (RedisResult[])await _db.ScriptEvaluateAsync(
            luaScript,
            keys: [key],
            values: [now, windowStart, limit, requestId, windowMs]);

        var isAllowed = (int)result[0] == 1;
        var currentCount = (int)result[1];

        return new RateLimitResult
        {
            IsAllowed = isAllowed,
            CurrentCount = currentCount,
            Limit = limit,
            Remaining = Math.Max(0, limit - currentCount),
            ResetAt = DateTimeOffset.UtcNow.Add(window)
        };
    }
}
```

### 9.3 Token Bucket Rate Limiting

Cho phép bursts nhỏ trong khi vẫn giới hạn rate trung bình theo thời gian.

```csharp
// RateLimiting/TokenBucketRateLimiter.cs
using StackExchange.Redis;

namespace MyApp.Infrastructure.RateLimiting;

/// <summary>
/// Token Bucket Algorithm:
/// - Bucket chứa tối đa 'capacity' tokens
/// - Tokens được thêm vào với rate 'refillRate' tokens/giây
/// - Mỗi request tiêu thụ 1 token
/// - Nếu không đủ tokens → từ chối request
/// 
/// Ưu điểm: Cho phép burst ngắn hạn
/// </summary>
public class TokenBucketRateLimiter
{
    private readonly IDatabase _db;

    public TokenBucketRateLimiter(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }

    public async Task<RateLimitResult> TryConsumeAsync(
        string identifier,
        int capacity,        // Dung lượng bucket tối đa
        double refillRate,   // Tokens/giây được thêm vào
        int tokensRequired = 1)
    {
        var key = $"rl:token:{identifier}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; // Seconds

        const string luaScript = @"
            local key = KEYS[1]
            local capacity = tonumber(ARGV[1])
            local refill_rate = tonumber(ARGV[2])
            local now = tonumber(ARGV[3])
            local tokens_required = tonumber(ARGV[4])
            
            -- Lấy trạng thái hiện tại của bucket
            local bucket = redis.call('HMGET', key, 'tokens', 'last_refill')
            local tokens = tonumber(bucket[1])
            local last_refill = tonumber(bucket[2])
            
            -- Nếu bucket chưa tồn tại, khởi tạo đầy
            if tokens == nil then
                tokens = capacity
                last_refill = now
            else
                -- Tính số tokens đã được refill từ lần trước
                local elapsed = now - last_refill
                local refilled = elapsed * refill_rate
                tokens = math.min(capacity, tokens + refilled)
                last_refill = now
            end
            
            local allowed = 0
            if tokens >= tokens_required then
                tokens = tokens - tokens_required
                allowed = 1
            end
            
            -- Lưu trạng thái mới
            redis.call('HSET', key, 'tokens', tokens, 'last_refill', last_refill)
            redis.call('EXPIRE', key, math.ceil(capacity / refill_rate) * 2)
            
            return {allowed, math.floor(tokens)}
        ";

        var result = (RedisResult[])await _db.ScriptEvaluateAsync(
            luaScript,
            keys: [key],
            values: [capacity, refillRate, now, tokensRequired]);

        var isAllowed = (int)result[0] == 1;
        var tokensLeft = (int)result[1];

        return new RateLimitResult
        {
            IsAllowed = isAllowed,
            CurrentCount = capacity - tokensLeft,
            Limit = capacity,
            Remaining = tokensLeft,
            ResetAt = DateTimeOffset.UtcNow.AddSeconds(
                tokensRequired / refillRate) // Thời gian để có đủ token
        };
    }
}
```

### 9.4 Rate Limiting Middleware

```csharp
// Middleware/RateLimitingMiddleware.cs
namespace MyApp.Api.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // Cấu hình rate limit theo endpoint
    private static readonly Dictionary<string, (int Limit, TimeSpan Window)> EndpointLimits = new()
    {
        ["/api/products/search"] = (100, TimeSpan.FromMinutes(1)),
        ["/api/auth/login"] = (5, TimeSpan.FromMinutes(15)),
        ["/api/products"] = (200, TimeSpan.FromMinutes(1)),
    };

    private static readonly (int Limit, TimeSpan Window) DefaultLimit = (1000, TimeSpan.FromMinutes(1));

    public RateLimitingMiddleware(
        RequestDelegate next,
        SlidingWindowRateLimiter rateLimiter,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Identifier: dùng user ID nếu authenticated, IP nếu không
        var identifier = context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        var path = context.Request.Path.Value?.ToLower() ?? "/";
        var (limit, window) = EndpointLimits.TryGetValue(path, out var config)
            ? config
            : DefaultLimit;

        var result = await _rateLimiter.CheckAndIncrementAsync(identifier, limit, window);

        // Thêm rate limit headers vào response (theo chuẩn RateLimit-* headers)
        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetAt.ToUnixTimeSeconds().ToString();

        if (!result.IsAllowed)
        {
            _logger.LogWarning(
                "Rate limit exceeded for {Identifier} on {Path}. Count: {Count}/{Limit}",
                identifier, path, result.CurrentCount, limit);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = 
                ((int)(result.ResetAt - DateTimeOffset.UtcNow).TotalSeconds).ToString();
            
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Too Many Requests",
                retryAfter = result.ResetAt
            });
            return;
        }

        await _next(context);
    }
}

// Extension method để đăng ký middleware
public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRedisRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
```

---

## 10. Best Practices & Production Checklist

### 10.1 Performance Best Practices

```csharp
// BestPractices/PerformanceOptimization.cs

// ✅ ĐÚNG: Dùng Pipeline để batch nhiều lệnh
public async Task BatchOperationsAsync(IConnectionMultiplexer multiplexer)
{
    var db = multiplexer.GetDatabase();
    var batch = db.CreateBatch();
    
    // Tất cả lệnh này được gửi trong 1 round trip
    var tasks = new[]
    {
        batch.StringSetAsync("key1", "value1"),
        batch.StringSetAsync("key2", "value2"),
        batch.StringSetAsync("key3", "value3"),
        batch.StringGetAsync("key1"),
    };
    
    batch.Execute();
    await Task.WhenAll(tasks);
}

// ❌ SAI: Nhiều round trips riêng lẻ
public async Task BadBatchOperationsAsync(IDatabase db)
{
    await db.StringSetAsync("key1", "value1"); // Round trip 1
    await db.StringSetAsync("key2", "value2"); // Round trip 2
    await db.StringSetAsync("key3", "value3"); // Round trip 3
    // → 3x latency thay vì 1x
}

// ✅ ĐÚNG: Dùng Lua script cho atomic operations
public async Task AtomicCheckAndSetAsync(IDatabase db, string key, int limit)
{
    const string script = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return current
    ";
    
    await db.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { 60 });
}

// ✅ ĐÚNG: Serialize hiệu quả với System.Text.Json
public static class EfficientSerializer
{
    public static byte[] Serialize<T>(T value)
    {
        // SerializeToUtf8Bytes tránh intermediate string allocation
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public static T? Deserialize<T>(byte[] data)
    {
        return JsonSerializer.Deserialize<T>(data);
    }
}
```

### 10.2 Resilience Patterns

```csharp
// BestPractices/Resilience.cs

/// <summary>
/// Circuit Breaker cho Redis - tránh cascade failure
/// Khi Redis down, fallback về database (degraded mode)
/// </summary>
public class ResilientCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly ILogger<ResilientCacheService> _logger;
    
    // Circuit breaker state
    private volatile bool _circuitOpen = false;
    private DateTime _lastFailure = DateTime.MinValue;
    private int _consecutiveFailures = 0;
    
    private const int FailureThreshold = 5;
    private static readonly TimeSpan OpenCircuitDuration = TimeSpan.FromSeconds(30);

    public ResilientCacheService(IRedisCacheService cache, ILogger<ResilientCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        // Circuit breaker: nếu circuit mở, không thử kết nối Redis
        if (IsCircuitOpen())
        {
            _logger.LogWarning("Redis circuit breaker OPEN - cache MISS for {Key}", key);
            return default;
        }

        try
        {
            var result = await _cache.GetAsync<T>(key, ct);
            ResetCircuit(); // Success → reset failure count
            return result;
        }
        catch (RedisException ex)
        {
            RecordFailure();
            _logger.LogError(ex, "Redis failure ({Count}/{Threshold})", 
                _consecutiveFailures, FailureThreshold);
            return default; // Graceful degradation
        }
    }

    private bool IsCircuitOpen()
    {
        if (!_circuitOpen) return false;
        
        // Sau OpenCircuitDuration, cho phép thử lại (half-open)
        if (DateTime.UtcNow - _lastFailure > OpenCircuitDuration)
        {
            _circuitOpen = false;
            _consecutiveFailures = 0;
            _logger.LogInformation("Redis circuit breaker HALF-OPEN - attempting reconnect");
            return false;
        }
        
        return true;
    }

    private void RecordFailure()
    {
        _consecutiveFailures++;
        _lastFailure = DateTime.UtcNow;
        
        if (_consecutiveFailures >= FailureThreshold)
        {
            _circuitOpen = true;
            _logger.LogError("Redis circuit breaker OPENED after {Count} failures", 
                _consecutiveFailures);
        }
    }

    private void ResetCircuit()
    {
        if (_consecutiveFailures > 0)
        {
            _consecutiveFailures = 0;
            _circuitOpen = false;
        }
    }
}
```

### 10.3 Monitoring và Observability

```csharp
// BestPractices/Monitoring.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class ObservableCacheService : IRedisCacheService
{
    private readonly IRedisCacheService _inner;
    
    // .NET Metrics API (OpenTelemetry compatible)
    private static readonly ActivitySource ActivitySource = new("MyApp.Cache");
    private static readonly Meter Meter = new("MyApp.Cache");
    
    private static readonly Counter<long> CacheHits = 
        Meter.CreateCounter<long>("cache.hits", "requests", "Cache hit count");
    private static readonly Counter<long> CacheMisses = 
        Meter.CreateCounter<long>("cache.misses", "requests", "Cache miss count");
    private static readonly Histogram<double> CacheLatency = 
        Meter.CreateHistogram<double>("cache.latency", "ms", "Cache operation latency");

    public ObservableCacheService(IRedisCacheService inner) => _inner = inner;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("cache.get");
        activity?.SetTag("cache.key", key);
        
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetAsync<T>(key, ct);
            sw.Stop();
            
            CacheLatency.Record(sw.Elapsed.TotalMilliseconds, 
                new KeyValuePair<string, object?>("operation", "get"));
            
            if (result is not null)
            {
                CacheHits.Add(1, new KeyValuePair<string, object?>("key_prefix", GetKeyPrefix(key)));
                activity?.SetTag("cache.hit", true);
            }
            else
            {
                CacheMisses.Add(1, new KeyValuePair<string, object?>("key_prefix", GetKeyPrefix(key)));
                activity?.SetTag("cache.hit", false);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static string GetKeyPrefix(string key) =>
        key.Contains(':') ? key[..key.IndexOf(':')] : key;

    // Delegate các methods khác đến _inner
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        => _inner.SetAsync(key, value, expiry, ct);
    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        => _inner.DeleteAsync(key, ct);
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => _inner.ExistsAsync(key, ct);
    public Task<long> IncrementAsync(string key, long value = 1, CancellationToken ct = default)
        => _inner.IncrementAsync(key, value, ct);
    public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default)
        => _inner.SetIfNotExistsAsync(key, value, expiry, ct);
    public Task PublishAsync(string channel, string message, CancellationToken ct = default)
        => _inner.PublishAsync(channel, message, ct);
    public Task SubscribeAsync(string channel, Action<string> handler)
        => _inner.SubscribeAsync(channel, handler);
}
```

### 10.4 Production Checklist

```
✅ PRODUCTION READINESS CHECKLIST

CONNECTION & RESILIENCE
  ☐ AbortOnConnectFail = false (không crash khi Redis khởi động sau app)
  ☐ ConnectRetry được cấu hình
  ☐ ConnectTimeout & SyncTimeout hợp lý (3-5 giây)
  ☐ Circuit breaker được implement
  ☐ Graceful degradation khi Redis unavailable

SECURITY
  ☐ Password được đặt cho Redis
  ☐ Dùng SSL/TLS trong production
  ☐ AllowAdmin = false trong production
  ☐ Firewall: chỉ cho phép app servers connect đến Redis port
  ☐ Secrets được lưu trong environment variables, không hardcode

DATA MANAGEMENT
  ☐ Tất cả keys đều có TTL (không để key tồn tại mãi mãi)
  ☐ Key naming convention nhất quán (dùng CacheKeys static class)
  ☐ Tránh lưu object quá lớn (> 1MB)
  ☐ Maxmemory-policy được cấu hình (allkeys-lru hoặc volatile-lru)

MONITORING
  ☐ Cache hit rate metrics được track
  ☐ Redis memory usage được monitor
  ☐ Latency alerts được cấu hình
  ☐ Distributed tracing (OpenTelemetry)
  ☐ Health check endpoint cho Redis

PERFORMANCE
  ☐ Dùng Pipeline/Batch cho nhiều operations
  ☐ Dùng Lua script cho atomic operations
  ☐ ConnectionMultiplexer được đăng ký singleton
  ☐ Không tạo nhiều ConnectionMultiplexer instances
  ☐ Sử dụng async/await đúng cách (tránh .Result, .Wait())

HIGH AVAILABILITY
  ☐ Redis Sentinel hoặc Redis Cluster cho production
  ☐ Replica được cấu hình
  ☐ Backup strategy cho critical data
  ☐ Failover được test
```

### 10.5 Kiến trúc tổng thể

```
COMPLETE SYSTEM ARCHITECTURE:

                              Internet
                                 │
                    ┌────────────▼────────────┐
                    │       API Gateway /      │
                    │       Load Balancer      │
                    └─────────────┬────────────┘
                                  │
              ┌───────────────────┼───────────────────┐
              │                   │                   │
    ┌─────────▼──────┐  ┌─────────▼──────┐  ┌────────▼───────┐
    │  API Server 1  │  │  API Server 2  │  │  API Server 3  │
    │                │  │                │  │                │
    │ ┌────────────┐ │  │ ┌────────────┐ │  │ ┌────────────┐ │
    │ │Product Svc │ │  │ │Product Svc │ │  │ │Product Svc │ │
    │ │Rate Limiter│ │  │ │Rate Limiter│ │  │ │Rate Limiter│ │
    │ │Session Svc │ │  │ │Session Svc │ │  │ │Session Svc │ │
    │ └────────────┘ │  │ └────────────┘ │  │ └────────────┘ │
    └────────┬───────┘  └────────┬───────┘  └───────┬────────┘
             │                   │                   │
             └───────────────────┼───────────────────┘
                                 │ StackExchange.Redis
                    ┌────────────▼────────────┐
                    │    Redis Cluster         │
                    │                          │
                    │  ┌──────┐  ┌──────┐     │
                    │  │Master│  │Master│ ...  │
                    │  └──┬───┘  └──┬───┘     │
                    │     │         │          │
                    │  ┌──▼───┐  ┌──▼───┐     │
                    │  │Slave │  │Slave │      │
                    │  └──────┘  └──────┘     │
                    └────────────┬────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │    PostgreSQL            │
                    │    (Primary Database)    │
                    └─────────────────────────┘
```

---

## Tổng kết

Trong bài này chúng ta đã tìm hiểu toàn diện về Distributed Caching với Redis trong .NET:

| Topic | Pattern/Tool | Khi nào dùng |
|-------|-------------|--------------|
| Single object cache | Cache-Aside | Đọc nhiều, ghi ít |
| Write performance | Write-Through | Cần cache luôn fresh |
| Extreme write perf | Write-Behind | Chấp nhận eventual consistency |
| Auto expiry | TTL | Mọi cache |
| Cross-service invalidation | Pub/Sub | Microservices |
| Related data invalidation | Tag-based | Có nhóm cache liên quan |
| Request throttling | Sliding Window | API rate limiting |
| Burst allowance | Token Bucket | Flexible rate limiting |
| Session sharing | Redis Session | Multi-instance apps |

**Bước tiếp theo:**
- Tìm hiểu Redis Cluster cho high availability
- Nghiên cứu RedisJSON module cho native JSON support
- Explore RedisSearch cho full-text search trực tiếp trong Redis
- Tìm hiểu Redis Streams cho event sourcing

---

*Bài viết tiếp theo: **Bài 16: Message Queues với RabbitMQ và MassTransit trong .NET***
