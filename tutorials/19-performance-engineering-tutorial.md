# Performance Engineering: Tối ưu hóa .NET cho High-throughput Systems

## 1. Mindset của Performance Engineering

Performance không phải là "làm cho nó chạy nhanh hơn". Performance engineering là quá trình:
1. **Measure**: đo trước khi optimize
2. **Profile**: tìm bottleneck thực sự, không đoán
3. **Optimize**: thay đổi có chủ đích với target rõ ràng
4. **Validate**: đo lại để confirm improvement

**Quy tắc vàng**: Đừng optimize premature. Đừng optimize những gì chưa đo.

---

## 2. Profiling: tìm bottleneck thực sự

### 2.1 BenchmarkDotNet cho micro-benchmarking

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

// Benchmark so sánh các cách serialize JSON
[MemoryDiagnoser]  // Đo memory allocations
[ThreadingDiagnoser]  // Đo thread info
[RankColumn]
public class JsonSerializationBenchmarks
{
    private readonly OrderDto _order;
    private readonly byte[] _serialized;

    [GlobalSetup]
    public void Setup()
    {
        _order = new OrderDto { /* ... */ };
        _serialized = JsonSerializer.SerializeToUtf8Bytes(_order);
    }

    [Benchmark(Baseline = true)]
    public string SystemTextJson_Serialize()
        => JsonSerializer.Serialize(_order);

    [Benchmark]
    public byte[] SystemTextJson_SerializeToUtf8Bytes()
        => JsonSerializer.SerializeToUtf8Bytes(_order);

    [Benchmark]
    public void SystemTextJson_SerializeToStream()
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, _order);
    }

    [Benchmark]
    public OrderDto? SystemTextJson_Deserialize()
        => JsonSerializer.Deserialize<OrderDto>(_serialized);
}

// Run benchmarks
BenchmarkRunner.Run<JsonSerializationBenchmarks>();
```

### 2.2 dotnet-trace và dotnet-counters

```bash
# Real-time counters
dotnet-counters monitor --process-id <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Collect CPU trace
dotnet-trace collect --process-id <pid> --duration 30s --output trace.nettrace

# Analyze trace trong Visual Studio Diagnostic Tools hoặc SpeedScope
```

---

## 3. Memory Optimization: giảm GC pressure

### 3.1 ArrayPool: tái sử dụng array

```csharp
// Xấu: tạo array mỗi lần
public byte[] SerializeToBytes(OrderDto order)
{
    return JsonSerializer.SerializeToUtf8Bytes(order);  // Allocates mỗi lần
}

// Tốt: dùng ArrayPool + Span
public async Task SerializeToStreamAsync(OrderDto order, Stream stream)
{
    using var buffer = new PooledByteBuffer(initialSize: 4096);
    var writer = new Utf8JsonWriter(buffer);
    JsonSerializer.Serialize(writer, order);
    await stream.WriteAsync(buffer.WrittenMemory);
}

// Hoặc dùng RecyclableMemoryStream
private static readonly RecyclableMemoryStreamManager _streamManager = new();

public async Task SerializeAsync(OrderDto order, Stream output)
{
    using var memoryStream = _streamManager.GetStream();
    await JsonSerializer.SerializeAsync(memoryStream, order);
    memoryStream.Position = 0;
    await memoryStream.CopyToAsync(output);
}
```

### 3.2 Span<T> và Memory<T>: zero-copy processing

```csharp
// Parse CSV order IDs mà không allocate string intermediate
public static IEnumerable<Guid> ParseOrderIds(ReadOnlySpan<char> input)
{
    while (input.Length > 0)
    {
        var commaIndex = input.IndexOf(',');
        ReadOnlySpan<char> token;
        
        if (commaIndex >= 0)
        {
            token = input[..commaIndex].Trim();
            input = input[(commaIndex + 1)..];
        }
        else
        {
            token = input.Trim();
            input = ReadOnlySpan<char>.Empty;
        }

        if (Guid.TryParse(token, out var id))
            yield return id;
    }
}

// Dùng ReadOnlySpan thay vì string.Split
var csvLine = "abc123-...,def456-...,ghi789-...".AsSpan();
foreach (var id in ParseOrderIds(csvLine))
{
    // Process mà không allocate array từ Split
}
```

### 3.3 Object Pooling

```csharp
// Pool expensive objects như HttpClient, Regex, StringBuilder
public class OrderProcessor
{
    private readonly ObjectPool<StringBuilder> _sbPool;
    private readonly ObjectPool<List<OrderLine>> _listPool;

    public OrderProcessor(ObjectPoolProvider poolProvider)
    {
        _sbPool = poolProvider.CreateStringBuilderPool();
        _listPool = poolProvider.Create(new DefaultPooledObjectPolicy<List<OrderLine>>());
    }

    public string BuildOrderSummary(OrderDto order)
    {
        var sb = _sbPool.Get();
        try
        {
            sb.Append($"Order {order.Id}: ");
            foreach (var line in order.Lines)
            {
                sb.Append($"{line.Sku}x{line.Quantity}, ");
            }
            return sb.ToString();
        }
        finally
        {
            _sbPool.Return(sb);
        }
    }
}
```

---

## 4. Async Best Practices: tránh async pitfalls

### 4.1 ConfigureAwait và context

```csharp
// Library code: luôn dùng ConfigureAwait(false) để tránh deadlock
public async Task<OrderDto?> GetOrderAsync(Guid id, CancellationToken ct = default)
{
    var order = await _repo.FindByIdAsync(id, ct).ConfigureAwait(false);
    if (order is null) return null;
    
    var enriched = await _enrichmentService.EnrichAsync(order, ct).ConfigureAwait(false);
    return OrderDto.From(enriched);
}

// Application code: không cần ConfigureAwait vì ASP.NET Core không có SynchronizationContext
public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
{
    var order = await _orderService.GetOrderAsync(id, ct);
    return order is null ? NotFound() : Ok(order);
}
```

### 4.2 ValueTask cho hot paths

```csharp
// Task allocates on heap mỗi lần. ValueTask: không allocate nếu completed synchronously
public interface IOrderCache
{
    ValueTask<OrderDto?> GetAsync(Guid id, CancellationToken ct = default);
}

public class OrderCache : IOrderCache
{
    private readonly IMemoryCache _cache;
    private readonly IOrderRepository _repo;

    public async ValueTask<OrderDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        // Cache hit: return synchronously (no allocation)
        if (_cache.TryGetValue(id, out OrderDto? cached))
            return cached;

        // Cache miss: async operation
        var order = await _repo.FindByIdAsync(id, ct);
        if (order is not null)
            _cache.Set(id, order, TimeSpan.FromMinutes(5));
        
        return order;
    }
}
```

### 4.3 Parallel async operations

```csharp
// Xấu: tuần tự, mỗi call phải đợi call trước
public async Task<HomepageDto> GetHomepageDataSlowAsync(Guid userId, CancellationToken ct)
{
    var profile = await _userService.GetProfileAsync(userId, ct);
    var orders = await _orderService.GetRecentAsync(userId, ct);  // Đợi profile xong
    var recommendations = await _catalogService.GetRecommendationsAsync(userId, ct);  // Đợi orders xong
    return new HomepageDto(profile, orders, recommendations);
}

// Tốt: song song
public async Task<HomepageDto> GetHomepageDataFastAsync(Guid userId, CancellationToken ct)
{
    var profileTask = _userService.GetProfileAsync(userId, ct);
    var ordersTask = _orderService.GetRecentAsync(userId, ct);
    var recommendationsTask = _catalogService.GetRecommendationsAsync(userId, ct);
    
    await Task.WhenAll(profileTask, ordersTask, recommendationsTask);
    
    return new HomepageDto(
        await profileTask,
        await ordersTask,
        await recommendationsTask);
}
```

### 4.4 Channel: producer-consumer với backpressure

```csharp
// Channel thay vì ConcurrentQueue cho high-throughput producer-consumer
public class OrderProcessingPipeline
{
    private readonly Channel<RawOrderEvent> _inputChannel;
    private readonly Channel<ProcessedOrder> _outputChannel;

    public OrderProcessingPipeline()
    {
        // BoundedChannel: backpressure khi consumer chậm
        _inputChannel = Channel.CreateBounded<RawOrderEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait  // Block producer khi channel đầy
        });
        
        _outputChannel = Channel.CreateUnbounded<ProcessedOrder>();
    }

    public async Task ProduceAsync(RawOrderEvent @event, CancellationToken ct)
    {
        await _inputChannel.Writer.WriteAsync(@event, ct);
    }

    public async Task StartProcessingAsync(CancellationToken ct)
    {
        // Parallel consumers
        var consumers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => ConsumeAsync(ct), ct))
            .ToArray();

        await Task.WhenAll(consumers);
        _outputChannel.Writer.Complete();
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var @event in _inputChannel.Reader.ReadAllAsync(ct))
        {
            var processed = await ProcessAsync(@event, ct);
            await _outputChannel.Writer.WriteAsync(processed, ct);
        }
    }
}
```

---

## 5. Database Query Optimization

### 5.1 Projection: chỉ lấy data cần thiết

```csharp
// Xấu: load toàn bộ entity với tất cả navigation properties
var orders = await _db.Orders
    .Include(o => o.Lines)
    .Include(o => o.Customer)
    .Include(o => o.ShipmentHistory)
    .ToListAsync(ct);

// Tốt: project chỉ fields cần thiết
var orderSummaries = await _db.Orders
    .Where(o => o.Status == OrderStatus.Pending)
    .Select(o => new OrderSummaryDto(
        o.Id,
        o.CustomerId,
        o.TotalAmount,
        o.Lines.Count,
        o.PlacedAt))
    .ToListAsync(ct);
```

### 5.2 Bulk operations

```csharp
// EF Core 7+ bulk update/delete
await _db.Orders
    .Where(o => o.CreatedAt < DateTimeOffset.UtcNow.AddMonths(-3) && o.Status == OrderStatus.Cancelled)
    .ExecuteDeleteAsync(ct);

await _db.Orders
    .Where(o => o.Status == OrderStatus.Processing && o.UpdatedAt < DateTimeOffset.UtcNow.AddHours(-24))
    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Failed), ct);

// Bulk insert với Npgsql COPY (nhanh nhất)
await using var writer = await conn.BeginBinaryImportAsync(
    "COPY orders (id, customer_id, total_amount, status) FROM STDIN BINARY", ct);

foreach (var order in orders)
{
    await writer.StartRowAsync(ct);
    await writer.WriteAsync(order.Id, NpgsqlDbType.Uuid, ct);
    await writer.WriteAsync(order.CustomerId, NpgsqlDbType.Uuid, ct);
    await writer.WriteAsync(order.TotalAmount, NpgsqlDbType.Numeric, ct);
    await writer.WriteAsync(order.Status.ToString(), NpgsqlDbType.Text, ct);
}

await writer.CompleteAsync(ct);
```

### 5.3 Connection và Command Pooling

```csharp
// Dùng Dapper cho simple queries, tránh EF Core overhead
public class OptimizedOrderRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public async Task<List<OrderSummaryDto>> GetByCustomerAsync(
        Guid customerId, 
        int limit = 20,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        
        return (await conn.QueryAsync<OrderSummaryDto>(
            """
            SELECT id, total_amount, status, placed_at
            FROM orders
            WHERE customer_id = @customerId
            ORDER BY placed_at DESC
            LIMIT @limit
            """,
            new { customerId, limit })).ToList();
    }
}
```

---

## 6. HTTP Response Optimization

### 6.1 Response compression

```csharp
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<BrotliCompressionProvider>();
    opts.Providers.Add<GzipCompressionProvider>();
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/problem+json"]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(opts =>
    opts.Level = CompressionLevel.Fastest);
```

### 6.2 Output Caching và ETag

```csharp
// ETag cho conditional requests
app.MapGet("/api/catalog/{id}", async (Guid id, IProductService service, HttpContext ctx, CancellationToken ct) =>
{
    var product = await service.GetByIdAsync(id, ct);
    if (product is null) return Results.NotFound();
    
    var etag = new EntityTagHeaderValue($"\"{product.Version}\"");
    
    // Nếu client có cùng ETag, return 304 Not Modified
    if (ctx.Request.Headers.IfNoneMatch.Contains(etag.ToString()))
    {
        return Results.StatusCode(304);
    }
    
    ctx.Response.Headers.ETag = etag.ToString();
    ctx.Response.Headers.CacheControl = "private, max-age=60";
    
    return Results.Ok(product);
});
```

---

## 7. Checklist Performance Engineering

- [ ] Benchmark trước và sau khi optimize - không assume improvement
- [ ] Profile với dotnet-trace, PerfView hoặc JetBrains dotMemory
- [ ] Dùng ArrayPool/MemoryPool thay vì tạo array thường xuyên trong hot paths
- [ ] Span<T> cho string processing, tránh intermediate string allocations
- [ ] ValueTask cho methods thường complete synchronously (cache hit)
- [ ] Parallel async operations với Task.WhenAll thay vì await sequential
- [ ] ConfigureAwait(false) trong library code
- [ ] EF Core projection: chỉ select fields cần thiết
- [ ] Bulk operations cho insert/update/delete lớn
- [ ] Response compression (Brotli > Gzip)
- [ ] Output caching và ETag cho read endpoints
- [ ] Monitor GC pressure: Gen0/Gen1/Gen2 collection rate
- [ ] Đặt target performance SLO: p50 < 50ms, p95 < 200ms, p99 < 500ms
- [ ] Load test với k6 hoặc Bombardier trước khi go live
