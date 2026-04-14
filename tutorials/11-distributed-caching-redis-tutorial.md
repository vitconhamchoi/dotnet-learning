# Distributed Caching at Scale: Redis Cluster, Cache Strategies và chống Cache Stampede

## 1. Tại sao caching là bắt buộc ở quy mô lớn

Khi hệ thống phục vụ hàng triệu người dùng đồng thời, database không thể là nguồn duy nhất của mọi read request. Hãy nghĩ về các số liệu thực tế:

- PostgreSQL có thể handle khoảng 10.000-50.000 queries/giây (tùy hardware)
- Redis có thể handle 1.000.000+ operations/giây
- Latency database query: 1-10ms. Latency Redis GET: 0.1-0.5ms

Với 10 triệu user active, mỗi user gửi 10 request/phút, bạn cần ~1.7 triệu requests/giây. Không có database nào chịu được điều đó nếu không có caching.

Nhưng caching không chỉ là "đặt Redis vào là xong". Caching sai cách gây ra các bug nghiêm trọng:
- **Stale data**: user thấy dữ liệu cũ quá lâu
- **Cache stampede**: 10.000 request đến cùng lúc khi cache expired, đổ hết vào DB
- **Cache poisoning**: data sai được cache, phải đợi TTL hết mới fix được
- **Thundering herd**: sau deploy, mọi cache miss đều đổ vào DB

---

## 2. Cache Strategies: chọn đúng pattern cho từng use case

### 2.1 Cache-Aside (Lazy Loading)

Pattern phổ biến nhất. App check cache trước, nếu miss thì load từ DB và cache lại.

```csharp
public class ProductService
{
    private readonly IDatabase _redis;
    private readonly IProductRepository _repo;
    private readonly ILogger<ProductService> _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        
        // 1. Thử lấy từ cache
        var cached = await _redis.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache hit for product {ProductId}", id);
            return JsonSerializer.Deserialize<ProductDto>(cached!);
        }

        // 2. Cache miss - load từ DB
        _logger.LogDebug("Cache miss for product {ProductId}", id);
        var product = await _repo.FindByIdAsync(id, ct);
        
        if (product is null)
        {
            // Cache null result để tránh DB hit liên tục cho ID không tồn tại
            await _redis.StringSetAsync(cacheKey, "null", TimeSpan.FromMinutes(1));
            return null;
        }

        // 3. Cache với TTL
        var serialized = JsonSerializer.Serialize(ProductDto.From(product));
        await _redis.StringSetAsync(cacheKey, serialized, DefaultTtl);
        
        return JsonSerializer.Deserialize<ProductDto>(serialized);
    }

    public async Task UpdateAsync(UpdateProductCommand cmd, CancellationToken ct = default)
    {
        await _repo.UpdateAsync(cmd, ct);
        
        // Invalidate cache sau khi update
        var cacheKey = $"product:{cmd.Id}";
        await _redis.KeyDeleteAsync(cacheKey);
        
        // Hoặc invalidate cả category cache nếu cần
        await _redis.KeyDeleteAsync($"products:category:{cmd.CategoryId}");
    }
}
```

### 2.2 Read-Through

Cache tự động load từ data source khi cache miss. App chỉ nói chuyện với cache.

```csharp
// Dùng IMemoryCache hoặc IDistributedCache với GetOrCreateAsync
public class CatalogReadService
{
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _repo;

    public async Task<List<ProductDto>> GetByCategoryAsync(Guid categoryId, CancellationToken ct = default)
    {
        var key = $"products:category:{categoryId}";
        
        return await _cache.GetOrCreateAsync(key, async () =>
        {
            var products = await _repo.GetByCategoryAsync(categoryId, ct);
            return products.Select(ProductDto.From).ToList();
        }, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            // Sliding expiration: gia hạn nếu được access trong window
            SlidingExpiration = TimeSpan.FromMinutes(2)
        }, ct);
    }
}

// Extension method để tiện dùng
public static class DistributedCacheExtensions
{
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<Task<T>> factory,
        DistributedCacheEntryOptions options,
        CancellationToken ct = default)
    {
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<T>(cached)!;

        var value = await factory();
        await cache.SetStringAsync(key, JsonSerializer.Serialize(value), options, ct);
        return value;
    }
}
```

### 2.3 Write-Through

Cache được update đồng thời với database. Đảm bảo cache luôn fresh.

```csharp
public async Task UpdateProductPriceAsync(Guid productId, decimal newPrice, CancellationToken ct = default)
{
    // Update DB và cache trong cùng một transaction logic
    await _repo.UpdatePriceAsync(productId, newPrice, ct);
    
    var cacheKey = $"product:{productId}";
    var product = await _repo.FindByIdAsync(productId, ct);
    if (product is not null)
    {
        // Write-through: cập nhật cache ngay
        await _redis.StringSetAsync(
            cacheKey,
            JsonSerializer.Serialize(ProductDto.From(product)),
            TimeSpan.FromMinutes(5));
    }
}
```

### 2.4 Write-Behind (Write-Back)

App write vào cache trước, background process flush vào database. Tốt cho high-write workload.

```csharp
// Write-behind với channel/queue
public class InventoryCounterService
{
    private readonly IDatabase _redis;
    private readonly Channel<InventoryUpdate> _updateChannel;

    public InventoryCounterService(IDatabase redis)
    {
        _redis = redis;
        _updateChannel = Channel.CreateBounded<InventoryUpdate>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });
        
        // Background flush worker
        _ = FlushToDatabaseAsync();
    }

    public async Task DecrementStockAsync(Guid productId, int quantity)
    {
        var key = $"inventory:{productId}:stock";
        
        // Atomic decrement trong Redis - extremely fast
        var newStock = await _redis.StringDecrementAsync(key, quantity);
        
        if (newStock < 0)
        {
            // Rollback và throw
            await _redis.StringIncrementAsync(key, quantity);
            throw new InsufficientStockException(productId, quantity);
        }

        // Async write to DB queue
        await _updateChannel.Writer.WriteAsync(new InventoryUpdate(productId, (int)newStock));
    }

    private async Task FlushToDatabaseAsync()
    {
        // Batch writes vào DB mỗi 100ms hoặc 100 items
        var batch = new List<InventoryUpdate>();
        
        await foreach (var update in _updateChannel.Reader.ReadAllAsync())
        {
            batch.Add(update);
            
            if (batch.Count >= 100)
            {
                await FlushBatchAsync(batch);
                batch.Clear();
            }
        }
    }

    private async Task FlushBatchAsync(List<InventoryUpdate> updates)
    {
        // Merge duplicate updates - chỉ giữ latest per product
        var latest = updates.GroupBy(u => u.ProductId)
            .Select(g => g.Last())
            .ToList();
        
        await _repo.BulkUpdateStockAsync(latest);
    }
}
```

---

## 3. Cache Stampede Prevention: chống thundering herd

Cache stampede xảy ra khi một popular key expired và hàng nghìn request đến cùng lúc, tất cả đều thấy cache miss và cùng query database.

### 3.1 Probabilistic Early Expiration (PER)

Thay vì đợi đến khi expired, proactively refresh cache khi gần expired với xác suất tăng dần.

```csharp
public class StampedeSafeCache
{
    private readonly IDatabase _redis;
    private readonly Random _random = new();

    public async Task<T?> GetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan ttl,
        double beta = 1.0) where T : class
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // Lấy cả value và expiry time
        var result = await _redis.ScriptEvaluateAsync(
            """
            local val = redis.call('GET', KEYS[1])
            local ttl = redis.call('TTL', KEYS[1])
            return {val, ttl}
            """,
            [(RedisKey)key]);

        if (result is RedisResult[] arr && arr[0].HasValue)
        {
            var remainingTtl = (long)arr[1];
            var totalTtl = (long)ttl.TotalSeconds;
            
            // Xác suất re-compute tăng khi gần expired
            // Công thức: currentTime - (beta * log(random)) > expiry - ttl
            var shouldRefresh = now - beta * Math.Log(_random.NextDouble()) > 
                                (now + remainingTtl) - totalTtl;
            
            if (!shouldRefresh)
                return JsonSerializer.Deserialize<T>(arr[0].ToString()!);
        }

        // Lock để chỉ một process compute
        var lockKey = $"lock:{key}";
        var lockValue = Guid.NewGuid().ToString();
        
        if (await _redis.StringSetAsync(lockKey, lockValue, TimeSpan.FromSeconds(30), When.NotExists))
        {
            try
            {
                var value = await factory();
                await _redis.StringSetAsync(key, JsonSerializer.Serialize(value), ttl);
                return value;
            }
            finally
            {
                // Release lock
                await _redis.ScriptEvaluateAsync(
                    """
                    if redis.call('GET', KEYS[1]) == ARGV[1] then
                        return redis.call('DEL', KEYS[1])
                    end
                    return 0
                    """,
                    [(RedisKey)lockKey],
                    [(RedisValue)lockValue]);
            }
        }
        else
        {
            // Đợi process khác compute xong
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            return await GetAsync(key, factory, ttl, beta);
        }
    }
}
```

### 3.2 Mutex Pattern: chỉ một request đi tới database

```csharp
public class MutexCacheService
{
    private readonly IDatabase _redis;
    private readonly SemaphoreSlim _localLock = new(1, 1);

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan ttl) where T : class
    {
        // Thử lấy từ cache (happy path)
        var cached = await _redis.StringGetAsync(key);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<T>(cached!);

        // Distributed lock để chỉ một pod query DB
        var lockKey = $"creating:{key}";
        var acquired = await _redis.StringSetAsync(
            lockKey, "1", TimeSpan.FromSeconds(30), When.NotExists);

        if (acquired)
        {
            try
            {
                // Double-check sau khi acquire lock
                cached = await _redis.StringGetAsync(key);
                if (cached.HasValue)
                    return JsonSerializer.Deserialize<T>(cached!);

                var value = await factory();
                await _redis.StringSetAsync(key, JsonSerializer.Serialize(value), ttl);
                return value;
            }
            finally
            {
                await _redis.KeyDeleteAsync(lockKey);
            }
        }
        else
        {
            // Poll và chờ lock release
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(100);
                cached = await _redis.StringGetAsync(key);
                if (cached.HasValue)
                    return JsonSerializer.Deserialize<T>(cached!);
            }
            
            // Fallback: query DB trực tiếp nếu chờ quá lâu
            return await factory();
        }
    }
}
```

---

## 4. Redis Cluster cho scale ngang

Khi một Redis node không đủ, Redis Cluster phân chia key space thành 16384 slots.

```csharp
// Setup Redis Cluster với StackExchange.Redis
var configString = "redis-node1:6379,redis-node2:6379,redis-node3:6379," +
                   "connectTimeout=5000,syncTimeout=5000," +
                   "abortConnect=false,ssl=true,password=your-password," +
                   "connectRetry=3,reconnectRetryPolicy=linear";

var config = ConfigurationOptions.Parse(configString);
config.ClientName = "MyApp-v1.0";
config.AllowAdmin = false;

var connection = await ConnectionMultiplexer.ConnectAsync(config);

// Đăng ký như singleton
builder.Services.AddSingleton<IConnectionMultiplexer>(connection);
builder.Services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
```

### 4.1 Hash Tags: đảm bảo related keys nằm cùng slot

Trong Redis Cluster, keys được distribute theo hash. Nếu cần multi-key operation (MGET, transaction), các keys phải nằm cùng slot.

```csharp
// Dùng hash tags {} để group keys vào cùng slot
public class OrderCacheService
{
    private readonly IDatabase _redis;

    public async Task CacheOrderWithItemsAsync(OrderDto order)
    {
        var orderId = order.Id;
        
        // Hash tag {orderId} đảm bảo tất cả keys nằm cùng slot
        var orderKey = $"{{order:{orderId}}}:data";
        var itemsKey = $"{{order:{orderId}}}:items";
        var statusKey = $"{{order:{orderId}}}:status";

        // Batch write cùng lúc
        var batch = _redis.CreateBatch();
        var tasks = new[]
        {
            batch.StringSetAsync(orderKey, JsonSerializer.Serialize(order), TimeSpan.FromHours(1)),
            batch.StringSetAsync(itemsKey, JsonSerializer.Serialize(order.Items), TimeSpan.FromHours(1)),
            batch.StringSetAsync(statusKey, order.Status.ToString(), TimeSpan.FromHours(1))
        };
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<(OrderDto? Order, List<OrderItemDto>? Items)> GetOrderWithItemsAsync(Guid orderId)
    {
        var orderKey = $"{{order:{orderId}}}:data";
        var itemsKey = $"{{order:{orderId}}}:items";

        // MGET hoạt động vì cùng slot
        var results = await _redis.StringGetAsync([orderKey, itemsKey]);
        
        var order = results[0].HasValue 
            ? JsonSerializer.Deserialize<OrderDto>(results[0]!) 
            : null;
        var items = results[1].HasValue 
            ? JsonSerializer.Deserialize<List<OrderItemDto>>(results[1]!) 
            : null;

        return (order, items);
    }
}
```

---

## 5. Cache Versioning: invalidation theo schema version

Khi deploy code mới có thay đổi serialization format, cache cũ sẽ không deserialize được.

```csharp
public class VersionedCacheService
{
    private const string CacheVersion = "v3"; // Bump khi thay đổi serialization

    private string GetVersionedKey(string key) => $"{CacheVersion}:{key}";

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var versionedKey = GetVersionedKey(key);
        var cached = await _redis.StringGetAsync(versionedKey);
        
        if (!cached.HasValue) return null;
        
        try
        {
            return JsonSerializer.Deserialize<T>(cached!);
        }
        catch (JsonException)
        {
            // Cache với format cũ - delete và return null
            await _redis.KeyDeleteAsync(versionedKey);
            return null;
        }
    }

    // Bulk invalidate tất cả keys version cũ - dùng khi deploy
    public async Task InvalidateOldVersionsAsync(string[] oldVersions)
    {
        foreach (var version in oldVersions)
        {
            var pattern = $"{version}:*";
            // Scan (không dùng KEYS * trong production!)
            await foreach (var key in _redis.KeysAsync(pattern: pattern))
            {
                await _redis.KeyDeleteAsync(key);
            }
        }
    }
}
```

---

## 6. Hybrid Cache: L1 Memory + L2 Redis

Dùng kết hợp in-process memory cache và Redis để giảm network latency cho hot data.

```csharp
// .NET 9+ có HybridCache built-in
builder.Services.AddHybridCache(opts =>
{
    opts.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),          // L2 Redis TTL
        LocalCacheExpiration = TimeSpan.FromSeconds(30) // L1 Memory TTL
    };
});

// Sử dụng
public class ProductQueryService
{
    private readonly HybridCache _cache;

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            $"product:{id}",
            async cToken => 
            {
                var product = await _repo.FindByIdAsync(id, cToken);
                return product is null ? null : ProductDto.From(product);
            },
            cancellationToken: ct);
    }
}
```

---

## 7. Cache Monitoring và Alerting

```csharp
// Metrics cho cache performance
public class InstrumentedCacheService
{
    private readonly IDatabase _redis;
    private static readonly Counter<long> CacheHits = 
        Metrics.CreateCounter<long>("cache_hits_total", "cache_key_prefix");
    private static readonly Counter<long> CacheMisses = 
        Metrics.CreateCounter<long>("cache_misses_total", "cache_key_prefix");
    private static readonly Histogram<double> CacheLatency = 
        Metrics.CreateHistogram<double>("cache_operation_duration_seconds", "operation", "cache_key_prefix");

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var prefix = ExtractPrefix(key);
        using var timer = CacheLatency.NewTimer("get", prefix);
        
        var cached = await _redis.StringGetAsync(key);
        
        if (cached.HasValue)
        {
            CacheHits.Add(1, prefix);
            return JsonSerializer.Deserialize<T>(cached!);
        }

        CacheMisses.Add(1, prefix);
        return null;
    }

    // Kiểm tra Redis info
    public async Task<RedisHealthInfo> GetHealthInfoAsync()
    {
        var server = _redis.Multiplexer.GetServer(_redis.Multiplexer.GetEndPoints()[0]);
        var info = await server.InfoAsync();
        
        var usedMemory = info.FirstOrDefault(g => g.Key == "Memory")?
            .FirstOrDefault(e => e.Key == "used_memory_human").Value;
        var hitRate = CalculateHitRate(info);
        
        return new RedisHealthInfo(usedMemory, hitRate);
    }
}
```

---

## 8. Checklist production cho Distributed Caching

- [ ] Chọn cache strategy phù hợp với từng loại data (cache-aside cho general, write-through cho critical data)
- [ ] Đặt TTL phù hợp - không quá dài (stale data), không quá ngắn (nhiều miss)
- [ ] Cache null results để tránh DB hit cho invalid keys
- [ ] Dùng hash tags trong Redis Cluster cho multi-key operations
- [ ] Implement stampede prevention cho popular, high-traffic keys
- [ ] Version cache keys khi thay đổi schema
- [ ] Monitor hit rate - nếu dưới 80% cần review caching strategy
- [ ] Dùng HybridCache (L1+L2) cho data cực hot
- [ ] Không cache sensitive data (credentials, tokens) trừ khi có encryption
- [ ] Đặt maxmemory và eviction policy phù hợp cho Redis
- [ ] Test failover: app hoạt động đúng khi Redis down (degraded mode)
- [ ] Scan key để delete batch - không dùng KEYS * trong production
