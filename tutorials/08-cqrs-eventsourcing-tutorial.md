# CQRS & Event Sourcing at Scale: Xây dựng hệ thống audit-proof, time-travel-ready cho hàng triệu người dùng

## 1. Tại sao CQRS và Event Sourcing lại quan trọng ở quy mô lớn

Khi hệ thống của bạn chỉ có vài nghìn người dùng, một database bình thường với read/write chung là đủ. Nhưng khi scale lên hàng triệu người dùng, bạn bắt đầu đối mặt với một tập vấn đề khác nhau hoàn toàn:

- **Read và write có profile tải rất khác nhau**. Thường đọc nhiều hơn ghi 10-100 lần. Tại sao lại dùng cùng một database schema cho cả hai?
- **Audit trail là bắt buộc**. Fintech, healthcare, e-commerce đều cần biết "ai đã làm gì và khi nào". Nếu bạn chỉ lưu state hiện tại, bạn không có audit trail.
- **Domain logic trở nên phức tạp**. Một aggregate có 20 trường, cập nhật bởi 15 loại hành động khác nhau. Nếu nhét hết vào một UPDATE statement, bạn mất đi ý nghĩa domain.
- **Consistency requirements khác nhau giữa read và write**. Write cần strong consistency. Read có thể eventual consistent và trả về nhiều denormalized view cùng lúc.

**CQRS** (Command Query Responsibility Segregation) phân tách luồng ghi (command) và đọc (query) thành hai mô hình riêng biệt, tối ưu hóa từng phía một cách độc lập.

**Event Sourcing** đi xa hơn: thay vì lưu state hiện tại, bạn lưu chuỗi các sự kiện đã xảy ra. State là kết quả fold (reduce) của các event đó.

Kết hợp hai kỹ thuật này cho phép bạn:

1. Scale đọc và ghi độc lập
2. Có audit log hoàn chỉnh miễn phí
3. Rebuild bất kỳ read model nào từ lịch sử event
4. Debug bằng cách replay lại event đến một thời điểm bất kỳ (time travel debugging)
5. Publish integration event dễ dàng từ domain event đã lưu

---

## 2. Mental model: Command, Event, Projection

### 2.1 Command

Command là ý chí muốn thay đổi hệ thống. Nó chưa xảy ra, có thể bị từ chối.

```csharp
// Command: yêu cầu hành động
public record PlaceOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLine> Lines,
    string ShippingAddress);

public record ApproveOrderCommand(Guid OrderId, string ApprovedBy);

public record CancelOrderCommand(Guid OrderId, string Reason);
```

### 2.2 Domain Event

Event là sự kiện đã xảy ra. Nó bất biến, không thể bị từ chối vì nó đã xảy ra.

```csharp
// Event: những gì đã xảy ra
public record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLine> Lines,
    string ShippingAddress,
    decimal TotalAmount,
    DateTimeOffset PlacedAt);

public record OrderApproved(
    Guid OrderId,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

public record OrderCancelled(
    Guid OrderId,
    string Reason,
    DateTimeOffset CancelledAt);
```

### 2.3 Aggregate

Aggregate nhận command, validate business rules, và emit events.

```csharp
public class Order
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public IReadOnlyList<OrderLine> Lines { get; private set; } = [];
    public string? ShippingAddress { get; private set; }
    
    // Event stream - những gì chưa persist
    private readonly List<object> _pendingEvents = [];
    public IReadOnlyList<object> PendingEvents => _pendingEvents;

    // Tạo order mới
    public static Order Place(PlaceOrderCommand cmd)
    {
        if (!cmd.Lines.Any()) 
            throw new DomainException("Order must have at least one line");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) 
            throw new DomainException("Quantity must be positive");

        var order = new Order();
        order.Apply(new OrderPlaced(
            cmd.OrderId,
            cmd.CustomerId,
            cmd.Lines,
            cmd.ShippingAddress,
            cmd.Lines.Sum(l => l.Price * l.Quantity),
            DateTimeOffset.UtcNow));

        return order;
    }

    public void Approve(string approvedBy)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException($"Cannot approve order in status {Status}");

        Apply(new OrderApproved(Id, approvedBy, DateTimeOffset.UtcNow));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
            throw new DomainException("Order already cancelled");
        if (Status == OrderStatus.Shipped)
            throw new DomainException("Cannot cancel shipped order");

        Apply(new OrderCancelled(Id, reason, DateTimeOffset.UtcNow));
    }

    // Apply: thay đổi state dựa trên event
    private void Apply(OrderPlaced e)
    {
        Id = e.OrderId;
        CustomerId = e.CustomerId;
        Lines = e.Lines;
        ShippingAddress = e.ShippingAddress;
        TotalAmount = e.TotalAmount;
        Status = OrderStatus.Pending;
        _pendingEvents.Add(e);
    }

    private void Apply(OrderApproved e)
    {
        Status = OrderStatus.Approved;
        _pendingEvents.Add(e);
    }

    private void Apply(OrderCancelled e)
    {
        Status = OrderStatus.Cancelled;
        _pendingEvents.Add(e);
    }

    // Rebuild state từ event stream (khi load từ store)
    public static Order Rehydrate(IEnumerable<object> events)
    {
        var order = new Order();
        foreach (var e in events)
        {
            order.ApplyExisting(e);
        }
        return order;
    }

    private void ApplyExisting(object e)
    {
        switch (e)
        {
            case OrderPlaced placed: Apply(placed); break;
            case OrderApproved approved: Apply(approved); break;
            case OrderCancelled cancelled: Apply(cancelled); break;
        }
        _pendingEvents.Clear(); // events từ store không pending
    }
}
```

### 2.4 Projection / Read Model

Projection lắng nghe events và cập nhật read model tối ưu cho từng use case.

```csharp
// Read model cho danh sách đơn hàng của customer
public class OrderSummary
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

// Projector cập nhật read model
public class OrderSummaryProjection
{
    private readonly IOrderSummaryRepository _repo;

    public OrderSummaryProjection(IOrderSummaryRepository repo)
    {
        _repo = repo;
    }

    public async Task On(OrderPlaced e, CancellationToken ct)
    {
        var summary = new OrderSummary
        {
            Id = e.OrderId,
            CustomerId = e.CustomerId,
            Status = "Pending",
            TotalAmount = e.TotalAmount,
            ItemCount = e.Lines.Count,
            PlacedAt = e.PlacedAt
        };
        await _repo.InsertAsync(summary, ct);
    }

    public async Task On(OrderApproved e, CancellationToken ct)
    {
        await _repo.UpdateStatusAsync(e.OrderId, "Approved", e.ApprovedAt, ct);
    }

    public async Task On(OrderCancelled e, CancellationToken ct)
    {
        await _repo.UpdateStatusAsync(e.OrderId, "Cancelled", null, ct);
    }
}
```

---

## 3. Event Store: nơi lưu trữ sự kiện

Event store lưu các event như một append-only log theo từng stream (một stream per aggregate).

### 3.1 Event Store với Marten

Marten là lựa chọn tuyệt vời cho Event Sourcing trên PostgreSQL.

```csharp
// Program.cs - Setup Marten event store
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("Postgres")!);
    
    // Khai báo projections
    opts.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Async);
    opts.Projections.Add<CustomerOrdersProjection>(ProjectionLifecycle.Async);
    
    // Event serialization
    opts.UseSystemTextJsonForSerialization();
}).AddAsyncDaemon(DaemonMode.HotCold); // Hot/Cold failover cho daemon projection
```

```csharp
// Command handler: place order
public class PlaceOrderHandler
{
    private readonly IDocumentSession _session;

    public PlaceOrderHandler(IDocumentSession session)
    {
        _session = session;
    }

    public async Task<Guid> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Place(cmd);
        
        // Lưu events vào Marten event store
        _session.Events.StartStream<Order>(cmd.OrderId, order.PendingEvents.ToArray());
        await _session.SaveChangesAsync(ct);
        
        return cmd.OrderId;
    }
}

// Command handler: approve order
public class ApproveOrderHandler
{
    private readonly IDocumentSession _session;

    public ApproveOrderHandler(IDocumentSession session)
    {
        _session = session;
    }

    public async Task Handle(ApproveOrderCommand cmd, CancellationToken ct)
    {
        // Load aggregate từ event stream
        var order = await _session.Events.AggregateStreamAsync<Order>(cmd.OrderId, token: ct)
            ?? throw new NotFoundException($"Order {cmd.OrderId} not found");
        
        order.Approve(cmd.ApprovedBy);
        
        // Append new events
        _session.Events.Append(cmd.OrderId, order.PendingEvents.ToArray());
        await _session.SaveChangesAsync(ct);
    }
}
```

### 3.2 Optimistic Concurrency Control

Với Event Sourcing, version-based optimistic locking rất tự nhiên.

```csharp
public async Task Handle(ApproveOrderCommand cmd, CancellationToken ct)
{
    // Load với version tracking
    var stream = await _session.Events.FetchStreamStateAsync(cmd.OrderId, token: ct)
        ?? throw new NotFoundException($"Order {cmd.OrderId} not found");
    
    var order = await _session.Events.AggregateStreamAsync<Order>(cmd.OrderId, token: ct);
    order!.Approve(cmd.ApprovedBy);
    
    // Append với version check - nếu version đã thay đổi, throw optimistic concurrency exception
    _session.Events.Append(cmd.OrderId, stream.Version, order.PendingEvents.ToArray());
    
    try
    {
        await _session.SaveChangesAsync(ct);
    }
    catch (EventStreamUnexpectedMaxEventIdException)
    {
        throw new ConcurrencyException("Order was modified by another process. Please retry.");
    }
}
```

---

## 4. Tách biệt Command stack và Query stack

### 4.1 Command API

```csharp
// Endpoints chỉ nhận command, return minimal response
app.MapPost("/orders", async (
    PlaceOrderCommand cmd,
    PlaceOrderHandler handler,
    CancellationToken ct) =>
{
    var orderId = await handler.Handle(cmd, ct);
    return Results.Accepted($"/orders/{orderId}", new { orderId });
});

app.MapPost("/orders/{id}/approve", async (
    Guid id,
    ApproveOrderRequest req,
    ApproveOrderHandler handler,
    CancellationToken ct) =>
{
    await handler.Handle(new ApproveOrderCommand(id, req.ApprovedBy), ct);
    return Results.Accepted();
});
```

### 4.2 Query API (dùng read models)

```csharp
// Query endpoints đọc từ projected read models, không load aggregate
app.MapGet("/orders/{id}", async (
    Guid id,
    IQuerySession session,
    CancellationToken ct) =>
{
    // Đọc từ projected document, không phải event stream
    var summary = await session.LoadAsync<OrderSummary>(id, ct);
    return summary is null ? Results.NotFound() : Results.Ok(summary);
});

app.MapGet("/customers/{customerId}/orders", async (
    Guid customerId,
    IQuerySession session,
    int page,
    int pageSize,
    CancellationToken ct) =>
{
    var orders = await session.Query<OrderSummary>()
        .Where(o => o.CustomerId == customerId)
        .OrderByDescending(o => o.PlacedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);
    
    return Results.Ok(orders);
});
```

### 4.3 Event replay và time travel

```csharp
// Debug: xem state của order tại một thời điểm cụ thể
app.MapGet("/orders/{id}/state-at", async (
    Guid id,
    DateTimeOffset at,
    IQuerySession session,
    CancellationToken ct) =>
{
    // Marten hỗ trợ aggregate tại một timestamp
    var state = await session.Events.AggregateStreamAsync<Order>(
        id,
        timestamp: at,
        token: ct);
    
    return state is null ? Results.NotFound() : Results.Ok(new
    {
        state.Id,
        state.Status,
        state.TotalAmount,
        AsOf = at
    });
});

// Admin: rebuild all projections từ event store
app.MapPost("/admin/rebuild-projections", async (
    IProjectionCoordinator coordinator,
    CancellationToken ct) =>
{
    await coordinator.RebuildAsync<OrderSummaryProjection>(ct);
    return Results.Accepted();
}).RequireAuthorization("Admin");
```

---

## 5. Snapshotting: tránh replay quá nhiều event

Khi một aggregate có hàng nghìn events, replay mỗi lần request là chậm. Snapshotting giải quyết vấn đề này.

```csharp
// Cấu hình snapshot tự động sau mỗi 50 events
opts.Events.AddSnapshotting<Order>(snapshotEvery: 50);

// Hoặc tự viết snapshot logic
public class OrderSnapshotPolicy : ISnapshotPolicy
{
    public bool ShouldSnapshot(IReadOnlyEventStream stream, IReadOnlyList<IEvent> events)
    {
        // Snapshot khi stream dài hơn 100 events hoặc cứ 1 giờ
        return stream.Count > 100 || 
               (stream.LastTimestamp is not null && 
                DateTimeOffset.UtcNow - stream.LastTimestamp > TimeSpan.FromHours(1));
    }
}
```

---

## 6. Integration event: nối event sourcing với hệ thống bên ngoài

Domain event được lưu trong event store. Integration event được publish ra message bus để các service khác lắng nghe.

```csharp
// Outbox pattern: publish integration event khi projection chạy
public class OrderIntegrationEventProjection : IProjection
{
    private readonly IMessageBus _bus;

    public OrderIntegrationEventProjection(IMessageBus bus)
    {
        _bus = bus;
    }

    public async Task ApplyAsync(IDocumentOperations ops, IReadOnlyList<StreamAction> streams, CancellationToken ct)
    {
        foreach (var stream in streams)
        foreach (var @event in stream.Events)
        {
            switch (@event.Data)
            {
                case OrderPlaced e:
                    await _bus.PublishAsync(new OrderPlacedIntegrationEvent(e.OrderId, e.CustomerId, e.TotalAmount), ct);
                    break;
                case OrderApproved e:
                    await _bus.PublishAsync(new OrderApprovedIntegrationEvent(e.OrderId), ct);
                    break;
            }
        }
    }
}
```

---

## 7. Scale: multiple read models cho multiple consumers

CQRS cho phép bạn có nhiều read model tối ưu cho từng use case mà không ảnh hưởng đến nhau.

```text
                    ┌─────────────────────────────┐
                    │         Event Store          │
                    │    (PostgreSQL / EventDB)     │
                    └──────────┬──────────────────┘
                               │
                    ┌──────────▼──────────────────┐
                    │    Async Daemon / Consumer    │
                    └───────┬────────┬─────────────┘
                            │        │
              ┌─────────────▼──┐   ┌─▼─────────────────┐
              │  OrderSummary  │   │  CustomerTimeline  │
              │  (PostgreSQL)  │   │  (Elasticsearch)   │
              └────────────────┘   └───────────────────┘
                            │
              ┌─────────────▼──────────────┐
              │  AnalyticsDailySalesView   │
              │  (ClickHouse / BigQuery)   │
              └────────────────────────────┘
```

```csharp
// Read model cho analytics, denormalized hoàn toàn
public class DailySalesProjection : IProjection
{
    public async Task ApplyAsync(IDocumentOperations ops, IReadOnlyList<StreamAction> streams, CancellationToken ct)
    {
        var placedEvents = streams
            .SelectMany(s => s.Events)
            .Where(e => e.Data is OrderPlaced)
            .Select(e => (OrderPlaced)e.Data)
            .GroupBy(e => e.PlacedAt.Date);

        foreach (var group in placedEvents)
        {
            var date = group.Key;
            var record = await ops.LoadAsync<DailySalesRecord>(date.ToString("yyyy-MM-dd"), ct)
                ?? new DailySalesRecord { Date = date };
            
            record.OrderCount += group.Count();
            record.TotalRevenue += group.Sum(e => e.TotalAmount);
            
            ops.Store(record);
        }
    }
}
```

---

## 8. Checklist production cho CQRS + Event Sourcing

- [ ] Đặt tên event theo **past tense** (`OrderPlaced`, không phải `PlaceOrder`)
- [ ] Event payload phải **self-contained** - không reference tới state ngoài event
- [ ] Không bao giờ sửa event đã lưu - chỉ append
- [ ] Version event schema khi cần upgrade: thêm `_v2` suffix hoặc dùng upcasting
- [ ] Snapshot khi stream có nhiều hơn 100-200 events
- [ ] Projection phải **idempotent** - replay không gây lỗi
- [ ] Tách riêng event store database và read model database nếu read scale cần thiết
- [ ] Monitor projection lag - nếu async projection tụt hậu quá nhiều, cần alert
- [ ] Đặt event position checkpoint rõ ràng để restart projection không replay từ đầu
- [ ] Expose `/events/{streamId}` endpoint cho admin debugging
