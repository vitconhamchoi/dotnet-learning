# Saga Pattern & Distributed Transactions

## Mục Lục
1. [Distributed Transaction Problems](#distributed-transaction-problems)
2. [Saga Pattern Overview](#saga-pattern-overview)
3. [Choreography vs Orchestration](#choreography-vs-orchestration)
4. [Compensating Transactions](#compensating-transactions)
5. [MassTransit StateMachine Sagas](#masstransit-statemachine)
6. [Complete Sample: OrderSaga](#complete-sample)
7. [Saga State Persistence](#saga-state-persistence)
8. [Idempotency trong Sagas](#idempotency)
9. [Error Handling](#error-handling)
10. [Best Practices](#best-practices)

---

## 1. Distributed Transaction Problems

### Two-Phase Commit (2PC) và vấn đề

```
2PC Protocol (Problematic):
┌──────────────────────────────────────────────────────────────┐
│  Phase 1: PREPARE                                            │
│                                                              │
│  Coordinator    OrderService    PaymentService  InventoryService│
│     │                │               │               │       │
│     │──PREPARE──────►│               │               │       │
│     │──PREPARE───────────────────────►               │       │
│     │──PREPARE───────────────────────────────────────►       │
│     │                │               │               │       │
│     │◄──READY────────│               │               │       │
│     │◄──READY────────────────────────│               │       │
│     │◄──READY────────────────────────────────────────│       │
│                                                              │
│  Phase 2: COMMIT                                             │
│     │──COMMIT────────►│               │               │       │
│     │──COMMIT────────────────────────►│               │       │
│     │  💥 NETWORK FAILURE!            │               │       │
│                                                              │
│  Vấn đề: InventoryService không nhận COMMIT                  │
│  → Không biết commit hay rollback                            │
│  → System bị BLOCKED cho đến khi coordinator recover        │
└──────────────────────────────────────────────────────────────┘

Vấn đề của 2PC:
❌ Blocking protocol - tất cả participants bị block
❌ Coordinator là Single Point of Failure
❌ Network partition → system bị treo
❌ Long-held locks → performance kém
❌ Không hoạt động với NoSQL databases
❌ Không scalable trong microservices
```

### Saga Pattern là giải pháp

```
SAGA APPROACH:
┌─────────────────────────────────────────────────────────────┐
│  Thay vì 1 transaction lớn, chia thành nhiều local          │
│  transactions với compensating transactions                  │
│                                                              │
│  T1 → T2 → T3 → T4                                          │
│  (nếu T3 fail)                                               │
│  T1 → T2 → T3(fail) → C2 → C1                               │
│                                                              │
│  Trong đó:                                                   │
│  T1 = CreateOrder      C1 = CancelOrder                      │
│  T2 = ProcessPayment   C2 = RefundPayment                    │
│  T3 = ReserveInventory C3 = ReleaseInventory                 │
│  T4 = ShipOrder        C4 = CancelShipment                   │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Saga Pattern Overview

```
Order Saga Flow:
                         ┌──────────────────────────────────────────┐
                         │              ORDER SAGA                   │
                         │                                           │
  OrderSubmitted         │  ┌──────────┐                            │
  ─────────────────────► │  │  Start   │                            │
                         │  └────┬─────┘                            │
                         │       │ CreateOrder                       │
                         │  ┌────▼─────────────────────────────┐    │
                         │  │  OrderCreated                    │    │
                         │  └────┬─────────────────────────────┘    │
                         │       │ ProcessPayment                    │
                         │  ┌────▼─────────────────────────────┐    │
                         │  │  PaymentProcessed ────────────►  │    │
                         │  │         or                        │    │
                         │  │  PaymentFailed ──►(Compensate)   │    │
                         │  └────┬─────────────────────────────┘    │
                         │       │ ReserveInventory                  │
                         │  ┌────▼─────────────────────────────┐    │
                         │  │  InventoryReserved ────────────► │    │
                         │  │         or                        │    │
                         │  │  InventoryFailed ──►(Compensate) │    │
                         │  └────┬─────────────────────────────┘    │
                         │       │ ShipOrder                         │
                         │  ┌────▼─────────────────────────────┐    │
                         │  │  OrderFulfilled                  │    │
                         │  └──────────────────────────────────┘    │
                         └──────────────────────────────────────────┘
```

---

## 3. Choreography vs Orchestration

### Choreography Saga

```
CHOREOGRAPHY (Event-driven, decentralized):
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│  OrderService  ──OrderPlaced──►  PaymentService             │
│                                      │                      │
│                                      ├──PaymentProcessed──► │
│                                      │   InventoryService   │
│  OrderService ◄──OrderConfirmed──    │        │             │
│                                      │   InventoryReserved  │
│                                      │        │             │
│                                      │   ShippingService    │
│                                      │        │             │
│                                      │   OrderShipped       │
│                                                              │
│ ✅ Loose coupling                                            │
│ ✅ Simple - không có central coordinator                     │
│ ❌ Khó track overall transaction state                       │
│ ❌ Circular dependencies risk                                │
│ ❌ Testing phức tạp                                          │
└──────────────────────────────────────────────────────────────┘

ORCHESTRATION (State machine, centralized):
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│              ┌──────────────────────────┐                   │
│              │      ORDER SAGA          │                   │
│              │    (Orchestrator)        │                   │
│              └──┬───────────────────┬──┘                   │
│                 │                   │                       │
│  ──command──►   │  PaymentService   │  InventoryService     │
│  ◄──response──  │                   │                       │
│                 │  ShippingService  │  NotificationService  │
│                                                              │
│ ✅ Centralized state management                             │
│ ✅ Dễ debug và monitor                                       │
│ ✅ Clear business flow                                       │
│ ❌ Orchestrator là potential bottleneck                      │
│ ❌ Coupling với services                                     │
└──────────────────────────────────────────────────────────────┘
```

---

## 4. Messages và Events

```csharp
// Shared Messages Contract
namespace ECommerce.Contracts.Messages;

// ==================== Commands ====================

public record ProcessPayment(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    Guid CorrelationId);

public record RefundPayment(
    Guid OrderId,
    Guid TransactionId,
    decimal Amount,
    string Reason,
    Guid CorrelationId);

public record ReserveInventory(
    Guid OrderId,
    IReadOnlyList<InventoryItem> Items,
    Guid CorrelationId);

public record ReleaseInventory(
    Guid OrderId,
    IReadOnlyList<InventoryItem> Items,
    string Reason,
    Guid CorrelationId);

public record ShipOrder(
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress Address,
    IReadOnlyList<ShipmentItem> Items,
    Guid CorrelationId);

public record SendOrderNotification(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    string NotificationType,
    Dictionary<string, string> TemplateData,
    Guid CorrelationId);

// ==================== Events ====================

public record OrderSubmitted(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    IReadOnlyList<OrderLineItem> Items,
    decimal TotalAmount,
    string Currency,
    ShippingAddress ShippingAddress);

public record PaymentProcessed(
    Guid OrderId,
    Guid TransactionId,
    decimal AmountPaid,
    string PaymentMethod,
    DateTime ProcessedAt);

public record PaymentFailed(
    Guid OrderId,
    string Reason,
    string ErrorCode);

public record InventoryReserved(
    Guid OrderId,
    IReadOnlyList<ReservationResult> Reservations,
    DateTime ReservedUntil);

public record InventoryReservationFailed(
    Guid OrderId,
    IReadOnlyList<FailedItem> FailedItems,
    string Reason);

public record OrderShipped(
    Guid OrderId,
    string TrackingNumber,
    string Carrier,
    DateTime EstimatedDelivery);

public record OrderFulfilled(
    Guid OrderId,
    Guid CustomerId,
    DateTime FulfilledAt);

public record OrderCancelled(
    Guid OrderId,
    string Reason,
    string CancellationSource);

// ==================== Supporting Records ====================

public record OrderLineItem(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public record InventoryItem(
    Guid ProductId,
    int Quantity);

public record ReservationResult(
    Guid ProductId,
    int ReservedQuantity,
    Guid WarehouseId);

public record FailedItem(
    Guid ProductId,
    int RequestedQuantity,
    int AvailableQuantity);

public record ShippingAddress(
    string Street,
    string City,
    string Province,
    string PostalCode,
    string Country,
    string RecipientName,
    string PhoneNumber);

public record ShipmentItem(
    Guid ProductId,
    string ProductName,
    int Quantity);
```

---

## 5. MassTransit StateMachine Saga

```csharp
// OrderSagaState.cs - Saga State (persisted)
public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }  // = OrderId
    public int Version { get; set; }          // Optimistic concurrency
    public string CurrentState { get; set; } = "";
    
    // Data từ events
    public Guid CustomerId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "";
    
    // Payment data
    public Guid? PaymentTransactionId { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? PaymentProcessedAt { get; set; }
    
    // Inventory data
    public string? InventoryReservationData { get; set; }  // JSON serialized
    public DateTime? InventoryReservedAt { get; set; }
    
    // Shipment data
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public DateTime? ShippedAt { get; set; }
    
    // Error handling
    public string? FailureReason { get; set; }
    public string? FailureStep { get; set; }
    public int RetryCount { get; set; }
    
    // Timing
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    
    // Saga scheduling
    public Guid? PaymentTimeoutTokenId { get; set; }
    public Guid? InventoryTimeoutTokenId { get; set; }
}

// OrderSaga.cs - State Machine Definition
public class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    private readonly ILogger<OrderSaga> _logger;

    // ==================== States ====================
    public State AwaitingPayment { get; private set; } = null!;
    public State ProcessingPayment { get; private set; } = null!;
    public State AwaitingInventory { get; private set; } = null!;
    public State ReservingInventory { get; private set; } = null!;
    public State AwaitingShipment { get; private set; } = null!;
    public State Fulfilling { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    // ==================== Events (Triggers) ====================
    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReserved { get; private set; } = null!;
    public Event<InventoryReservationFailed> InventoryReservationFailed { get; private set; } = null!;
    public Event<OrderShipped> OrderShipped { get; private set; } = null!;
    public Event<OrderCancelled> OrderCancelled { get; private set; } = null!;

    // Scheduled Events (Timeouts)
    public Schedule<OrderSagaState, PaymentTimeout> PaymentTimeout { get; private set; } = null!;
    public Schedule<OrderSagaState, InventoryTimeout> InventoryTimeout { get; private set; } = null!;

    public OrderSaga(ILogger<OrderSaga> logger)
    {
        _logger = logger;

        // Configure CorrelationId - dùng OrderId
        Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentProcessed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReserved, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReservationFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => OrderShipped, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => OrderCancelled, x => x.CorrelateById(m => m.Message.OrderId));

        // Configure Schedules (Timeouts)
        Schedule(() => PaymentTimeout,
            instance => instance.PaymentTimeoutTokenId,
            config => config.Delay = TimeSpan.FromMinutes(15));

        Schedule(() => InventoryTimeout,
            instance => instance.InventoryTimeoutTokenId,
            config => config.Delay = TimeSpan.FromMinutes(10));

        // Bắt đầu từ Initial state
        InstanceState(x => x.CurrentState);

        // ==================== State Transitions ====================

        // STEP 1: Order Submitted → Process Payment
        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    var order = context.Message;
                    _logger.LogInformation(
                        "Saga started for order {OrderId} by customer {CustomerId}",
                        order.OrderId, order.CustomerId);

                    context.Saga.CustomerId = order.CustomerId;
                    context.Saga.CustomerEmail = order.CustomerEmail;
                    context.Saga.TotalAmount = order.TotalAmount;
                    context.Saga.Currency = order.Currency;
                    context.Saga.SubmittedAt = DateTime.UtcNow;
                })
                .Schedule(PaymentTimeout,
                    context => context.Init<PaymentTimeout>(new
                    {
                        OrderId = context.Message.OrderId
                    }))
                .Send(context => new ProcessPayment(
                    context.Message.OrderId,
                    context.Message.CustomerId,
                    context.Message.TotalAmount,
                    context.Message.Currency,
                    "Default",
                    context.Message.OrderId))
                .TransitionTo(ProcessingPayment));

        // STEP 2: Payment Processed → Reserve Inventory
        During(ProcessingPayment,
            When(PaymentProcessed)
                .Then(context =>
                {
                    _logger.LogInformation(
                        "Payment processed for order {OrderId}, transaction {TransactionId}",
                        context.Message.OrderId, context.Message.TransactionId);

                    context.Saga.PaymentTransactionId = context.Message.TransactionId;
                    context.Saga.PaymentMethod = context.Message.PaymentMethod;
                    context.Saga.PaymentProcessedAt = context.Message.ProcessedAt;
                })
                .Unschedule(PaymentTimeout)
                .Schedule(InventoryTimeout,
                    context => context.Init<InventoryTimeout>(new
                    {
                        OrderId = context.Message.OrderId
                    }))
                .Send(context => new ReserveInventory(
                    context.Message.OrderId,
                    new List<InventoryItem>(),  // Từ order data
                    context.Message.OrderId))
                .TransitionTo(ReservingInventory),

            // Payment Failed → Compensate
            When(PaymentFailed)
                .Then(context =>
                {
                    _logger.LogWarning(
                        "Payment failed for order {OrderId}: {Reason}",
                        context.Message.OrderId, context.Message.Reason);

                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.FailureStep = "Payment";
                    context.Saga.CancelledAt = DateTime.UtcNow;
                })
                .Unschedule(PaymentTimeout)
                .Publish(context => new OrderCancelled(
                    context.Message.OrderId,
                    $"Payment failed: {context.Message.Reason}",
                    "System"))
                .TransitionTo(Failed),

            // Payment Timeout
            When(PaymentTimeout.Received)
                .Then(context =>
                {
                    _logger.LogWarning("Payment timeout for order {OrderId}", context.Message.OrderId);
                    context.Saga.FailureReason = "Payment timeout";
                    context.Saga.FailureStep = "Payment";
                })
                .Publish(context => new OrderCancelled(
                    context.Message.OrderId,
                    "Payment timeout",
                    "System"))
                .TransitionTo(Failed));

        // STEP 3: Inventory Reserved → Ship Order
        During(ReservingInventory,
            When(InventoryReserved)
                .Then(context =>
                {
                    _logger.LogInformation(
                        "Inventory reserved for order {OrderId}",
                        context.Message.OrderId);

                    context.Saga.InventoryReservedAt = DateTime.UtcNow;
                    context.Saga.InventoryReservationData =
                        JsonSerializer.Serialize(context.Message.Reservations);
                })
                .Unschedule(InventoryTimeout)
                .Send(context => new ShipOrder(
                    context.Message.OrderId,
                    context.Saga.CustomerId,
                    new ShippingAddress("", "", "", "", "", "", ""),  // From saga state
                    new List<ShipmentItem>(),
                    context.Message.OrderId))
                .TransitionTo(Fulfilling),

            // Inventory Failed → Compensate (refund payment)
            When(InventoryReservationFailed)
                .Then(context =>
                {
                    _logger.LogWarning(
                        "Inventory reservation failed for order {OrderId}: {Reason}",
                        context.Message.OrderId, context.Message.Reason);

                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.FailureStep = "Inventory";
                })
                .Unschedule(InventoryTimeout)
                .TransitionTo(Compensating)
                // Compensate: Refund payment
                .Send(context => new RefundPayment(
                    context.Message.OrderId,
                    context.Saga.PaymentTransactionId!.Value,
                    context.Saga.TotalAmount,
                    $"Inventory unavailable: {context.Message.Reason}",
                    context.Message.OrderId)),

            // Inventory Timeout
            When(InventoryTimeout.Received)
                .Then(context =>
                {
                    context.Saga.FailureReason = "Inventory reservation timeout";
                    context.Saga.FailureStep = "Inventory";
                })
                .TransitionTo(Compensating)
                .Send(context => new RefundPayment(
                    context.Message.OrderId,
                    context.Saga.PaymentTransactionId!.Value,
                    context.Saga.TotalAmount,
                    "Inventory reservation timeout",
                    context.Message.OrderId)));

        // STEP 4: Order Shipped → Completed
        During(Fulfilling,
            When(OrderShipped)
                .Then(context =>
                {
                    _logger.LogInformation(
                        "Order {OrderId} shipped with tracking {TrackingNumber}",
                        context.Message.OrderId, context.Message.TrackingNumber);

                    context.Saga.TrackingNumber = context.Message.TrackingNumber;
                    context.Saga.Carrier = context.Message.Carrier;
                    context.Saga.EstimatedDelivery = context.Message.EstimatedDelivery;
                    context.Saga.ShippedAt = DateTime.UtcNow;
                    context.Saga.CompletedAt = DateTime.UtcNow;
                })
                .Publish(context => new OrderFulfilled(
                    context.Message.OrderId,
                    context.Saga.CustomerId,
                    DateTime.UtcNow))
                .Send(context => new SendOrderNotification(
                    context.Message.OrderId,
                    context.Saga.CustomerId,
                    context.Saga.CustomerEmail,
                    "OrderShipped",
                    new Dictionary<string, string>
                    {
                        ["TrackingNumber"] = context.Message.TrackingNumber,
                        ["Carrier"] = context.Message.Carrier,
                        ["EstimatedDelivery"] = context.Message.EstimatedDelivery.ToString("dd/MM/yyyy")
                    },
                    context.Message.OrderId))
                .TransitionTo(Completed)
                .Finalize());

        // Compensating state → wait for refund then cancel
        During(Compensating,
            // After refund (from any state), mark as cancelled
            Ignore(PaymentProcessed));

        // Handle OrderCancelled from external (customer request)
        DuringAny(
            When(OrderCancelled)
                .If(context => context.Saga.CurrentState != "Completed" &&
                               context.Saga.CurrentState != "Failed" &&
                               context.Saga.CurrentState != "Cancelled",
                    binder => binder
                        .Then(context =>
                        {
                            context.Saga.CancelledAt = DateTime.UtcNow;
                            context.Saga.FailureReason = context.Message.Reason;
                        })
                        .TransitionTo(Cancelled)
                        .Finalize()));

        // Mark as completed when finalized
        SetCompletedWhenFinalized();
    }
}

// Timeout Messages
public record PaymentTimeout(Guid OrderId);
public record InventoryTimeout(Guid OrderId);
```

---

## 6. Service Consumers (Participants)

```csharp
// Payment Service Consumer
public class ProcessPaymentConsumer : IConsumer<ProcessPayment>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<ProcessPaymentConsumer> _logger;

    public ProcessPaymentConsumer(IPaymentGateway paymentGateway, ILogger<ProcessPaymentConsumer> logger)
    {
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("Processing payment for order {OrderId}", cmd.OrderId);

        try
        {
            // Idempotency check
            var existingTransaction = await _paymentGateway.FindByReferenceAsync(
                cmd.OrderId.ToString(),
                context.CancellationToken);

            if (existingTransaction is not null)
            {
                _logger.LogInformation(
                    "Payment already processed for order {OrderId}, publishing success",
                    cmd.OrderId);
                
                await context.Publish(new PaymentProcessed(
                    cmd.OrderId,
                    existingTransaction.TransactionId,
                    existingTransaction.Amount,
                    existingTransaction.PaymentMethod,
                    existingTransaction.ProcessedAt));
                return;
            }

            // Process payment
            var result = await _paymentGateway.ChargeAsync(new ChargeRequest
            {
                Amount = cmd.Amount,
                Currency = cmd.Currency,
                CustomerId = cmd.CustomerId,
                OrderReference = cmd.OrderId.ToString(),
                PaymentMethod = cmd.PaymentMethod
            }, context.CancellationToken);

            if (result.Success)
            {
                await context.Publish(new PaymentProcessed(
                    cmd.OrderId,
                    result.TransactionId,
                    result.ChargedAmount,
                    cmd.PaymentMethod,
                    DateTime.UtcNow));
            }
            else
            {
                await context.Publish(new PaymentFailed(
                    cmd.OrderId,
                    result.FailureReason ?? "Unknown payment error",
                    result.ErrorCode ?? "PAYMENT_FAILED"));
            }
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogError(ex, "Payment gateway error for order {OrderId}", cmd.OrderId);
            await context.Publish(new PaymentFailed(cmd.OrderId, ex.Message, "GATEWAY_ERROR"));
        }
    }
}

// Inventory Service Consumer
public class ReserveInventoryConsumer : IConsumer<ReserveInventory>
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<ReserveInventoryConsumer> _logger;

    public ReserveInventoryConsumer(
        IInventoryService inventoryService,
        ILogger<ReserveInventoryConsumer> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("Reserving inventory for order {OrderId}", cmd.OrderId);

        var reservationResults = new List<ReservationResult>();
        var failedItems = new List<FailedItem>();

        foreach (var item in cmd.Items)
        {
            var available = await _inventoryService.GetAvailableStockAsync(item.ProductId);

            if (available >= item.Quantity)
            {
                await _inventoryService.ReserveAsync(cmd.OrderId, item.ProductId, item.Quantity);
                reservationResults.Add(new ReservationResult(
                    item.ProductId, item.Quantity, Guid.NewGuid()));
            }
            else
            {
                failedItems.Add(new FailedItem(item.ProductId, item.Quantity, available));
            }
        }

        if (!failedItems.Any())
        {
            await context.Publish(new InventoryReserved(
                cmd.OrderId,
                reservationResults,
                DateTime.UtcNow.AddHours(24)));
        }
        else
        {
            // Release already reserved items (partial rollback)
            foreach (var reserved in reservationResults)
            {
                await _inventoryService.ReleaseAsync(cmd.OrderId, reserved.ProductId, reserved.ReservedQuantity);
            }

            await context.Publish(new InventoryReservationFailed(
                cmd.OrderId,
                failedItems,
                "Insufficient stock for some items"));
        }
    }
}

// Refund Consumer (Compensation)
public class RefundPaymentConsumer : IConsumer<RefundPayment>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<RefundPaymentConsumer> _logger;

    public RefundPaymentConsumer(IPaymentGateway paymentGateway, ILogger<RefundPaymentConsumer> logger)
    {
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RefundPayment> context)
    {
        var cmd = context.Message;
        _logger.LogInformation(
            "Processing refund for order {OrderId}, amount {Amount}",
            cmd.OrderId, cmd.Amount);

        try
        {
            await _paymentGateway.RefundAsync(new RefundRequest
            {
                TransactionId = cmd.TransactionId,
                Amount = cmd.Amount,
                Reason = cmd.Reason,
                OrderReference = cmd.OrderId.ToString()
            }, context.CancellationToken);

            _logger.LogInformation("Refund processed for order {OrderId}", cmd.OrderId);

            // Publish compensation completed
            await context.Publish(new OrderCancelled(
                cmd.OrderId,
                cmd.Reason,
                "CompensationRefund"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund failed for order {OrderId}", cmd.OrderId);
            // Alert operations team for manual intervention
            throw; // Will be retried by MassTransit
        }
    }
}
```

---

## 7. Saga State Persistence

```csharp
// Program.cs - MassTransit với Saga Persistence
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(config =>
{
    // Saga State Machine
    config.AddSagaStateMachine<OrderSaga, OrderSagaState>()
        .EntityFrameworkRepository(repo =>
        {
            repo.ConcurrencyMode = ConcurrencyMode.Optimistic;
            repo.AddDbContext<DbContext, SagaDbContext>((provider, options) =>
            {
                options.UseNpgsql(
                    builder.Configuration.GetConnectionString("SagaDb"),
                    npgsql => npgsql.EnableRetryOnFailure(3));
            });
        });

    // Service Consumers
    config.AddConsumer<ProcessPaymentConsumer>();
    config.AddConsumer<ReserveInventoryConsumer>();
    config.AddConsumer<ReleaseInventoryConsumer>();
    config.AddConsumer<RefundPaymentConsumer>();
    config.AddConsumer<ShipOrderConsumer>();
    config.AddConsumer<SendOrderNotificationConsumer>();

    config.UsingRabbitMq((context, rmq) =>
    {
        rmq.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]!);
            h.Password(builder.Configuration["RabbitMQ:Password"]!);
        });

        // Configure retry policy
        rmq.UseMessageRetry(retry =>
            retry.Exponential(
                retryLimit: 5,
                minInterval: TimeSpan.FromSeconds(1),
                maxInterval: TimeSpan.FromSeconds(30),
                intervalDelta: TimeSpan.FromSeconds(2)));

        // Configure outbox for at-least-once delivery
        rmq.UseInMemoryOutbox(context);

        // Saga endpoint
        rmq.ReceiveEndpoint("order-saga", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.StateMachineSaga<OrderSagaState>(context);
        });

        // Service endpoints
        rmq.ReceiveEndpoint("payment-processing", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(10)));
            e.ConfigureConsumer<ProcessPaymentConsumer>(context);
        });

        rmq.ReceiveEndpoint("inventory-reservation", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.ConfigureConsumer<ReserveInventoryConsumer>(context);
        });

        rmq.ReceiveEndpoint("payment-refund", e =>
        {
            e.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(15)));
            e.ConfigureConsumer<RefundPaymentConsumer>(context);
        });

        rmq.ConfigureEndpoints(context);
    });
});

// SagaDbContext.cs
public class SagaDbContext : DbContext
{
    public SagaDbContext(DbContextOptions<SagaDbContext> options) : base(options) { }

    public DbSet<OrderSagaState> OrderSagas => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.ToTable("order_sagas");
            entity.HasKey(e => e.CorrelationId);
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.Property(e => e.CurrentState).HasMaxLength(100);
            entity.Property(e => e.FailureReason).HasMaxLength(1000);
            entity.Property(e => e.InventoryReservationData).HasColumnType("jsonb");
            
            // Index for querying
            entity.HasIndex(e => e.CurrentState);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.SubmittedAt);
        });
    }
}
```

---

## 8. Idempotency trong Sagas

```csharp
// Idempotency Key Middleware
public class IdempotencyFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IIdempotencyStore _store;
    private readonly ILogger<IdempotencyFilter<T>> _logger;

    public IdempotencyFilter(IIdempotencyStore store, ILogger<IdempotencyFilter<T>> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        // Dùng MessageId như idempotency key
        var key = context.MessageId?.ToString() ?? Guid.NewGuid().ToString();

        if (await _store.ExistsAsync(key, context.CancellationToken))
        {
            _logger.LogInformation(
                "Duplicate message detected for key {Key}, message type {MessageType}. Skipping.",
                key, typeof(T).Name);
            return;
        }

        await next.Send(context);

        // Mark as processed after successful handling
        await _store.MarkAsProcessedAsync(key, TimeSpan.FromDays(7), context.CancellationToken);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("idempotency");
}

// Redis-based Idempotency Store
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDistributedCache _cache;

    public RedisIdempotencyStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        var value = await _cache.GetAsync($"idempotency:{key}", ct);
        return value is not null;
    }

    public async Task MarkAsProcessedAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        await _cache.SetAsync(
            $"idempotency:{key}",
            Encoding.UTF8.GetBytes("processed"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);
    }
}
```

---

## 9. Monitoring và Observability

```csharp
// Saga Monitoring Extension
public static class SagaMonitoringExtensions
{
    public static IServiceCollection AddSagaMonitoring(this IServiceCollection services)
    {
        services.AddScoped<ISagaMonitor, SagaMonitor>();
        return services;
    }
}

public class SagaMonitor : ISagaMonitor
{
    private readonly SagaDbContext _context;
    private readonly ILogger<SagaMonitor> _logger;

    public SagaMonitor(SagaDbContext context, ILogger<SagaMonitor> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SagaStatistics> GetStatisticsAsync(CancellationToken ct)
    {
        var stats = await _context.OrderSagas
            .GroupBy(s => s.CurrentState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var stuckSagas = await _context.OrderSagas
            .Where(s => s.CurrentState != "Completed" &&
                        s.CurrentState != "Failed" &&
                        s.CurrentState != "Cancelled" &&
                        s.SubmittedAt < DateTime.UtcNow.AddHours(-1))
            .CountAsync(ct);

        return new SagaStatistics
        {
            StateBreakdown = stats.ToDictionary(s => s.State, s => s.Count),
            StuckSagasCount = stuckSagas
        };
    }

    public async Task<IReadOnlyList<OrderSagaState>> GetStuckSagasAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);
        
        return await _context.OrderSagas
            .Where(s => s.CurrentState != "Completed" &&
                        s.CurrentState != "Failed" &&
                        s.CurrentState != "Cancelled" &&
                        s.SubmittedAt < cutoff)
            .OrderBy(s => s.SubmittedAt)
            .Take(100)
            .ToListAsync(ct);
    }
}

// Admin endpoint cho monitoring
[ApiController]
[Route("admin/sagas")]
[Authorize(Roles = "Admin")]
public class SagaAdminController : ControllerBase
{
    private readonly ISagaMonitor _monitor;
    private readonly IBusControl _bus;

    public SagaAdminController(ISagaMonitor monitor, IBusControl bus)
    {
        _monitor = monitor;
        _bus = bus;
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(CancellationToken ct)
    {
        var stats = await _monitor.GetStatisticsAsync(ct);
        return Ok(stats);
    }

    [HttpGet("stuck")]
    public async Task<IActionResult> GetStuckSagas(CancellationToken ct)
    {
        var stuck = await _monitor.GetStuckSagasAsync(ct);
        return Ok(stuck.Select(s => new
        {
            s.CorrelationId,
            s.CurrentState,
            s.SubmittedAt,
            s.FailureReason,
            StuckFor = DateTime.UtcNow - s.SubmittedAt
        }));
    }

    [HttpPost("{orderId}/retry")]
    public async Task<IActionResult> RetryOrder(Guid orderId, CancellationToken ct)
    {
        // Manually retry a stuck saga
        await _bus.Publish(new OrderSubmitted(
            orderId,
            Guid.Empty,
            "",
            new List<OrderLineItem>(),
            0,
            "VND",
            new ShippingAddress("", "", "", "", "", "", "")),
            ct);
        
        return Accepted();
    }
}

public record SagaStatistics(
    Dictionary<string, int> StateBreakdown,
    int StuckSagasCount);
```

---

## 10. Testing Sagas

```csharp
// Integration Tests cho OrderSaga
public class OrderSagaTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
                   .InMemoryRepository();
                
                cfg.AddConsumer<ProcessPaymentConsumer>();
                cfg.AddConsumer<ReserveInventoryConsumer>();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    [Fact]
    public async Task OrderSaga_HappyPath_CompletesSuccessfully()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<OrderSaga, OrderSagaState>();

        // Act - Submit order
        await _harness.Bus.Publish(new OrderSubmitted(
            orderId,
            Guid.NewGuid(),
            "customer@example.com",
            new List<OrderLineItem> { new(Guid.NewGuid(), "Product 1", 2, 100_000) },
            200_000,
            "VND",
            new ShippingAddress("123 Main St", "Hanoi", "HN", "10000", "VN", "John", "0901234567")));

        // Assert - Saga started
        Assert.True(await sagaHarness.Consumed.Any<OrderSubmitted>());
        
        // Wait for payment to be processed
        Assert.True(await _harness.Sent.Any<ProcessPayment>());

        // Simulate payment success
        await _harness.Bus.Publish(new PaymentProcessed(
            orderId,
            Guid.NewGuid(),
            200_000,
            "CreditCard",
            DateTime.UtcNow));

        // Assert inventory reserved
        Assert.True(await _harness.Sent.Any<ReserveInventory>());

        // Simulate inventory success
        await _harness.Bus.Publish(new InventoryReserved(
            orderId,
            new List<ReservationResult>(),
            DateTime.UtcNow.AddHours(24)));

        // Assert order shipped
        Assert.True(await _harness.Sent.Any<ShipOrder>());

        // Simulate shipment
        await _harness.Bus.Publish(new OrderShipped(
            orderId,
            "TN123456789",
            "GHTK",
            DateTime.UtcNow.AddDays(2)));

        // Assert saga completed
        Assert.True(await _harness.Published.Any<OrderFulfilled>());
        
        var sagaState = await sagaHarness.Created.SelectAsync(
            x => x.CorrelationId == orderId).FirstOrDefault();
        Assert.NotNull(sagaState);
        Assert.Equal("Completed", sagaState.CurrentState);
    }

    [Fact]
    public async Task OrderSaga_PaymentFailed_CancelsOrder()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaHarness = _harness.GetSagaStateMachineHarness<OrderSaga, OrderSagaState>();

        // Act
        await _harness.Bus.Publish(new OrderSubmitted(
            orderId,
            Guid.NewGuid(),
            "customer@example.com",
            new List<OrderLineItem>(),
            100_000,
            "VND",
            new ShippingAddress("", "", "", "", "", "", "")));

        await _harness.Sent.Any<ProcessPayment>();

        // Simulate payment failure
        await _harness.Bus.Publish(new PaymentFailed(
            orderId,
            "Insufficient funds",
            "INSUFFICIENT_FUNDS"));

        // Assert
        Assert.True(await _harness.Published.Any<OrderCancelled>());
        
        var sagaState = await sagaHarness.Created
            .SelectAsync(x => x.CorrelationId == orderId)
            .FirstOrDefault();
        
        Assert.Equal("Failed", sagaState?.CurrentState);
    }

    [Fact]
    public async Task OrderSaga_InventoryFailed_RefundsPayment()
    {
        var orderId = Guid.NewGuid();
        
        await _harness.Bus.Publish(new OrderSubmitted(orderId, Guid.NewGuid(), "test@test.com",
            new List<OrderLineItem>(), 100_000, "VND",
            new ShippingAddress("", "", "", "", "", "", "")));

        await _harness.Sent.Any<ProcessPayment>();
        
        await _harness.Bus.Publish(new PaymentProcessed(orderId, Guid.NewGuid(), 100_000, "MoMo", DateTime.UtcNow));
        await _harness.Sent.Any<ReserveInventory>();
        
        // Inventory fails
        await _harness.Bus.Publish(new InventoryReservationFailed(
            orderId,
            new List<FailedItem> { new(Guid.NewGuid(), 5, 2) },
            "Insufficient stock"));

        // Assert: RefundPayment should be sent
        Assert.True(await _harness.Sent.Any<RefundPayment>());
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }
}
```

---

## 11. Best Practices

```csharp
// ✅ 1. Always use idempotency
// Mỗi consumer phải handle duplicate messages gracefully

// ✅ 2. Compensating transactions phải idempotent
// Refund có thể được gọi nhiều lần do retry
public async Task HandleRefund(RefundRequest request)
{
    var existingRefund = await _db.Refunds
        .FirstOrDefaultAsync(r => r.OrderId == request.OrderId);
    
    if (existingRefund is not null)
    {
        // Đã refund rồi, skip
        return;
    }
    
    // Process refund...
}

// ✅ 3. Set reasonable timeouts
Schedule(() => PaymentTimeout,
    instance => instance.PaymentTimeoutTokenId,
    config => config.Delay = TimeSpan.FromMinutes(15)); // Reasonable timeout

// ✅ 4. Log state transitions
// ✅ 5. Monitor stuck sagas
// ✅ 6. Alert on compensation

// ✅ 7. Design compensations trước khi implement
/*
Saga Step          | Compensation
-------------------|------------------
CreateOrder        | CancelOrder
ProcessPayment     | RefundPayment
ReserveInventory   | ReleaseInventory
ShipOrder          | RecallShipment (if possible)
*/

// ✅ 8. Outbox pattern để đảm bảo at-least-once delivery
rmq.UseInMemoryOutbox(context);

// ✅ 9. Dead Letter Queue cho messages không xử lý được
rmq.ReceiveEndpoint("payment-dlq", e =>
{
    e.ConfigureDeadLetterQueueDeadLetterTransport();
    // Alert khi có messages trong DLQ
});

// ✅ 10. Saga timeout policy
config.AddSagaStateMachine<OrderSaga, OrderSagaState>()
    .EntityFrameworkRepository(repo =>
    {
        repo.ConcurrencyMode = ConcurrencyMode.Optimistic;
        // ...
    });
```

---

## Tổng Kết

Saga Pattern giải quyết distributed transaction problems:

```
So sánh Approaches:
┌──────────────────┬──────────────────┬──────────────────────┐
│ Aspect           │ 2PC              │ Saga                  │
├──────────────────┼──────────────────┼──────────────────────┤
│ Consistency      │ Strong (ACID)    │ Eventual              │
│ Blocking         │ Yes              │ No                    │
│ Performance      │ Low              │ High                  │
│ Fault tolerance  │ Low (SPoF)       │ High                  │
│ Complexity       │ Low concept      │ High (compensations)  │
│ DB Support       │ Requires 2PC     │ Any                   │
│ Scalability      │ Limited          │ High                  │
└──────────────────┴──────────────────┴──────────────────────┘

Choreography vs Orchestration:
┌──────────────────┬──────────────────┬──────────────────────┐
│ Aspect           │ Choreography     │ Orchestration         │
├──────────────────┼──────────────────┼──────────────────────┤
│ Coupling         │ Loose            │ Tighter               │
│ Visibility       │ Hard to trace    │ Easy to monitor       │
│ Testing          │ Complex          │ Straightforward       │
│ Business flow    │ Implicit         │ Explicit              │
│ Complexity       │ Grows with size  │ Centralized           │
└──────────────────┴──────────────────┴──────────────────────┘
```

**Key Takeaways:**
- ✅ Saga cho phép distributed transactions không cần locks
- ✅ Compensating transactions phải idempotent
- ✅ MassTransit StateMachine đơn giản hóa orchestration
- ✅ Monitoring là critical - track stuck sagas
- ✅ Design compensations từ đầu, không phải sau
- ✅ Idempotency key ở mọi step để handle retries
