# Kiến trúc Tổng thể: Thiết kế Distributed System cho 1 Tỷ Người Dùng

## 1. Suy nghĩ ở quy mô tỉ người dùng

Thiết kế cho 1 tỷ người dùng không phải là "làm cho hệ thống 1 triệu người dùng lớn hơn 1000 lần". Đây là một bài toán chất lượng khác, đòi hỏi kiến trúc khác biệt từ gốc rễ.

**Các con số cần nghĩ đến**:
- 1 tỷ user, 300 triệu daily active users (DAU)
- Peak: 50-100 triệu concurrent users
- Với 10 requests/user/phút: ~16 triệu requests/phút → ~270.000 requests/giây
- Với response size 2KB trung bình: ~540 GB/s bandwidth outgoing

Không một server đơn lẻ, không một datacenter đơn lẻ có thể handle được điều này.

---

## 2. Nguyên tắc kiến trúc cho scale cực lớn

### 2.1 Everything is horizontal

Thiết kế mọi thứ để scale ngang (horizontal), không scale dọc (vertical). Scale dọc có giới hạn vật lý và tạo single point of failure.

- Stateless services: không lưu state trong process
- State được externalize: database, cache, queue
- Mỗi service có thể chạy N instance đồng thời

### 2.2 Partition everything

Dữ liệu phải được partition theo nhiều chiều:
- **Geographic**: US region, EU region, APAC region
- **User segment**: VIP users có dedicated resources
- **Functional**: orders, inventory, catalog là isolated data stores
- **Temporal**: hot data (recent) vs cold data (archive)

### 2.3 Async over sync

Ở quy mô lớn, synchronous chain calls tạo latency cascade. Khi service A gọi B, B gọi C, C gọi D, tổng latency là tổng của tất cả. Nếu một service chậm, toàn bộ chain bị ảnh hưởng.

Async (event-driven) tách biệt temporal coupling: producer không phải đợi consumer.

### 2.4 Accept eventual consistency

Strong consistency ở quy mô lớn cực kỳ tốn kém (CAP theorem). Phần lớn use case chấp nhận được eventual consistency với SLA rõ ràng (ví dụ: tồn kho được consistent trong vòng 1 giây).

Chỉ enforce strong consistency ở những nơi thực sự cần thiết về mặt business (ví dụ: payment transaction).

---

## 3. Kiến trúc Reference Architecture

```text
                         ┌──────────────────────────────────────────┐
                         │            Global DNS / Anycast           │
                         │      (CloudFlare / AWS Route53 / Azure)   │
                         └──────────────┬───────────────────────────┘
                                        │ Route đến region gần nhất
                              ┌─────────┴──────────┐
                              │                    │
                    ┌─────────▼──────┐   ┌─────────▼──────┐
                    │  US-EAST CDN   │   │  EU-WEST CDN   │
                    │  (CloudFront)  │   │  (CloudFront)  │
                    └────────┬───────┘   └────────┬───────┘
                             │                    │
                    ┌────────▼───────────────────▼───────┐
                    │         API Gateway Layer           │
                    │  ┌──────────┐  ┌──────────────┐    │
                    │  │ Web BFF  │  │ Mobile BFF   │    │
                    │  │ (YARP)   │  │   (YARP)     │    │
                    │  └────┬─────┘  └──────┬───────┘    │
                    └───────┼───────────────┼────────────┘
                            │               │
            ┌───────────────┼───────────────┼──────────────────┐
            │               │               │                  │
   ┌────────▼──────┐ ┌──────▼─────┐ ┌──────▼──────┐ ┌────────▼──────┐
   │ Order Service │ │ Catalog Svc│ │  User Svc   │ │ Payment Svc   │
   │  (Orleans)   │ │  (ASP.NET) │ │  (ASP.NET)  │ │  (ASP.NET)    │
   └────────┬──────┘ └──────┬─────┘ └──────┬──────┘ └────────┬──────┘
            │               │               │                  │
   ┌────────▼──────────────────────────────────────────────────▼──────┐
   │                     Message Bus Layer                             │
   │              Kafka (high-throughput events)                       │
   │              RabbitMQ/MassTransit (commands, sagas)               │
   └────────┬──────────────────────────────────────────────────┬──────┘
            │                                                  │
   ┌────────▼──────┐                                 ┌────────▼──────┐
   │  Data Layer   │                                 │  Worker Layer  │
   │  ┌──────────┐ │                                 │ ┌────────────┐ │
   │  │PostgreSQL│ │                                 │ │ Projection │ │
   │  │ (sharded)│ │                                 │ │  Workers   │ │
   │  └──────────┘ │                                 │ └────────────┘ │
   │  ┌──────────┐ │                                 │ ┌────────────┐ │
   │  │  Redis   │ │                                 │ │ Background │ │
   │  │ Cluster  │ │                                 │ │  Workers   │ │
   │  └──────────┘ │                                 │ └────────────┘ │
   └───────────────┘                                 └────────────────┘
```

---

## 4. Deep Dive: Order Domain tại scale 1 tỷ user

### 4.1 Order Domain Design

```csharp
// Order aggregate - thiết kế cho high throughput
public class Order
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Region { get; private set; } = "";  // Shard key: us-east, eu-west, apac
    public string Shard { get; private set; } = "";   // Database shard
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public long Version { get; private set; }  // Optimistic concurrency
    
    // Domain events
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> Events => _events;
}

// Order Grain - stateful với Orleans cho real-time operations
public class OrderGrain : Grain, IOrderGrain
{
    private readonly IPersistentState<OrderState> _state;
    private readonly IOrderMetrics _metrics;
    private readonly ILogger<OrderGrain> _logger;

    public async Task<PlaceOrderResult> PlaceAsync(PlaceOrderRequest request)
    {
        if (_state.State.Status != OrderStatus.None)
            throw new InvalidOperationException("Order already placed");

        using var activity = OrderTracing.StartPlace(this.GetPrimaryKey());
        
        try
        {
            _state.State = new OrderState
            {
                Status = OrderStatus.Pending,
                CustomerId = request.CustomerId,
                Lines = request.Lines,
                TotalAmount = request.Lines.Sum(l => l.Price * l.Quantity),
                PlacedAt = DateTimeOffset.UtcNow,
                Version = 1
            };

            await _state.WriteStateAsync();
            
            // Publish integration event
            var bus = GrainFactory.GetGrain<IEventBusGrain>(0);
            await bus.PublishAsync(new OrderPlacedEvent(this.GetPrimaryKey(), _state.State));
            
            _metrics.RecordOrderPlaced(_state.State.TotalAmount, request.Region);
            _logger.LogInformation("Order {OrderId} placed for customer {CustomerId}", 
                this.GetPrimaryKey(), request.CustomerId);

            return new PlaceOrderResult(this.GetPrimaryKey(), OrderStatus.Pending);
        }
        catch (Exception ex)
        {
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

### 4.2 Multi-region Write Strategy

```csharp
// Write routing: route về region gần nhất của user
public class GeoAwareOrderService
{
    private readonly IRegionRouter _regionRouter;
    private readonly Dictionary<string, IOrderClient> _regionalClients;

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderRequest request, 
        string userRegion,
        CancellationToken ct = default)
    {
        // Xác định region phù hợp
        var targetRegion = await _regionRouter.GetPrimaryRegionAsync(userRegion);
        var client = _regionalClients[targetRegion];

        try
        {
            return await client.PlaceOrderAsync(request, ct);
        }
        catch (RegionUnavailableException)
        {
            // Failover sang region thứ hai
            var fallbackRegion = await _regionRouter.GetFallbackRegionAsync(userRegion);
            var fallbackClient = _regionalClients[fallbackRegion];
            return await fallbackClient.PlaceOrderAsync(request, ct);
        }
    }
}
```

---

## 5. Capacity Planning

### 5.1 Tính toán tài nguyên

```text
Assumption:
- 300M DAU
- Peak 100M concurrent users
- 10 requests/user/phút = ~17M req/phút = ~270K req/giây
- Mỗi service instance handle 1000 req/giây

Cần:
- API Gateway: 270K / 1000 = 270 instances → 30 instances/region với 9 regions
- Order Service: 50K req/giây → 50 instances
- Catalog Service: 100K req/giây → 100 instances (cache aggressively)
- User Service: 30K req/giây → 30 instances

Database:
- Orders: 1B/ngày mới → 365B/năm → Cần sharding
- Mỗi shard: max 100M rows → cần 10 shards
- Read replicas: 5 replica / shard

Cache:
- Redis Cluster: 10-20 nodes
- Cache hit rate > 90%: 90K cache hits/giây → Redis 3M ops/giây → ok

Message:
- Kafka: 10K events/giây → 10 partitions là đủ
- Consumer lag < 1 giây
```

---

## 6. Failure Scenarios và Mitigation

### 6.1 Database Failover

```csharp
// Tự động failover khi primary down
public class ResilientDatabaseFactory
{
    private readonly string[] _connectionStrings;
    private int _currentPrimary = 0;

    public async Task<NpgsqlConnection> GetConnectionAsync(bool requireWrite = false, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < _connectionStrings.Length; attempt++)
        {
            var index = requireWrite 
                ? _currentPrimary 
                : (attempt + 1) % _connectionStrings.Length; // Round-robin replica
            
            try
            {
                var conn = new NpgsqlConnection(_connectionStrings[index]);
                await conn.OpenAsync(ct);
                return conn;
            }
            catch (NpgsqlException ex) when (ex.IsTransient)
            {
                if (requireWrite && attempt == 0)
                {
                    // Primary down - promote replica
                    _currentPrimary = (_currentPrimary + 1) % _connectionStrings.Length;
                }
                continue;
            }
        }
        
        throw new DatabaseUnavailableException("All database nodes are unavailable");
    }
}
```

### 6.2 Graceful Degradation

```csharp
// Degrade gracefully khi service không available
public class OrderServiceWithDegradation
{
    private readonly IOrderService _primary;
    private readonly IOrderCache _cache;
    private readonly IReadOnlyOrderService _readOnlyFallback;

    public async Task<OrderDto?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        // Try primary
        try
        {
            return await _primary.GetOrderAsync(id, ct);
        }
        catch (ServiceUnavailableException)
        {
            // Fallback to cache
            var cached = await _cache.GetAsync(id, ct);
            if (cached is not null)
            {
                cached.IsCachedData = true; // Mark as potentially stale
                return cached;
            }
        }
        
        // Last resort: read-only replica
        return await _readOnlyFallback.GetOrderAsync(id, ct);
    }
}
```

---

## 7. Testing Strategy cho Large Scale Systems

### 7.1 Load Testing

```javascript
// k6 load test script
import http from 'k6/http';
import { sleep, check } from 'k6';

export let options = {
    stages: [
        { duration: '2m', target: 100 },   // Ramp up
        { duration: '5m', target: 1000 },  // Sustain
        { duration: '2m', target: 5000 },  // Stress test
        { duration: '1m', target: 0 },     // Ramp down
    ],
    thresholds: {
        http_req_duration: ['p(95)<500'],   // 95% requests < 500ms
        http_req_failed: ['rate<0.01'],     // < 1% errors
    }
};

export default function() {
    let response = http.post('https://api.myapp.com/orders', 
        JSON.stringify({ customerId: '...', lines: [...] }),
        { headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ...' } });
    
    check(response, {
        'is status 202': (r) => r.status === 202,
        'has orderId': (r) => r.json().orderId !== null,
    });
    
    sleep(1);
}
```

### 7.2 Chaos Engineering

```csharp
// Chaos monkey: random service failures
public class ChaosMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IChaosSettings _settings;
    private readonly Random _random = new();

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (_settings.Enabled && _random.NextDouble() < _settings.FailureRate)
        {
            var chaosType = _settings.FailureTypes[_random.Next(_settings.FailureTypes.Count)];
            
            switch (chaosType)
            {
                case ChaosType.Latency:
                    await Task.Delay(TimeSpan.FromSeconds(_settings.LatencySeconds));
                    break;
                case ChaosType.Error:
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("Chaos monkey error");
                    return;
                case ChaosType.Timeout:
                    await Task.Delay(TimeSpan.FromMinutes(5)); // Never complete
                    return;
            }
        }
        
        await _next(ctx);
    }
}
```

---

## 8. Checklist cho System Design ở Scale

**Architecture**:
- [ ] Stateless services với externalized state
- [ ] Horizontal scaling cho mọi component
- [ ] Multi-region deployment với geo-routing
- [ ] Async event-driven communication cho non-critical paths
- [ ] Caching ở nhiều tầng: CDN, API Gateway, service, database

**Data**:
- [ ] Database sharding strategy từ đầu
- [ ] Read replicas cho read-heavy workloads
- [ ] Event sourcing cho audit-critical data
- [ ] Data archival strategy cho old data
- [ ] GDPR compliance: right to erasure, data locality

**Resilience**:
- [ ] Circuit breaker cho mọi external call
- [ ] Graceful degradation: app hoạt động khi service down
- [ ] Bulkhead: isolate resource cho critical vs non-critical
- [ ] Multi-region failover với automation
- [ ] Chaos engineering plan và regular GameDays

**Operations**:
- [ ] Full observability: metrics, traces, logs
- [ ] Runbook cho mọi alert
- [ ] On-call rotation và incident response process
- [ ] Capacity planning review hàng quý
- [ ] Load testing trước mọi major launch

**Cost**:
- [ ] Right-sizing: không over-provision
- [ ] Auto-scaling với correct thresholds
- [ ] Cost alerts và budget controls
- [ ] Spot/preemptible instances cho non-critical workloads
- [ ] Data tiering: hot/warm/cold storage
