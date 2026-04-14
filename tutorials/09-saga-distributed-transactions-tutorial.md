# Saga Pattern & Distributed Transactions: Điều phối workflow dài trong hệ thống distributed

## 1. Vấn đề: Two-Phase Commit không hoạt động ở quy mô lớn

Trong monolith hoặc hệ thống đơn database, transaction ACID là đương nhiên. Bạn có thể `BEGIN TRANSACTION`, làm 10 thứ, rồi `COMMIT` hoặc `ROLLBACK`. Tất cả hoặc không.

Trong distributed system với microservices, bạn không có shared database transaction. Nếu bạn có:
- `OrderService` viết vào Postgres A
- `InventoryService` viết vào Postgres B
- `PaymentService` viết vào Stripe
- `NotificationService` gửi email qua SendGrid

...thì không có cách nào để wrap tất cả trong một transaction ACID. Two-Phase Commit (2PC) tồn tại về mặt lý thuyết nhưng:
- Đòi hỏi coordinator, tạo single point of failure
- Giữ lock lâu, giảm throughput nghiêm trọng
- Không phải mọi resource đều hỗ trợ XA transaction
- Latency cao vì phải chờ nhiều vòng network round-trip

**Saga pattern** là giải pháp: thay vì một transaction lớn, bạn có chuỗi các transaction nhỏ cục bộ trong từng service. Nếu một bước thất bại, bạn chạy **compensating transaction** để hoàn tác các bước trước.

---

## 2. Hai loại Saga: Choreography vs Orchestration

### 2.1 Choreography Saga

Mỗi service lắng nghe event từ service khác và tự quyết định hành động tiếp theo. Không có coordinator trung tâm.

```text
OrderService ──► OrderPlaced event
                     │
                     ▼
            InventoryService
            (lắng nghe OrderPlaced)
            ── reserved → InventoryReserved event
            ── failed  → InventoryReservationFailed event
                     │
                     ▼
            PaymentService  
            (lắng nghe InventoryReserved)
            ── success → PaymentProcessed event
            ── failed  → PaymentFailed event
                     │
                     ▼
            OrderService
            (lắng nghe PaymentProcessed / PaymentFailed)
            ── confirms order hoặc cancels
```

**Ưu điểm**:
- Loose coupling thực sự - service không biết về nhau
- Không có single point of failure

**Nhược điểm**:
- Logic phân tán, khó trace toàn bộ flow
- Khó thêm bước mới vào workflow
- Khó debug khi có vấn đề

### 2.2 Orchestration Saga

Một saga orchestrator trung tâm điều phối toàn bộ flow, gửi command tới từng service và xử lý response.

```text
                    ┌──────────────────────┐
                    │   OrderSaga (State    │
                    │   Machine trong       │
                    │   OrderService)       │
                    └──┬──────────┬─────────┘
                       │          │
                  send cmd    receive response
                       │          │
              ┌────────▼──┐  ┌───▼──────────────┐
              │ Inventory  │  │    Payment        │
              │ Service   │  │    Service        │
              └───────────┘  └──────────────────┘
```

**Ưu điểm**:
- Flow rõ ràng, dễ debug
- Dễ thêm bước mới
- Compensating transaction tập trung

**Nhược điểm**:
- Orchestrator biết về nhiều service (higher coupling)
- Orchestrator có thể trở thành bottleneck nếu không careful

---

## 3. Orchestration Saga với MassTransit

MassTransit có state machine saga là công cụ mạnh nhất để implement orchestration saga trong .NET.

### 3.1 Định nghĩa Saga State

```csharp
// State của saga được persist vào database
public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }  // = OrderId
    public string CurrentState { get; set; } = "";
    
    // Data thu thập qua các bước
    public Guid CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? InventoryReservationId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? FailureReason { get; set; }
    
    // Timeout tracking
    public Guid? PaymentTimeoutTokenId { get; set; }
}
```

### 3.2 Saga State Machine

```csharp
public class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    // States
    public State WaitingForInventory { get; private set; } = null!;
    public State WaitingForPayment { get; private set; } = null!;
    public State CompensatingInventory { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    // Events (messages nhận được)
    public Event<OrderPlaced> OrderPlaced { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReserved { get; private set; } = null!;
    public Event<InventoryReservationFailed> InventoryReservationFailed { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;
    public Schedule<OrderSagaState, PaymentTimeout> PaymentTimeoutSchedule { get; private set; } = null!;

    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);

        // Correlate messages tới đúng saga instance bằng OrderId
        Event(() => OrderPlaced, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserved, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReservationFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentProcessed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        // Timeout schedule
        Schedule(() => PaymentTimeoutSchedule, 
            x => x.PaymentTimeoutTokenId,
            s => s.Delay = TimeSpan.FromMinutes(5));

        // Initial state: nhận OrderPlaced, gửi ReserveInventory command
        Initially(
            When(OrderPlaced)
                .Then(ctx =>
                {
                    ctx.Saga.CustomerId = ctx.Message.CustomerId;
                    ctx.Saga.TotalAmount = ctx.Message.TotalAmount;
                })
                .Send(ctx => new ReserveInventoryCommand(
                    ctx.Message.OrderId,
                    ctx.Message.Lines))
                .TransitionTo(WaitingForInventory));

        // Inventory reserved: gửi ProcessPayment command, set timeout
        During(WaitingForInventory,
            When(InventoryReserved)
                .Then(ctx => ctx.Saga.InventoryReservationId = ctx.Message.ReservationId)
                .Schedule(PaymentTimeoutSchedule, ctx => new PaymentTimeout { OrderId = ctx.Saga.CorrelationId })
                .Send(ctx => new ProcessPaymentCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.CustomerId,
                    ctx.Saga.TotalAmount))
                .TransitionTo(WaitingForPayment),

            When(InventoryReservationFailed)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .Publish(ctx => new OrderFailed(ctx.Saga.CorrelationId, ctx.Saga.FailureReason!))
                .TransitionTo(Failed));

        // Payment processed: complete saga
        During(WaitingForPayment,
            When(PaymentProcessed)
                .Unschedule(PaymentTimeoutSchedule)
                .Then(ctx => ctx.Saga.PaymentIntentId = ctx.Message.PaymentIntentId)
                .Publish(ctx => new OrderCompleted(ctx.Saga.CorrelationId))
                .TransitionTo(Completed),

            When(PaymentFailed)
                .Unschedule(PaymentTimeoutSchedule)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                // Compensate: release inventory
                .Send(ctx => new ReleaseInventoryCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.InventoryReservationId!))
                .TransitionTo(CompensatingInventory),

            When(PaymentTimeoutSchedule.Received)
                .Then(ctx => ctx.Saga.FailureReason = "Payment timeout")
                .Send(ctx => new CancelPaymentCommand(ctx.Saga.CorrelationId))
                .Send(ctx => new ReleaseInventoryCommand(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.InventoryReservationId!))
                .Publish(ctx => new OrderFailed(ctx.Saga.CorrelationId, "Payment timeout"))
                .TransitionTo(Failed));

        // Compensation complete
        During(CompensatingInventory,
            Ignore(InventoryReserved), // có thể đến muộn
            When(InventoryReservationFailed) // inventory đã released rồi thì ignore
                .Publish(ctx => new OrderFailed(ctx.Saga.CorrelationId, ctx.Saga.FailureReason!))
                .TransitionTo(Failed));

        // Cleanup completed sagas sau 7 ngày
        SetCompletedWhenFinalized();
    }
}
```

### 3.3 Đăng ký Saga với PostgreSQL persistence

```csharp
// Program.cs
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, OrderSagaDbContext>((provider, options) =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
            });
        });

    cfg.UsingRabbitMq((ctx, rabbitCfg) =>
    {
        rabbitCfg.Host("rabbitmq://localhost");
        
        rabbitCfg.ReceiveEndpoint("order-saga", ep =>
        {
            ep.ConfigureSaga<OrderSagaState>(ctx);
            
            // Quan trọng: tắt prefetch để tránh concurrency issue với saga
            ep.PrefetchCount = 1;
        });
    });
});
```

---

## 4. Compensating Transactions: hoàn tác khi thất bại

Đây là phần khó nhất của Saga. Compensating transaction phải:
1. **Idempotent**: có thể chạy nhiều lần mà không gây lỗi
2. **Always succeed** về mặt infrastructure (dù business logic rollback)
3. **Semantic undo**, không phải technical rollback

```csharp
// Handler release inventory - compensating transaction
public class ReleaseInventoryCommandHandler : IConsumer<ReleaseInventoryCommand>
{
    private readonly IInventoryRepository _repo;
    private readonly ILogger<ReleaseInventoryCommandHandler> _logger;

    public async Task Consume(ConsumeContext<ReleaseInventoryCommand> ctx)
    {
        var cmd = ctx.Message;
        
        _logger.LogInformation("Releasing inventory reservation {ReservationId} for order {OrderId}",
            cmd.ReservationId, cmd.OrderId);

        // Idempotency check: nếu đã release rồi thì bỏ qua
        var reservation = await _repo.FindAsync(cmd.ReservationId);
        if (reservation is null || reservation.Status == ReservationStatus.Released)
        {
            _logger.LogWarning("Reservation {ReservationId} already released or not found, skipping", 
                cmd.ReservationId);
            return;
        }

        await _repo.ReleaseAsync(cmd.ReservationId);
        
        _logger.LogInformation("Inventory released successfully for order {OrderId}", cmd.OrderId);
    }
}
```

---

## 5. Choreography Saga với MassTransit

Khi saga đơn giản và service thực sự cần decoupled.

```csharp
// InventoryService lắng nghe OrderPlaced
public class OrderPlacedConsumer : IConsumer<OrderPlaced>
{
    private readonly IInventoryService _inventory;

    public async Task Consume(ConsumeContext<OrderPlaced> ctx)
    {
        try
        {
            var reservationId = await _inventory.ReserveAsync(ctx.Message.Lines);
            
            await ctx.Publish(new InventoryReserved(
                ctx.Message.OrderId,
                reservationId));
        }
        catch (InsufficientStockException ex)
        {
            await ctx.Publish(new InventoryReservationFailed(
                ctx.Message.OrderId,
                ex.Message));
        }
    }
}

// PaymentService lắng nghe InventoryReserved
public class InventoryReservedConsumer : IConsumer<InventoryReserved>
{
    private readonly IPaymentGateway _payment;

    public async Task Consume(ConsumeContext<InventoryReserved> ctx)
    {
        // Lấy order details từ OrderService (hoặc từ message context nếu carry data)
        var order = await GetOrderDetailsAsync(ctx.Message.OrderId);
        
        try
        {
            var result = await _payment.ChargeAsync(order.CustomerId, order.TotalAmount);
            
            await ctx.Publish(new PaymentProcessed(
                ctx.Message.OrderId,
                result.PaymentIntentId));
        }
        catch (PaymentDeclinedException ex)
        {
            // Publish failed event để OrderService và InventoryService biết cần compensate
            await ctx.Publish(new PaymentFailed(ctx.Message.OrderId, ex.Message));
        }
    }
}
```

---

## 6. Idempotency: chìa khóa để saga không gây double effects

Trong distributed system, message delivery có thể at-least-once. Handler phải idempotent.

```csharp
// Idempotency key pattern
public class ProcessPaymentCommandHandler : IConsumer<ProcessPaymentCommand>
{
    private readonly IPaymentGateway _gateway;
    private readonly IIdempotencyStore _idempotencyStore;

    public async Task Consume(ConsumeContext<ProcessPaymentCommand> ctx)
    {
        var idempotencyKey = $"payment:{ctx.Message.OrderId}";
        
        // Kiểm tra xem đã xử lý chưa
        if (await _idempotencyStore.HasBeenProcessedAsync(idempotencyKey))
        {
            // Lấy kết quả cũ và publish lại nếu cần
            var previousResult = await _idempotencyStore.GetResultAsync<PaymentResult>(idempotencyKey);
            await ctx.Publish(new PaymentProcessed(ctx.Message.OrderId, previousResult!.PaymentIntentId));
            return;
        }

        try
        {
            // Gửi idempotency key sang payment gateway
            var result = await _gateway.ChargeAsync(
                ctx.Message.CustomerId,
                ctx.Message.Amount,
                idempotencyKey: idempotencyKey);

            await _idempotencyStore.MarkProcessedAsync(idempotencyKey, result, TimeSpan.FromDays(7));
            
            await ctx.Publish(new PaymentProcessed(ctx.Message.OrderId, result.PaymentIntentId));
        }
        catch (PaymentDeclinedException ex)
        {
            await _idempotencyStore.MarkProcessedAsync(idempotencyKey, null, TimeSpan.FromDays(7));
            await ctx.Publish(new PaymentFailed(ctx.Message.OrderId, ex.Message));
        }
    }
}
```

---

## 7. Monitoring Saga: dashboard và alert

```csharp
// Health check cho saga lag
public class SagaLagHealthCheck : IHealthCheck
{
    private readonly IOrderSagaRepository _repo;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext ctx,
        CancellationToken ct)
    {
        // Kiểm tra xem có saga nào stuck không
        var stuckSagas = await _repo.CountAsync(
            s => s.CurrentState == "WaitingForPayment" &&
                 s.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1),
            ct);

        if (stuckSagas > 10)
            return HealthCheckResult.Degraded($"{stuckSagas} sagas stuck waiting for payment");

        if (stuckSagas > 100)
            return HealthCheckResult.Unhealthy($"{stuckSagas} sagas stuck - investigate immediately");

        return HealthCheckResult.Healthy();
    }
}
```

---

## 8. Checklist production cho Saga

- [ ] Saga state phải được persist ở database bền vững - không dùng in-memory
- [ ] Tất cả compensating transaction phải idempotent
- [ ] Đặt timeout cho mỗi bước - đừng để saga stuck mãi mãi
- [ ] Log mọi transition của saga với correlation ID
- [ ] Monitor saga lag và stuck sagas bằng metrics
- [ ] Có admin tool để manually advance hoặc reset saga bị stuck
- [ ] Test compensating transaction path đầy đủ - đây là phần hay bị bỏ qua nhất
- [ ] Đừng để saga quá dài (> 7-10 bước) - chia nhỏ nếu cần
- [ ] Dùng optimistic concurrency cho saga state để tránh race condition
- [ ] Tài liệu hóa flow của mỗi saga bằng sequence diagram
