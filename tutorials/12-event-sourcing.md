# Event Sourcing với .NET và Marten

## Mục Lục
1. [Event Sourcing là gì?](#event-sourcing-la-gi)
2. [Events as Source of Truth](#events-as-source-of-truth)
3. [Aggregate Design](#aggregate-design)
4. [Event Store vs Traditional DB](#event-store-vs-traditional-db)
5. [Marten Setup](#marten-setup)
6. [Complete Sample: OrderAggregate](#complete-sample)
7. [Projections và Read Models](#projections-va-read-models)
8. [Snapshots](#snapshots)
9. [Eventual Consistency](#eventual-consistency)
10. [Best Practices](#best-practices)

---

## 1. Event Sourcing là gì?

Event Sourcing là một pattern lưu trữ dữ liệu bằng cách ghi lại **chuỗi các events** thay vì trạng thái hiện tại. Hệ thống có thể tái tạo (replay) trạng thái hiện tại bằng cách apply tất cả events theo thứ tự.

### So sánh: Traditional vs Event Sourcing

```
TRADITIONAL APPROACH (CRUD):
┌────────────────────────────────────────────────────────┐
│  Time    │ Action              │ DB State               │
├──────────┼─────────────────────┼────────────────────────┤
│  T1      │ Create Order        │ {status: "Pending"}    │
│  T2      │ Confirm Order       │ {status: "Confirmed"}  │ ← Mất lịch sử T1
│  T3      │ Add Item            │ {status: "Confirmed",  │ ← Không biết ai thêm
│          │                     │  items: [...]}         │
│  T4      │ Ship Order          │ {status: "Shipped"}    │ ← Không biết khi nào confirm
└────────────────────────────────────────────────────────┘

EVENT SOURCING APPROACH:
┌────────────────────────────────────────────────────────┐
│  Seq │ Event                 │ Payload                  │
├──────┼───────────────────────┼──────────────────────────┤
│  1   │ OrderPlaced           │ {customerId, items, ...} │
│  2   │ OrderConfirmed        │ {confirmedBy, at}        │
│  3   │ ItemAdded             │ {productId, quantity}    │
│  4   │ OrderShipped          │ {trackingNumber, carrier}│
│  5   │ OrderDelivered        │ {deliveredAt, signature} │
└────────────────────────────────────────────────────────┘

Current State = Apply(Event1) + Apply(Event2) + ... + Apply(EventN)
```

### Lợi ích của Event Sourcing:
- **Complete audit trail** - Biết chính xác điều gì xảy ra, khi nào, bởi ai
- **Time travel** - Xem trạng thái tại bất kỳ thời điểm nào trong quá khứ
- **Event replay** - Replay events để debug hoặc fix bugs
- **Multiple projections** - Tạo nhiều read models từ cùng event stream
- **Event-driven integration** - Events tự nhiên trigger other services

### Thách thức:
- Querying phức tạp hơn (cần projections)
- Eventually consistent read models
- Schema evolution (thay đổi event structure)
- Event versioning
- Performance (replay nhiều events)

---

## 2. Events as Source of Truth

```csharp
// Base interface cho tất cả domain events
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    string EventType { get; }
    int SchemaVersion { get; }
}

// Base record cho events (immutable)
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public abstract string EventType { get; }
    public virtual int SchemaVersion => 1;
}

// Order Events - mỗi event capture một business fact
public record OrderPlaced : DomainEvent
{
    public override string EventType => "order.placed";
    
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerEmail { get; init; }
    public required IReadOnlyList<OrderItemData> Items { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required AddressData ShippingAddress { get; init; }
    public string? CouponCode { get; init; }
    public decimal? DiscountAmount { get; init; }
}

public record OrderConfirmed : DomainEvent
{
    public override string EventType => "order.confirmed";
    
    public required Guid OrderId { get; init; }
    public required Guid ConfirmedBy { get; init; }  // UserId hoặc SystemId
    public required string ConfirmationNote { get; init; }
    public required decimal FinalAmount { get; init; }
}

public record PaymentProcessed : DomainEvent
{
    public override string EventType => "order.payment_processed";
    
    public required Guid OrderId { get; init; }
    public required Guid TransactionId { get; init; }
    public required string PaymentMethod { get; init; }  // "CreditCard", "MoMo", "VNPay"
    public required decimal AmountPaid { get; init; }
    public required string Currency { get; init; }
    public required string PaymentGatewayReference { get; init; }
}

public record InventoryReserved : DomainEvent
{
    public override string EventType => "order.inventory_reserved";
    
    public required Guid OrderId { get; init; }
    public required IReadOnlyList<ReservationData> Reservations { get; init; }
    public required DateTime ReservedUntil { get; init; }
}

public record OrderShipped : DomainEvent
{
    public override string EventType => "order.shipped";
    
    public required Guid OrderId { get; init; }
    public required string TrackingNumber { get; init; }
    public required string Carrier { get; init; }
    public required DateTime EstimatedDelivery { get; init; }
    public required AddressData ShippingFrom { get; init; }
}

public record OrderDelivered : DomainEvent
{
    public override string EventType => "order.delivered";
    
    public required Guid OrderId { get; init; }
    public required DateTime DeliveredAt { get; init; }
    public string? ReceivedBy { get; init; }
    public string? DeliveryNote { get; init; }
}

public record OrderCancelled : DomainEvent
{
    public override string EventType => "order.cancelled";
    
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required Guid CancelledBy { get; init; }
    public required string CancellationSource { get; init; }  // "Customer", "Admin", "System"
    public bool RefundRequired { get; init; }
}

public record OrderRefunded : DomainEvent
{
    public override string EventType => "order.refunded";
    
    public required Guid OrderId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string RefundReason { get; init; }
    public required string RefundMethod { get; init; }
    public required Guid RefundTransactionId { get; init; }
}

// Value objects trong events
public record OrderItemData(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal SubTotal);

public record AddressData(
    string Street,
    string City,
    string Province,
    string PostalCode,
    string Country);

public record ReservationData(
    Guid ProductId,
    int ReservedQuantity,
    Guid WarehouseId);
```

---

## 3. Aggregate Design

```csharp
// Order Aggregate - business logic và state management
public class Order
{
    // Stream ID trong event store
    public Guid Id { get; private set; }
    public int Version { get; private set; }
    
    // Current state (rebuilt từ events)
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderItem> Items { get; private set; } = new List<OrderItem>();
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public Address? ShippingAddress { get; private set; }
    public PaymentInfo? Payment { get; private set; }
    public ShipmentInfo? Shipment { get; private set; }
    public DateTime PlacedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    
    // Private constructor - chỉ tạo qua factory hoặc event replay
    private Order() { }

    // ==================== Factory Methods ====================

    // Tạo order mới, trả về events
    public static (Order Order, IReadOnlyList<IDomainEvent> Events) Place(
        Guid orderId,
        Guid customerId,
        string customerEmail,
        IEnumerable<(Guid ProductId, string Name, int Qty, decimal Price)> items,
        Address shippingAddress,
        string? couponCode = null)
    {
        // Validate business rules
        if (!items.Any())
            throw new DomainException("Order must have at least one item");

        var itemList = items.ToList();
        var totalAmount = itemList.Sum(i => i.Qty * i.Price);

        var @event = new OrderPlaced
        {
            OrderId = orderId,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Items = itemList.Select(i => new OrderItemData(
                i.ProductId, i.Name, i.Qty, i.Price, i.Qty * i.Price)).ToList(),
            TotalAmount = totalAmount,
            Currency = "VND",
            ShippingAddress = new AddressData(
                shippingAddress.Street,
                shippingAddress.City,
                shippingAddress.Province,
                shippingAddress.PostalCode,
                shippingAddress.Country),
            CouponCode = couponCode
        };

        var order = new Order();
        order.Apply(@event);

        return (order, new[] { @event });
    }

    // ==================== Commands ====================

    public IReadOnlyList<IDomainEvent> Confirm(Guid confirmedBy, string note = "")
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException($"Cannot confirm order in status {Status}");

        var @event = new OrderConfirmed
        {
            OrderId = Id,
            ConfirmedBy = confirmedBy,
            ConfirmationNote = note,
            FinalAmount = TotalAmount
        };

        Apply(@event);
        return new[] { @event };
    }

    public IReadOnlyList<IDomainEvent> ProcessPayment(
        Guid transactionId,
        string paymentMethod,
        decimal amountPaid,
        string gatewayRef)
    {
        if (Status != OrderStatus.Confirmed)
            throw new DomainException("Payment can only be processed for confirmed orders");

        if (amountPaid < TotalAmount)
            throw new DomainException($"Payment amount {amountPaid} is less than order total {TotalAmount}");

        var @event = new PaymentProcessed
        {
            OrderId = Id,
            TransactionId = transactionId,
            PaymentMethod = paymentMethod,
            AmountPaid = amountPaid,
            Currency = Currency,
            PaymentGatewayReference = gatewayRef
        };

        Apply(@event);
        return new[] { @event };
    }

    public IReadOnlyList<IDomainEvent> ReserveInventory(
        IEnumerable<(Guid ProductId, int Quantity, Guid WarehouseId)> reservations)
    {
        if (Status != OrderStatus.PaymentProcessed)
            throw new DomainException("Can only reserve inventory after payment");

        var @event = new InventoryReserved
        {
            OrderId = Id,
            Reservations = reservations.Select(r => new ReservationData(
                r.ProductId, r.Quantity, r.WarehouseId)).ToList(),
            ReservedUntil = DateTime.UtcNow.AddHours(24)
        };

        Apply(@event);
        return new[] { @event };
    }

    public IReadOnlyList<IDomainEvent> Ship(
        string trackingNumber,
        string carrier,
        DateTime estimatedDelivery,
        Address shippingFrom)
    {
        if (Status != OrderStatus.InventoryReserved)
            throw new DomainException("Can only ship orders with reserved inventory");

        var @event = new OrderShipped
        {
            OrderId = Id,
            TrackingNumber = trackingNumber,
            Carrier = carrier,
            EstimatedDelivery = estimatedDelivery,
            ShippingFrom = new AddressData(
                shippingFrom.Street, shippingFrom.City,
                shippingFrom.Province, shippingFrom.PostalCode,
                shippingFrom.Country)
        };

        Apply(@event);
        return new[] { @event };
    }

    public IReadOnlyList<IDomainEvent> MarkDelivered(
        DateTime deliveredAt,
        string? receivedBy = null,
        string? note = null)
    {
        if (Status != OrderStatus.Shipped)
            throw new DomainException("Can only deliver shipped orders");

        var @event = new OrderDelivered
        {
            OrderId = Id,
            DeliveredAt = deliveredAt,
            ReceivedBy = receivedBy,
            DeliveryNote = note
        };

        Apply(@event);
        return new[] { @event };
    }

    public IReadOnlyList<IDomainEvent> Cancel(Guid cancelledBy, string reason, string source)
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel shipped or delivered order");

        var events = new List<IDomainEvent>();

        var cancelEvent = new OrderCancelled
        {
            OrderId = Id,
            Reason = reason,
            CancelledBy = cancelledBy,
            CancellationSource = source,
            RefundRequired = Payment is not null  // Hoàn tiền nếu đã thanh toán
        };
        events.Add(cancelEvent);

        // Tự động tạo refund event nếu cần
        if (Payment is not null)
        {
            var refundEvent = new OrderRefunded
            {
                OrderId = Id,
                RefundAmount = Payment.AmountPaid,
                RefundReason = $"Order cancelled: {reason}",
                RefundMethod = Payment.PaymentMethod,
                RefundTransactionId = Guid.NewGuid()
            };
            events.Add(refundEvent);
        }

        foreach (var @event in events)
            Apply(@event);

        return events;
    }

    // ==================== Event Appliers (When methods) ====================
    // Apply thay đổi state, KHÔNG throw exceptions

    private void Apply(IDomainEvent @event)
    {
        Version++;
        switch (@event)
        {
            case OrderPlaced e:      ApplyEvent(e); break;
            case OrderConfirmed e:   ApplyEvent(e); break;
            case PaymentProcessed e: ApplyEvent(e); break;
            case InventoryReserved e: ApplyEvent(e); break;
            case OrderShipped e:     ApplyEvent(e); break;
            case OrderDelivered e:   ApplyEvent(e); break;
            case OrderCancelled e:   ApplyEvent(e); break;
            case OrderRefunded e:    ApplyEvent(e); break;
        }
    }

    private void ApplyEvent(OrderPlaced e)
    {
        Id = e.OrderId;
        CustomerId = e.CustomerId;
        Status = OrderStatus.Pending;
        Items = e.Items.Select(i => new OrderItem(
            i.ProductId, i.ProductName, i.Quantity, i.UnitPrice)).ToList();
        TotalAmount = e.TotalAmount;
        Currency = e.Currency;
        PlacedAt = e.OccurredAt;
        ShippingAddress = new Address(
            e.ShippingAddress.Street,
            e.ShippingAddress.City,
            e.ShippingAddress.Province,
            e.ShippingAddress.PostalCode,
            e.ShippingAddress.Country);
    }

    private void ApplyEvent(OrderConfirmed e)
    {
        Status = OrderStatus.Confirmed;
        ConfirmedAt = e.OccurredAt;
    }

    private void ApplyEvent(PaymentProcessed e)
    {
        Status = OrderStatus.PaymentProcessed;
        Payment = new PaymentInfo(
            e.TransactionId,
            e.PaymentMethod,
            e.AmountPaid,
            e.OccurredAt);
    }

    private void ApplyEvent(InventoryReserved e)
    {
        Status = OrderStatus.InventoryReserved;
    }

    private void ApplyEvent(OrderShipped e)
    {
        Status = OrderStatus.Shipped;
        ShippedAt = e.OccurredAt;
        Shipment = new ShipmentInfo(e.TrackingNumber, e.Carrier, e.EstimatedDelivery);
    }

    private void ApplyEvent(OrderDelivered e)
    {
        Status = OrderStatus.Delivered;
        DeliveredAt = e.DeliveredAt;
    }

    private void ApplyEvent(OrderCancelled e)
    {
        Status = OrderStatus.Cancelled;
        CancelledAt = e.OccurredAt;
        CancellationReason = e.Reason;
    }

    private void ApplyEvent(OrderRefunded _) { }

    // ==================== Rebuild từ events (Event Sourcing core) ====================

    public static Order Rebuild(IEnumerable<IDomainEvent> events)
    {
        var order = new Order();
        foreach (var @event in events)
        {
            order.Apply(@event);
        }
        return order;
    }
}

// Supporting value objects và records
public enum OrderStatus
{
    Pending,
    Confirmed,
    PaymentProcessed,
    InventoryReserved,
    Shipped,
    Delivered,
    Cancelled,
    Refunded
}

public record OrderItem(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice)
{
    public decimal SubTotal => Quantity * UnitPrice;
}

public record Address(string Street, string City, string Province, string PostalCode, string Country);

public record PaymentInfo(
    Guid TransactionId,
    string PaymentMethod,
    decimal AmountPaid,
    DateTime ProcessedAt);

public record ShipmentInfo(
    string TrackingNumber,
    string Carrier,
    DateTime EstimatedDelivery);
```

---

## 4. Marten Setup

```csharp
// Cài đặt packages
// dotnet add package Marten
// dotnet add package Marten.AsyncDaemon (cho projections)

// Program.cs
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(options =>
{
    // Connection string
    options.Connection(builder.Configuration.GetConnectionString("EventStore")!);
    
    // Schema
    options.DatabaseSchemaName = "event_store";
    
    // Stream identity
    options.Events.StreamIdentity = StreamIdentity.AsGuid;
    
    // Event type mappings
    options.Events.AddEventType<OrderPlaced>();
    options.Events.AddEventType<OrderConfirmed>();
    options.Events.AddEventType<PaymentProcessed>();
    options.Events.AddEventType<InventoryReserved>();
    options.Events.AddEventType<OrderShipped>();
    options.Events.AddEventType<OrderDelivered>();
    options.Events.AddEventType<OrderCancelled>();
    options.Events.AddEventType<OrderRefunded>();
    
    // Projections
    options.Projections.Add<OrderProjection>(ProjectionLifecycle.Inline);
    options.Projections.Add<CustomerOrderSummaryProjection>(ProjectionLifecycle.Async);
    options.Projections.Add<DailyOrderReportProjection>(ProjectionLifecycle.Async);
    
    // Snapshot configuration
    options.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);
    
    // Serializer
    options.UseSystemTextJsonForSerialization(configure: serializer =>
    {
        serializer.Converters.Add(new JsonStringEnumConverter());
    });
    
    // Auto-create schema (dev/test only)
    if (builder.Environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
})
.AddAsyncDaemon(DaemonMode.HotCold)  // Background daemon cho async projections
.UseLightweightSessions();            // Optimized sessions

// Order Repository
builder.Services.AddScoped<IOrderRepository, MartenOrderRepository>();

var app = builder.Build();

// Apply migrations
await using var scope = app.Services.CreateAsyncScope();
var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
```

---

## 5. Complete Sample: OrderAggregate với Marten

```csharp
// Repository Implementation
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct = default);
    Task<Order?> GetByIdAtVersionAsync(Guid orderId, long version, CancellationToken ct = default);
    Task SaveAsync(Guid orderId, IReadOnlyList<IDomainEvent> events, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> GetEventsAfterAsync(
        Guid orderId, long afterVersion, CancellationToken ct = default);
}

public class MartenOrderRepository : IOrderRepository
{
    private readonly IDocumentSession _session;
    private readonly ILogger<MartenOrderRepository> _logger;

    public MartenOrderRepository(IDocumentSession session, ILogger<MartenOrderRepository> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        // Marten tự rebuild aggregate từ events
        var order = await _session.Events.AggregateStreamAsync<Order>(orderId, token: ct);
        
        if (order is null)
        {
            _logger.LogDebug("Order {OrderId} not found", orderId);
            return null;
        }
        
        _logger.LogDebug("Loaded order {OrderId} at version {Version}", orderId, order.Version);
        return order;
    }

    public async Task<Order?> GetByIdAtVersionAsync(
        Guid orderId,
        long version,
        CancellationToken ct = default)
    {
        // Time travel - lấy state tại version cụ thể
        return await _session.Events.AggregateStreamAsync<Order>(
            orderId,
            version: version,
            token: ct);
    }

    public async Task SaveAsync(
        Guid orderId,
        IReadOnlyList<IDomainEvent> events,
        CancellationToken ct = default)
    {
        if (!events.Any()) return;
        
        // Append events to stream
        _session.Events.Append(orderId, events.Cast<object>().ToArray());
        
        await _session.SaveChangesAsync(ct);
        
        _logger.LogInformation(
            "Appended {EventCount} events to order stream {OrderId}",
            events.Count, orderId);
    }

    public async Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var stream = await _session.Events.FetchStreamAsync(orderId, token: ct);
        return stream.Select(e => (IDomainEvent)e.Data).ToList();
    }

    public async Task<IReadOnlyList<IDomainEvent>> GetEventsAfterAsync(
        Guid orderId,
        long afterVersion,
        CancellationToken ct = default)
    {
        var events = await _session.Events.FetchStreamAsync(orderId, fromVersion: afterVersion + 1, token: ct);
        return events.Select(e => (IDomainEvent)e.Data).ToList();
    }
}

// Application Service (Command Handlers)
public class OrderCommandService
{
    private readonly IOrderRepository _repository;
    private readonly IProductServiceClient _productClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderCommandService> _logger;

    public OrderCommandService(
        IOrderRepository repository,
        IProductServiceClient productClient,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderCommandService> logger)
    {
        _repository = repository;
        _productClient = productClient;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Guid> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        // Validate products exist và có stock
        var productTasks = command.Items.Select(i =>
            _productClient.GetProductAsync(i.ProductId, ct));
        var products = await Task.WhenAll(productTasks);

        var missingProducts = command.Items
            .Select((item, idx) => (item, product: products[idx]))
            .Where(x => x.product is null)
            .Select(x => x.item.ProductId)
            .ToList();

        if (missingProducts.Any())
            throw new DomainException($"Products not found: {string.Join(", ", missingProducts)}");

        // Prepare items với actual prices từ product service
        var items = command.Items.Select((item, idx) => (
            ProductId: item.ProductId,
            Name: products[idx]!.Name,
            Qty: item.Quantity,
            Price: products[idx]!.Price
        ));

        // Create aggregate
        var orderId = Guid.NewGuid();
        var (order, events) = Order.Place(
            orderId,
            command.CustomerId,
            command.CustomerEmail,
            items,
            command.ShippingAddress,
            command.CouponCode);

        // Save events
        await _repository.SaveAsync(orderId, events, ct);

        // Publish integration events
        foreach (var @event in events.OfType<OrderPlaced>())
        {
            await _publishEndpoint.Publish(@event, ct);
        }

        _logger.LogInformation("Order {OrderId} placed by customer {CustomerId}", orderId, command.CustomerId);
        return orderId;
    }

    public async Task ConfirmOrderAsync(ConfirmOrderCommand command, CancellationToken ct)
    {
        var order = await _repository.GetByIdAsync(command.OrderId, ct)
            ?? throw new OrderNotFoundException(command.OrderId);

        var events = order.Confirm(command.ConfirmedBy, command.Note);
        await _repository.SaveAsync(command.OrderId, events, ct);

        foreach (var @event in events)
            await _publishEndpoint.Publish(@event, ct);
    }

    public async Task ProcessPaymentAsync(ProcessPaymentCommand command, CancellationToken ct)
    {
        var order = await _repository.GetByIdAsync(command.OrderId, ct)
            ?? throw new OrderNotFoundException(command.OrderId);

        var events = order.ProcessPayment(
            command.TransactionId,
            command.PaymentMethod,
            command.AmountPaid,
            command.GatewayReference);

        await _repository.SaveAsync(command.OrderId, events, ct);

        foreach (var @event in events)
            await _publishEndpoint.Publish(@event, ct);
    }

    public async Task CancelOrderAsync(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await _repository.GetByIdAsync(command.OrderId, ct)
            ?? throw new OrderNotFoundException(command.OrderId);

        var events = order.Cancel(command.CancelledBy, command.Reason, command.Source);
        await _repository.SaveAsync(command.OrderId, events, ct);

        foreach (var @event in events)
            await _publishEndpoint.Publish(@event, ct);
    }

    // Time travel - xem order tại điểm trong quá khứ
    public async Task<Order?> GetOrderAtVersionAsync(Guid orderId, long version, CancellationToken ct)
    {
        return await _repository.GetByIdAtVersionAsync(orderId, version, ct);
    }
}
```

---

## 6. Projections và Read Models

```csharp
// Inline Projection - cập nhật đồng bộ khi event append
public class OrderProjection : SingleStreamProjection<OrderReadModel>
{
    public OrderProjection()
    {
        // Map events to projector methods
    }

    public OrderReadModel Create(OrderPlaced e)
    {
        return new OrderReadModel
        {
            Id = e.OrderId,
            CustomerId = e.CustomerId,
            CustomerEmail = e.CustomerEmail,
            Status = OrderStatus.Pending.ToString(),
            TotalAmount = e.TotalAmount,
            Currency = e.Currency,
            Items = e.Items.Select(i => new OrderItemReadModel
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                SubTotal = i.SubTotal
            }).ToList(),
            ShippingAddress = $"{e.ShippingAddress.Street}, {e.ShippingAddress.City}",
            PlacedAt = e.OccurredAt,
            LastUpdatedAt = e.OccurredAt
        };
    }

    public void Apply(OrderConfirmed e, OrderReadModel order)
    {
        order.Status = OrderStatus.Confirmed.ToString();
        order.ConfirmedAt = e.OccurredAt;
        order.LastUpdatedAt = e.OccurredAt;
    }

    public void Apply(PaymentProcessed e, OrderReadModel order)
    {
        order.Status = OrderStatus.PaymentProcessed.ToString();
        order.PaymentMethod = e.PaymentMethod;
        order.PaymentTransactionId = e.TransactionId.ToString();
        order.PaidAt = e.OccurredAt;
        order.LastUpdatedAt = e.OccurredAt;
    }

    public void Apply(OrderShipped e, OrderReadModel order)
    {
        order.Status = OrderStatus.Shipped.ToString();
        order.TrackingNumber = e.TrackingNumber;
        order.Carrier = e.Carrier;
        order.EstimatedDelivery = e.EstimatedDelivery;
        order.ShippedAt = e.OccurredAt;
        order.LastUpdatedAt = e.OccurredAt;
    }

    public void Apply(OrderDelivered e, OrderReadModel order)
    {
        order.Status = OrderStatus.Delivered.ToString();
        order.DeliveredAt = e.DeliveredAt;
        order.LastUpdatedAt = e.OccurredAt;
    }

    public void Apply(OrderCancelled e, OrderReadModel order)
    {
        order.Status = OrderStatus.Cancelled.ToString();
        order.CancellationReason = e.Reason;
        order.CancelledAt = e.OccurredAt;
        order.LastUpdatedAt = e.OccurredAt;
    }
}

// Read Model document
public class OrderReadModel
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public List<OrderItemReadModel> Items { get; set; } = new();
    public string ShippingAddress { get; set; } = "";
    public string? PaymentMethod { get; set; }
    public string? PaymentTransactionId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}

public record OrderItemReadModel
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }
}

// Async Multi-Stream Projection - aggregate across multiple streams
public class CustomerOrderSummaryProjection : MultiStreamProjection<CustomerOrderSummary, Guid>
{
    public CustomerOrderSummaryProjection()
    {
        // Group events by CustomerId
        Identity<OrderPlaced>(e => e.CustomerId);
        Identity<OrderConfirmed>(e => e.OrderId);  // Cần lookup
        Identity<OrderCancelled>(e => e.OrderId);  // Cần lookup
        Identity<OrderDelivered>(e => e.OrderId);  // Cần lookup
    }

    public CustomerOrderSummary Create(OrderPlaced e)
    {
        return new CustomerOrderSummary
        {
            Id = e.CustomerId,
            CustomerId = e.CustomerId,
            TotalOrders = 1,
            PendingOrders = 1,
            TotalSpent = 0, // Chưa thanh toán
            LastOrderAt = e.OccurredAt
        };
    }

    public void Apply(OrderPlaced e, CustomerOrderSummary summary)
    {
        if (summary.Id != Guid.Empty)  // Đã tồn tại
        {
            summary.TotalOrders++;
            summary.PendingOrders++;
            if (e.OccurredAt > summary.LastOrderAt)
                summary.LastOrderAt = e.OccurredAt;
        }
    }

    public void Apply(PaymentProcessed e, CustomerOrderSummary summary)
    {
        summary.TotalSpent += e.AmountPaid;
        summary.PendingOrders = Math.Max(0, summary.PendingOrders - 1);
    }

    public void Apply(OrderDelivered e, CustomerOrderSummary summary)
    {
        summary.CompletedOrders++;
    }

    public void Apply(OrderCancelled e, CustomerOrderSummary summary)
    {
        summary.CancelledOrders++;
        summary.PendingOrders = Math.Max(0, summary.PendingOrders - 1);
    }
}

public class CustomerOrderSummary
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime LastOrderAt { get; set; }
}

// Query Side
public class OrderQueryService
{
    private readonly IQuerySession _session;

    public OrderQueryService(IQuerySession session)
    {
        _session = session;
    }

    public async Task<OrderReadModel?> GetOrderAsync(Guid orderId, CancellationToken ct)
    {
        return await _session.LoadAsync<OrderReadModel>(orderId, ct);
    }

    public async Task<IReadOnlyList<OrderReadModel>> GetCustomerOrdersAsync(
        Guid customerId,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20)
    {
        return await _session.Query<OrderReadModel>()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrderReadModel>> GetOrdersByStatusAsync(
        string status,
        CancellationToken ct)
    {
        return await _session.Query<OrderReadModel>()
            .Where(o => o.Status == status)
            .OrderBy(o => o.PlacedAt)
            .ToListAsync(ct);
    }

    // Event history cho một order
    public async Task<IReadOnlyList<EventEnvelope>> GetOrderHistoryAsync(
        Guid orderId,
        CancellationToken ct)
    {
        var events = await _session.Events.FetchStreamAsync(orderId, token: ct);
        return events.Select(e => new EventEnvelope(
            e.Id,
            e.EventType.Name,
            e.Version,
            e.Timestamp,
            e.Data)).ToList();
    }
}

public record EventEnvelope(
    Guid EventId,
    string EventType,
    long Version,
    DateTimeOffset Timestamp,
    object Data);
```

---

## 7. Snapshots

```csharp
// Snapshot configuration
options.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);

// Custom snapshot strategy
public class OrderSnapshotStrategy : ISnapshotStrategy
{
    // Snapshot mỗi 50 events
    public bool ShouldSnapshot(IEventStream eventStream)
    {
        return eventStream.Events.Count % 50 == 0;
    }
}

// Manual snapshot loading với fallback
public class OptimizedOrderRepository : IOrderRepository
{
    private readonly IDocumentSession _session;

    public OptimizedOrderRepository(IDocumentSession session)
    {
        _session = session;
    }

    public async Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct)
    {
        // Marten tự động dùng snapshot nếu có,
        // sau đó apply các events sau snapshot
        return await _session.Events.AggregateStreamAsync<Order>(orderId, token: ct);
    }

    // Load chỉ events từ version cụ thể (sau snapshot)
    public async Task<Order?> GetOptimizedAsync(Guid orderId, CancellationToken ct)
    {
        // Tải snapshot nếu có
        var snapshot = await _session.LoadAsync<OrderSnapshot>(orderId, ct);
        
        if (snapshot is null)
        {
            // Không có snapshot, load toàn bộ events
            return await _session.Events.AggregateStreamAsync<Order>(orderId, token: ct);
        }
        
        // Có snapshot, chỉ load events sau snapshot version
        var newEvents = await _session.Events.FetchStreamAsync(
            orderId,
            fromVersion: snapshot.Version + 1,
            token: ct);
        
        // Rebuild từ snapshot + new events
        var order = snapshot.ToOrder();
        foreach (var @event in newEvents)
        {
            // Apply events...
        }
        
        return order;
    }
}

// Snapshot document
public class OrderSnapshot
{
    public Guid Id { get; set; }
    public long Version { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    // ... other state

    public Order ToOrder()
    {
        // Reconstruct Order từ snapshot
        return Order.Rebuild(new List<IDomainEvent>()); // Simplified
    }
}
```

---

## 8. Event Schema Evolution

```csharp
// Upcasting - convert old event format to new
public class OrderPlacedV2Upcaster : IEventUpcaster<OrderPlacedV1, OrderPlaced>
{
    public OrderPlaced Upcast(OrderPlacedV1 oldEvent)
    {
        return new OrderPlaced
        {
            OrderId = oldEvent.OrderId,
            CustomerId = oldEvent.CustomerId,
            CustomerEmail = oldEvent.Email, // Renamed field
            Items = oldEvent.Products.Select(p => new OrderItemData(
                p.Id, p.Name, p.Qty, p.Price, p.Qty * p.Price)).ToList(),
            TotalAmount = oldEvent.Products.Sum(p => p.Qty * p.Price),
            Currency = "VND",  // Default currency added in V2
            ShippingAddress = new AddressData(
                oldEvent.Address, "", "", "", "VN")  // Default values
        };
    }
}

// Registration
options.Events.Upcast<OrderPlacedV1, OrderPlaced, OrderPlacedV2Upcaster>();
```

---

## 9. Best Practices

### Idempotency

```csharp
// Đảm bảo idempotency bằng cách kiểm tra event đã tồn tại
public async Task HandleAsync(ProcessPaymentCommand command, CancellationToken ct)
{
    // Check if event already processed (idempotency key)
    var existingEvents = await _session.Events
        .QueryRawEventDataOnly<PaymentProcessed>()
        .Where(e => e.OrderId == command.OrderId &&
                    e.PaymentGatewayReference == command.GatewayReference)
        .AnyAsync(ct);
    
    if (existingEvents)
    {
        _logger.LogWarning("Payment {Reference} already processed for order {OrderId}",
            command.GatewayReference, command.OrderId);
        return; // Idempotent - skip duplicate
    }
    
    // Process...
}
```

### Optimistic Concurrency

```csharp
// Marten hỗ trợ optimistic concurrency natively
public async Task SaveWithConcurrencyCheckAsync(
    Guid orderId,
    IReadOnlyList<IDomainEvent> events,
    long expectedVersion,
    CancellationToken ct)
{
    // Nếu version không match, throw exception
    _session.Events.Append(orderId, expectedVersion, events.Cast<object>().ToArray());
    
    try
    {
        await _session.SaveChangesAsync(ct);
    }
    catch (EventStreamUnexpectedMaxEventIdException)
    {
        throw new ConcurrencyException(
            $"Order {orderId} was modified by another process");
    }
}
```

---

## Tổng Kết

Event Sourcing với Marten cung cấp:

| Tính năng | Mô tả |
|-----------|-------|
| Event Store | PostgreSQL-backed, ACID-compliant |
| Aggregation | Tự động rebuild aggregate từ events |
| Projections | Inline (sync) và Async |
| Snapshots | Tự động, cải thiện performance |
| Time travel | Query state tại bất kỳ version nào |
| Event streaming | Subscribe và react to events |

**Khi nào dùng Event Sourcing:**
- ✅ Cần complete audit trail
- ✅ Domain phức tạp với nhiều state transitions
- ✅ Cần "time travel" debugging
- ✅ Event-driven architecture
- ✅ Nhiều read models từ cùng data

**Khi nào KHÔNG dùng:**
- ❌ Simple CRUD operations
- ❌ Team chưa familiar với pattern
- ❌ Reporting-heavy workloads (prefer traditional DB)
