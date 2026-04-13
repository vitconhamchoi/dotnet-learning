# MassTransit chuyên sâu, xây hệ thống message-driven trong .NET một cách thực chiến

## 1. MassTransit là gì và vì sao rất nhiều hệ .NET chọn nó

MassTransit là thư viện service bus và application framework cho .NET, giúp bạn xây dựng hệ thống **message-driven**, **asynchronous**, **event-driven** hoặc **workflow-based** trên các broker như RabbitMQ, Azure Service Bus, Amazon SQS, ActiveMQ, Kafka rider và nhiều thành phần khác.

Nếu Orleans mạnh ở mô hình Virtual Actor và stateful entity runtime, thì MassTransit mạnh ở lớp **message transport, consumer pipeline, retry, saga, scheduling, observability và integration boundary**. Nó giúp bạn biến những khái niệm vốn rất dễ rối như exchange, queue, routing key, dead-letter, outbox, saga state machine, request/response qua broker thành một mô hình .NET khá nhất quán.

Trong ứng dụng thực chiến, MassTransit thường được dùng cho:

- giao tiếp bất đồng bộ giữa microservices
- phát event domain/integration event
- xử lý command qua queue
- orchestration và saga lâu dài
- delayed delivery, retry, circuit protection
- background processing có broker bền vững
- tích hợp với ASP.NET Core, EF Core, OpenTelemetry, health check

Vấn đề mà MassTransit giúp giải quyết:

1. Giảm coupling giữa producer và consumer.
2. Cô lập workload nặng ra khỏi request HTTP đồng bộ.
3. Xử lý failure tốt hơn qua retry, outbox, dead-letter.
4. Mở rộng hệ thống theo consumer độc lập.
5. Diễn đạt workflow nghiệp vụ dài hơi bằng saga thay vì cron script rải rác.

MassTransit không phải “chỉ là wrapper RabbitMQ”. Giá trị của nó nằm ở **programming model**. Bạn định nghĩa message contract, consumer, saga, filter, policy. Thư viện lo một phần rất lớn plumbing và default tốt.

---

## 2. Mental model, message-driven architecture bằng ngôn ngữ đời thường

Hãy tưởng tượng một hệ thống bán hàng gồm các bước:

- API nhận yêu cầu đặt hàng
- tạo order trong DB
- publish `OrderSubmitted`
- inventory service reserve hàng
- payment service tạo payment intent
- notification service gửi email xác nhận
- fulfillment service chuẩn bị giao hàng

Nếu làm đồng bộ theo HTTP call nối chuỗi, bạn dễ gặp:

- request timeout
- coupling chặt giữa service
- khó retry từng bước
- khó xử lý khi service downstream chậm hoặc lỗi
- khó mở rộng thêm subscriber mới

Với MassTransit, bạn chuyển cách nghĩ sang thông điệp:

- **Command**: “hãy làm việc này”, thường nhắm tới một consumer logic cụ thể
- **Event**: “việc này đã xảy ra”, nhiều consumer có thể cùng quan tâm
- **Consumer**: mã xử lý message
- **Bus**: thành phần gửi/nhận message
- **Endpoint/queue**: nơi consumer nhận message
- **Saga**: state machine theo dõi workflow dài nhiều bước

Điểm quan trọng là producer không cần biết chi tiết ai đang xử lý event. Nó chỉ phát thông tin business cần thiết.

---

## 3. Cài đặt cơ bản với RabbitMQ

MassTransit tích hợp rất mượt với Generic Host/ASP.NET Core.

### 3.1 Package

```bash
dotnet add package MassTransit
dotnet add package MassTransit.RabbitMQ
dotnet add package MassTransit.AspNetCore
```

Nếu dùng EF Core cho saga/outbox:

```bash
dotnet add package MassTransit.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

### 3.2 Cấu hình host tối thiểu

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<SubmitOrderConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.MapGet("/health", () => Results.Ok("ok"));
app.Run();
```

`ConfigureEndpoints(context)` là điểm rất tiện. MassTransit sẽ tự tạo receive endpoint dựa trên consumer đã đăng ký và naming formatter.

---

## 4. Message contract, thiết kế cho đúng từ đầu

Message contract là trái tim của kiến trúc message-driven. Chất lượng contract ảnh hưởng trực tiếp đến khả năng evolvability của hệ thống.

### 4.1 Quy tắc thực dụng

- message nên là dữ liệu thuần, không nhúng service logic
- contract phải ổn định và rõ nghĩa nghiệp vụ
- thêm field mới theo kiểu backward-compatible
- không nhét object graph quá nặng
- có correlation id, timestamp, tenant id khi cần

Ví dụ command và event:

```csharp
public record SubmitOrder
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public List<OrderItemLine> Items { get; init; } = new();
    public DateTime SubmittedAtUtc { get; init; }
}

public record OrderSubmitted
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

public record OrderItemLine
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
```

`record` rất hợp cho contract vì immutable tương đối và rõ ý.

### 4.2 Command hay event

Một rule đơn giản:

- **Command** dùng khi bạn đang chỉ định hệ thống nên làm gì
- **Event** dùng khi bạn thông báo việc gì đã xảy ra

Ví dụ:

- `SubmitOrder`: command
- `OrderSubmitted`: event
- `ReserveInventory`: command
- `InventoryReserved`: event
- `PaymentFailed`: event

Sai lầm phổ biến là đặt mọi message thành event, khiến intent mơ hồ.

---

## 5. Consumer cơ bản, bắt đầu từ use case đặt hàng

### 5.1 Consumer nhận command

```csharp
public class SubmitOrderConsumer : IConsumer<SubmitOrder>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<SubmitOrderConsumer> _logger;
    private readonly IPublishEndpoint _publishEndpoint;

    public SubmitOrderConsumer(
        OrderDbContext db,
        ILogger<SubmitOrderConsumer> logger,
        IPublishEndpoint publishEndpoint)
    {
        _db = db;
        _logger = logger;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<SubmitOrder> context)
    {
        var message = context.Message;

        if (await _db.Orders.AnyAsync(x => x.Id == message.OrderId))
        {
            _logger.LogInformation("Order {OrderId} already exists, skipping duplicate", message.OrderId);
            return;
        }

        var order = new Order
        {
            Id = message.OrderId,
            UserId = message.UserId,
            Status = "Submitted",
            SubmittedAtUtc = message.SubmittedAtUtc,
            TotalAmount = message.Items.Sum(x => x.Quantity * x.UnitPrice),
            Items = message.Items.Select(x => new OrderItem
            {
                Sku = x.Sku,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice
            }).ToList()
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(context.CancellationToken);

        await _publishEndpoint.Publish(new OrderSubmitted
        {
            OrderId = order.Id,
            UserId = order.UserId,
            TotalAmount = order.TotalAmount,
            OccurredAtUtc = DateTime.UtcNow
        }, context.CancellationToken);
    }
}
```

Ở đây có các điểm thực chiến:

- kiểm tra duplicate theo `OrderId`
- lưu DB trước rồi mới phát event
- event dùng cho downstream systems

### 5.2 Gửi command từ API

```csharp
app.MapPost("/orders", async (
    CreateOrderHttpRequest request,
    IPublishEndpoint publishEndpoint) =>
{
    var orderId = Guid.NewGuid();

    await publishEndpoint.Publish(new SubmitOrder
    {
        OrderId = orderId,
        UserId = request.UserId,
        Items = request.Items.Select(x => new OrderItemLine
        {
            Sku = x.Sku,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice
        }).ToList(),
        SubmittedAtUtc = DateTime.UtcNow
    });

    return Results.Accepted($"/orders/{orderId}", new { orderId });
});

public record CreateOrderHttpRequest(string UserId, List<CreateOrderItemHttpRequest> Items);
public record CreateOrderItemHttpRequest(string Sku, int Quantity, decimal UnitPrice);
```

Trong thực tế, với command, bạn cũng có thể dùng `ISendEndpointProvider` để gửi vào queue đích thay vì publish broadcast.

---

## 6. Publish và Send, khác nhau ra sao

Đây là chỗ nhiều người mới dùng MassTransit nhầm.

### 6.1 Publish

`Publish` phát một event cho mọi subscriber tương ứng.

```csharp
await publishEndpoint.Publish(new OrderSubmitted { ... });
```

Dùng khi:

- nhiều service có thể quan tâm
- producer không muốn phụ thuộc đích cụ thể
- semantic là “đã xảy ra rồi”

### 6.2 Send

`Send` gửi message đến một endpoint cụ thể.

```csharp
var endpoint = await sendEndpointProvider.GetSendEndpoint(
    new Uri("queue:reserve-inventory"));

await endpoint.Send(new ReserveInventory
{
    OrderId = orderId,
    Items = items
});
```

Dùng khi:

- biết rõ queue nhận lệnh
- semantic là command có một handler chính
- muốn route chủ động

### 6.3 Chọn thế nào

- event business fact: `Publish`
- command workflow step: thường `Send`
- integration event nhiều subscriber: `Publish`
- internal task queue rõ đích: `Send`

Đừng dùng bừa một kiểu cho mọi thứ.

---

## 7. Receive endpoint, queue topology và naming

MassTransit giúp bạn trừu tượng hóa topology nhưng không nên mù mờ hoàn toàn.

Khi cấu hình:

```csharp
x.SetKebabCaseEndpointNameFormatter();
x.AddConsumer<SubmitOrderConsumer>();
```

và sau đó:

```csharp
cfg.ConfigureEndpoints(context);
```

MassTransit sẽ tạo endpoint như `submit-order` hoặc tên tương ứng formatter. Một số hệ thống thích chỉ định endpoint name rõ ràng:

```csharp
x.AddConsumer<SubmitOrderConsumer>()
    .Endpoint(e => e.Name = "order-submit");
```

Cấu hình receive endpoint thủ công khi cần policy riêng:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host("localhost", "/");

    cfg.ReceiveEndpoint("order-submit", e =>
    {
        e.PrefetchCount = 16;
        e.ConcurrentMessageLimit = 8;
        e.ConfigureConsumer<SubmitOrderConsumer>(context);
    });
});
```

Bạn nên hiểu ít nhất:

- một consumer không nhất thiết một queue, nhưng thường map khá gần
- queue là nơi nhận message
- exchange/topic được MassTransit bind tùy transport
- topology khác nhau giữa RabbitMQ và Azure Service Bus

---

## 8. Retry, redelivery, fault handling, phần cực kỳ quan trọng

Trong distributed systems, lỗi là mặc định. MassTransit hỗ trợ retry rất tốt nhưng cần dùng đúng.

### 8.1 Immediate retry

```csharp
x.AddConsumer<SubmitOrderConsumer>(cfg =>
{
    cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
});
```

Phù hợp với lỗi tạm thời ngắn như network hiccup, transient SQL error.

### 8.2 Delayed redelivery

```csharp
x.AddConsumer<SubmitOrderConsumer>(cfg =>
{
    cfg.UseDelayedRedelivery(r => r.Intervals(
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)));

    cfg.UseMessageRetry(r => r.Immediate(3));
});
```

Phù hợp khi dependency ngoài đang down lâu hơn, ví dụ payment provider hoặc email service.

### 8.3 Lỗi cuối cùng và fault event

Khi consumer fail sau tất cả retry, MassTransit có thể publish `Fault<T>`.

```csharp
public class SubmitOrderFaultConsumer : IConsumer<Fault<SubmitOrder>>
{
    private readonly ILogger<SubmitOrderFaultConsumer> _logger;

    public SubmitOrderFaultConsumer(ILogger<SubmitOrderFaultConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<Fault<SubmitOrder>> context)
    {
        _logger.LogError("SubmitOrder failed permanently for OrderId {OrderId}",
            context.Message.Message.OrderId);

        return Task.CompletedTask;
    }
}
```

### 8.4 Đừng retry mọi lỗi

Ví dụ validation lỗi, data lỗi, duplicate business rule, “SKU không tồn tại”, “số dư không đủ” thường không nên retry mù quáng.

```csharp
cfg.UseMessageRetry(r =>
{
    r.Ignore<ValidationException>();
    r.Ignore<BusinessRuleException>();
    r.Interval(5, TimeSpan.FromSeconds(1));
});
```

Đây là khác biệt giữa hệ ổn định và hệ tự DDoS chính nó khi có dữ liệu xấu.

---

## 9. Middleware pipeline, filters và cross-cutting concerns

MassTransit có pipeline rất mạnh. Bạn có thể cắm behavior cho consume/send/publish.

Các use case phổ biến:

- thêm correlation id
- tenant resolution
- audit log
- metrics
- auth metadata nội bộ
- standardized exception mapping

Ví dụ filter consume đơn giản:

```csharp
public class LoggingConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly ILogger<LoggingConsumeFilter<T>> _logger;

    public LoggingConsumeFilter(ILogger<LoggingConsumeFilter<T>> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("loggingConsumeFilter");
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        _logger.LogInformation("Consuming {MessageType} CorrelationId={CorrelationId}",
            typeof(T).Name,
            context.CorrelationId);

        await next.Send(context);
    }
}
```

Đăng ký:

```csharp
cfg.UseConsumeFilter(typeof(LoggingConsumeFilter<>), context);
```

---

## 10. Request/Response qua bus

Dù message-driven thiên async, vẫn có những use case cần request/response nhưng không muốn gọi HTTP trực tiếp. MassTransit hỗ trợ request client.

### 10.1 Contract

```csharp
public record GetInventoryStatus
{
    public string Sku { get; init; } = string.Empty;
}

public record InventoryStatusResult
{
    public string Sku { get; init; } = string.Empty;
    public int Available { get; init; }
}
```

### 10.2 Consumer

```csharp
public class GetInventoryStatusConsumer : IConsumer<GetInventoryStatus>
{
    private readonly InventoryDbContext _db;

    public GetInventoryStatusConsumer(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<GetInventoryStatus> context)
    {
        var sku = context.Message.Sku;
        var available = await _db.Inventory
            .Where(x => x.Sku == sku)
            .Select(x => x.Available)
            .SingleOrDefaultAsync(context.CancellationToken);

        await context.RespondAsync(new InventoryStatusResult
        {
            Sku = sku,
            Available = available
        });
    }
}
```

### 10.3 Request client

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<GetInventoryStatusConsumer>();
    x.AddRequestClient<GetInventoryStatus>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
});
```

```csharp
app.MapGet("/inventory/{sku}", async (string sku, IRequestClient<GetInventoryStatus> client) =>
{
    var response = await client.GetResponse<InventoryStatusResult>(new GetInventoryStatus
    {
        Sku = sku
    });

    return Results.Ok(response.Message);
});
```

Pattern này hữu ích, nhưng đừng lạm dụng đến mức biến broker thành RPC sync cho mọi thứ. Broker phù hợp nhất với async decoupling.

---

## 11. Consumer definitions, gom policy cho gọn

Khi hệ thống nhiều consumer, bỏ hết policy vào lambda đăng ký sẽ rối. `ConsumerDefinition<T>` giúp tổ chức tốt hơn.

```csharp
public class SubmitOrderConsumerDefinition : ConsumerDefinition<SubmitOrderConsumer>
{
    public SubmitOrderConsumerDefinition()
    {
        EndpointName = "order-submit";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SubmitOrderConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(2)));
        endpointConfigurator.UseInMemoryOutbox(context);
    }
}
```

Đăng ký:

```csharp
x.AddConsumer<SubmitOrderConsumer, SubmitOrderConsumerDefinition>();
```

Rất hợp cho codebase lớn.

---

## 12. In-memory outbox và transactional outbox, tránh side effect lặp

Đây là tính năng cực kỳ quan trọng.

### 12.1 Vì sao cần outbox

Giả sử consumer:

1. lưu DB thành công
2. publish event
3. giữa bước 2 và acknowledge message bị crash

Khi message được redeliver, có thể logic chạy lại và side effect bị lặp. Outbox giúp kiểm soát việc phát message chỉ khi transaction nội bộ commit và deduplicate tốt hơn.

### 12.2 In-memory outbox

```csharp
endpointConfigurator.UseInMemoryOutbox(context);
```

Nó gom các publish/send trong consume pipeline và chỉ phát khi consumer thành công. Phù hợp cho nhiều trường hợp đơn giản nhưng không thay thế persistent outbox khi cần đảm bảo cao hơn qua crash boundary.

### 12.3 EF transactional outbox

Ví dụ với Entity Framework:

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConsumer<SubmitOrderConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
});
```

Lợi ích:

- message outgoing được lưu cùng transaction DB
- background delivery service publish sau khi transaction commit
- giảm nguy cơ inconsistency giữa DB và bus

Khi hệ thống liên quan order, payment, fulfillment, outbox gần như là bắt buộc nếu bạn muốn ngủ ngon.

---

## 13. Saga, trái tim của workflow dài hơi

Saga dùng khi nghiệp vụ kéo dài qua nhiều bước, nhiều service, không thể gói trong một DB transaction duy nhất.

Ví dụ flow order:

1. Order submitted
2. Reserve inventory
3. Request payment
4. Payment succeeded hoặc failed
5. Nếu succeeded, mark completed
6. Nếu failed, release inventory và cancel order

MassTransit hỗ trợ saga theo hai kiểu chính:

- consumer saga đơn giản
- state machine saga rất mạnh và phổ biến

### 13.1 Saga state instance

```csharp
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public int Version { get; set; }
}
```

### 13.2 Events cho saga

```csharp
public record InventoryReserved
{
    public Guid OrderId { get; init; }
}

public record InventoryReservationFailed
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public record PaymentSucceeded
{
    public Guid OrderId { get; init; }
    public string TransactionId { get; init; } = string.Empty;
}

public record PaymentFailed
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
```

### 13.3 State machine

```csharp
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State AwaitingInventory { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReserved { get; private set; } = null!;
    public Event<InventoryReservationFailed> InventoryReservationFailed { get; private set; } = null!;
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReserved, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReservationFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.UserId = context.Message.UserId;
                    context.Saga.TotalAmount = context.Message.TotalAmount;
                    context.Saga.SubmittedAtUtc = context.Message.OccurredAtUtc;
                })
                .Send(new Uri("queue:reserve-inventory"), context => new ReserveInventory
                {
                    OrderId = context.Message.OrderId
                })
                .TransitionTo(AwaitingInventory));

        During(AwaitingInventory,
            When(InventoryReserved)
                .Send(new Uri("queue:request-payment"), context => new RequestPayment
                {
                    OrderId = context.Message.OrderId,
                    Amount = context.Saga.TotalAmount
                })
                .TransitionTo(AwaitingPayment),

            When(InventoryReservationFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .TransitionTo(Failed)
                .Finalize());

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .Then(context => context.Saga.CompletedAtUtc = DateTime.UtcNow)
                .Publish(context => new OrderCompleted
                {
                    OrderId = context.Message.OrderId
                })
                .TransitionTo(Completed)
                .Finalize(),

            When(PaymentFailed)
                .Then(context => context.Saga.FailureReason = context.Message.Reason)
                .Send(new Uri("queue:release-inventory"), context => new ReleaseInventory
                {
                    OrderId = context.Message.OrderId
                })
                .TransitionTo(Failed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
```

MassTransit state machine rất expressive. Bạn mô tả trạng thái, sự kiện, hành vi chuyển trạng thái trong một nơi tập trung. Đây là cách rất tốt để làm rõ workflow business phức tạp.

### 13.4 Đăng ký saga với EF Core repository

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, OrderSagaDbContext>((provider, options) =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("SagaDb"));
            });
        });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
});
```

Sử dụng optimistic concurrency là lựa chọn phổ biến cho saga state repository.

---

## 14. Scheduling và delayed message

Hệ thống nghiệp vụ thường có timeout hoặc hẹn giờ.

Ví dụ:

- nếu 15 phút chưa thanh toán thì hủy đơn
- sau 24 giờ gửi email nhắc thanh toán
- hẹn retry một tác vụ vào giờ thấp điểm

MassTransit hỗ trợ delayed/scheduled message tùy transport và scheduler.

### 14.1 Dùng delayed scheduler với RabbitMQ

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host("localhost");
    cfg.UseDelayedMessageScheduler();
    cfg.ConfigureEndpoints(context);
});
```

### 14.2 Schedule message

```csharp
public record CancelOrderIfUnpaid
{
    public Guid OrderId { get; init; }
}
```

```csharp
await context.ScheduleSend(
    new Uri("queue:cancel-unpaid-order"),
    DateTime.UtcNow.AddMinutes(15),
    new CancelOrderIfUnpaid { OrderId = orderId });
```

Consumer:

```csharp
public class CancelOrderIfUnpaidConsumer : IConsumer<CancelOrderIfUnpaid>
{
    private readonly OrderDbContext _db;

    public CancelOrderIfUnpaidConsumer(OrderDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<CancelOrderIfUnpaid> context)
    {
        var order = await _db.Orders.FindAsync(new object[] { context.Message.OrderId }, context.CancellationToken);
        if (order is null || order.Status == "Paid") return;

        order.Status = "Cancelled";
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
```

Đây là cách sạch hơn nhiều so với quét DB bằng cron mỗi phút nếu nghiệp vụ gắn tự nhiên với sự kiện ban đầu.

---

## 15. Routing slip, courier pattern cho pipeline nhiều bước

Ngoài saga, MassTransit còn có **routing slip** cho workflow theo activity chain. Pattern này hợp với pipeline kỹ thuật, ví dụ:

- xử lý file upload
- biến đổi ảnh/video
- ETL document
- pipeline provisioning nhiều bước ít cần state machine business phức tạp

Mỗi activity có execute/compensate. Tức là gần giống mini-workflow với rollback compensation.

Ví dụ ý tưởng:

1. Validate document
2. Virus scan
3. OCR
4. Index search
5. Notify user

Nếu OCR fail sau bước 2 và 3, bạn có thể compensating action cho bước trước nếu cần. Đây là khả năng rất đáng giá mà nhiều đội chưa tận dụng đủ.

---

## 16. Concurrency, throughput tuning và backpressure

Khi hệ thống bắt đầu lớn, bạn phải hiểu throughput không chỉ nằm ở broker mà còn ở consumer process và downstream DB/API.

### 16.1 Một vài knob quan trọng

```csharp
cfg.ReceiveEndpoint("order-submit", e =>
{
    e.PrefetchCount = 32;
    e.ConcurrentMessageLimit = 16;
    e.ConfigureConsumer<SubmitOrderConsumer>(context);
});
```

- `PrefetchCount`: broker đẩy sẵn bao nhiêu message
- `ConcurrentMessageLimit`: tối đa bao nhiêu message xử lý đồng thời

Nếu DB chỉ chịu được 8 connection write nặng, để concurrency 100 sẽ không thần kỳ hơn, chỉ làm hệ thống đổ nhanh hơn.

### 16.2 Phân loại consumer theo tính chất workload

- CPU-bound: scale process, concurrency vừa phải, tránh blocking
- IO-bound DB/API: tune theo downstream capacity
- strict ordering per key: cân nhắc partition hoặc thiết kế queue/key phù hợp

### 16.3 Đừng quên idempotency

Tăng concurrency mà thiếu idempotency thì duplicate effect sẽ thành ác mộng.

---

## 17. Idempotency, duplicate delivery và tư duy “at least once”

Hầu hết broker và pipeline thực chiến nên được hiểu theo hướng **at least once delivery**. Tức là message có thể đến hơn một lần.

Vì vậy, consumer phải thiết kế idempotent nếu side effect quan trọng.

Ví dụ tạo invoice:

```csharp
public class GenerateInvoiceConsumer : IConsumer<OrderCompleted>
{
    private readonly BillingDbContext _db;

    public GenerateInvoiceConsumer(BillingDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<OrderCompleted> context)
    {
        if (await _db.Invoices.AnyAsync(x => x.OrderId == context.Message.OrderId))
            return;

        _db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            OrderId = context.Message.OrderId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
```

Các chiến lược idempotency thường dùng:

- unique constraint ở DB
- dedupe table theo message id/business id
- outbox/inbox pattern
- natural idempotency qua trạng thái hiện có

Nếu bạn chưa nghĩ về duplicate, bạn chưa thật sự thiết kế hệ message-driven.

---

## 18. Error queue, poison message và quy trình vận hành

Không phải message nào cũng cứu được. Có message dữ liệu sai, schema lệch, bug code hoặc business impossible.

Bạn cần có quy trình rõ:

1. message lỗi cuối cùng đi vào error queue/dead-letter
2. alert khi số lượng lỗi tăng bất thường
3. log đầy đủ correlation id, message type, exception
4. có script hoặc tool để inspect/replay cẩn thận
5. phân biệt replay an toàn và replay nguy hiểm

MassTransit làm phần plumbing khá tốt, nhưng vận hành vẫn là trách nhiệm của team.

Một lời khuyên thực tế: đừng replay hàng loạt mù quáng nếu side effect không idempotent.

---

## 19. Observability, tracing, metrics và health checks

Khi hệ thống message-driven gặp sự cố, thứ cứu bạn không phải niềm tin mà là telemetry.

Bạn nên quan sát được:

- throughput theo endpoint
- consume duration
- retry count
- fault count
- queue depth ở broker
- saga state transitions
- outbox lag
- scheduler backlog

MassTransit hỗ trợ tốt với logging và OpenTelemetry. Ngoài ra nên dùng health checks của transport và app host.

Ví dụ:

```csharp
builder.Services.AddHealthChecks();
```

```csharp
app.MapHealthChecks("/health");
```

Cùng với broker metrics, đây là baseline tối thiểu cho production.

---

## 20. Testing MassTransit

### 20.1 Test harness

MassTransit có test harness rất hữu ích.

```csharp
public class SubmitOrderConsumerTests
{
    [Fact]
    public async Task Should_publish_order_submitted_after_consuming_submit_order()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<SubmitOrderConsumer>();
            })
            .AddDbContext<OrderDbContext>(options => options.UseInMemoryDatabase("orders"))
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new SubmitOrder
        {
            OrderId = Guid.NewGuid(),
            UserId = "u1",
            SubmittedAtUtc = DateTime.UtcNow,
            Items = new List<OrderItemLine>
            {
                new() { Sku = "SKU-1", Quantity = 2, UnitPrice = 10 }
            }
        });

        Assert.True(await harness.Consumed.Any<SubmitOrder>());
        Assert.True(await harness.Published.Any<OrderSubmitted>());
    }
}
```

Test harness cho phép assert:

- message đã consume chưa
- message nào được publish/send
- saga state đã tạo/chuyển chưa

### 20.2 Test saga

Với saga, bạn có thể assert instance tồn tại và state chuyển đúng. Đây là chỗ cực nên test vì workflow business rất dễ lệch logic khi refactor.

---

## 21. Multi-bus và boundary rõ ràng

Một số hệ thống cần nhiều bus hoặc nhiều transport:

- bus nội bộ service-to-service trên RabbitMQ
- rider Kafka cho analytics event stream
- Azure Service Bus cho integration với hạ tầng enterprise

MassTransit hỗ trợ các pattern này, nhưng lời khuyên thực tế là:

- chỉ dùng multi-bus khi thật sự cần
- boundary của từng bus phải rõ
- contract ownership phải rõ
- tránh tạo mê cung topology chỉ vì framework hỗ trợ được

---

## 22. Kafka rider, khi bạn cần event streaming

MassTransit không chỉ chơi với queue broker truyền thống. Nó còn có Kafka rider để bạn tiêu thụ/publish stream event trong khi vẫn giữ mô hình consumer nhất quán hơn.

Điều này hữu ích khi:

- cần analytics/event streaming
- cần tích hợp vào data pipeline lớn
- vẫn muốn giữ code application thống nhất trong .NET

Tuy vậy, Kafka semantics khác RabbitMQ khá nhiều. Đừng giả định mọi transport giống nhau. Ordering, partition, consumer group, replay behavior phải được hiểu đúng theo transport bạn chọn.

---

## 23. Mẫu kiến trúc thực chiến cho hệ order/payment

Một cách tổ chức khá cân bằng:

### Service A, Order API

- nhận HTTP create order
- lưu order DB cục bộ
- dùng outbox publish `OrderSubmitted`

### Service B, Inventory Worker

- consume `OrderSubmitted`
- reserve inventory
- publish `InventoryReserved` hoặc `InventoryReservationFailed`

### Service C, Payment Worker

- consume `InventoryReserved`
- gọi payment provider
- publish `PaymentSucceeded` hoặc `PaymentFailed`

### Service D, Order Saga/Coordinator

- state machine theo dõi tiến trình
- phát command release inventory khi cần
- publish `OrderCompleted` hoặc `OrderCancelled`

### Service E, Notification Worker

- subscribe `OrderCompleted`, `PaymentFailed`, `OrderCancelled`
- gửi email/SMS

Kiến trúc này tách trách nhiệm rõ ràng, side effect tách rời, dễ mở rộng subscriber mới.

---

## 24. Những lỗi người mới dùng MassTransit hay mắc

1. **Không phân biệt send và publish**.
2. **Consumer không idempotent**.
3. **Retry mọi exception** kể cả validation lỗi.
4. **Không dùng outbox** nhưng tưởng hệ đã “exactly once”.
5. **Nhét logic business lớn vào API rồi chỉ publish event phụ**. Khi đó bus chỉ làm trang trí.
6. **Thiết kế message contract quá giống entity DB** nên coupling schema dữ dội.
7. **Thiếu correlation id và observability** khiến debug cross-service cực khó.
8. **Không có chiến lược error queue replay**.
9. **Saga không có persistence tốt hoặc không test transition**.
10. **Dùng request/response qua broker quá nhiều** làm mất giá trị async decoupling.

---

## 25. Khi nào nên dùng MassTransit, khi nào không

### Nên dùng khi

- hệ thống có nhiều xử lý bất đồng bộ
- cần broker-based decoupling
- nhiều microservice/subsystem cần trao đổi event
- có workflow dài cần saga
- muốn chuẩn hóa retry, outbox, consumer pipeline trong .NET

### Không nhất thiết dùng khi

- ứng dụng CRUD đơn giản một service một DB
- không có nhu cầu async đáng kể
- team chưa sẵn sàng vận hành broker
- yêu cầu business quá nhỏ, background queue đơn giản có thể dùng Hangfire hoặc hosted service là đủ

MassTransit là công cụ tuyệt vời, nhưng không phải mọi project đều cần service bus.

---

## 26. So sánh nhanh với lựa chọn khác

### So với raw RabbitMQ client

- Raw client cho nhiều quyền kiểm soát nhưng rất nhiều plumbing phải tự làm
- MassTransit cung cấp convention, pipeline, retry, saga, test harness, observability tốt hơn nhiều

### So với NServiceBus

- NServiceBus rất mạnh nhưng có licensing/commercial considerations
- MassTransit phổ biến mạnh trong cộng đồng .NET open source, linh hoạt và hiện đại

### So với CAP hoặc Wolverine

- CAP đơn giản hơn cho event bus/outbox cơ bản
- Wolverine thiên về local-first message bus + command handling mạnh
- MassTransit nổi bật ở ecosystem transport, saga state machine và maturity lâu năm

---

## 27. Ví dụ cấu hình production-friendly hơn

```csharp
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<SubmitOrderConsumer, SubmitOrderConsumerDefinition>();
    x.AddConsumer<GenerateInvoiceConsumer>();
    x.AddConsumer<CancelOrderIfUnpaidConsumer>();
    x.AddConsumer<SubmitOrderFaultConsumer>();

    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, OrderSagaDbContext>((provider, options) =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("SagaDb"));
            });
        });

    x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]);
            h.Password(builder.Configuration["RabbitMq:Password"]);
        });

        cfg.UseDelayedMessageScheduler();
        cfg.ConfigureEndpoints(context);
    });
});
```

Đây chưa phải cấu hình cuối cùng cho mọi hệ thống, nhưng là điểm khởi đầu khá vững cho ứng dụng production thật.

---

## 28. Checklist triển khai MassTransit thực chiến

- Xác định message contract ownership
- Phân biệt command/event rõ ràng
- Chọn transport theo hạ tầng và semantics phù hợp
- Đặt naming convention endpoint rõ ràng
- Thêm retry có chọn lọc
- Dùng outbox cho write + publish consistency
- Thiết kế consumer idempotent
- Có error queue monitoring và replay process
- Với workflow dài, dùng saga và test transition
- Bổ sung metrics, tracing, logs, correlation id
- Tuning concurrency theo downstream capacity
- Tài liệu hóa topology và contract versioning

---

## 29. Lộ trình học MassTransit hiệu quả

Nếu mới bắt đầu, bạn có thể học theo thứ tự này:

1. consumer cơ bản, publish/send
2. retry, fault, error queue
3. request/response qua bus
4. endpoint naming và topology
5. in-memory outbox rồi transactional outbox
6. saga state machine
7. scheduling và timeout
8. observability, test harness, production tuning

Sau chuỗi này, bạn sẽ hiểu MassTransit không chỉ là thư viện gửi message, mà là một framework hoàn chỉnh để xây hệ thống integration và workflow trong .NET.

---

## 30. Mẫu endpoint orchestration đầy đủ hơn, từ API đến consumer

Để nối các phần lại với nhau, ta có thể dựng một API nhỏ tạo order rồi để bus xử lý phần sau.

```csharp
app.MapPost("/checkout", async (
    CheckoutHttpRequest request,
    OrderDbContext db,
    IPublishEndpoint publishEndpoint,
    CancellationToken cancellationToken) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        UserId = request.UserId,
        Status = "PendingSubmission",
        SubmittedAtUtc = DateTime.UtcNow,
        TotalAmount = request.Items.Sum(x => x.Quantity * x.UnitPrice),
        Items = request.Items.Select(x => new OrderItem
        {
            Sku = x.Sku,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice
        }).ToList()
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync(cancellationToken);

    await publishEndpoint.Publish(new SubmitOrder
    {
        OrderId = order.Id,
        UserId = order.UserId,
        SubmittedAtUtc = order.SubmittedAtUtc,
        Items = order.Items.Select(x => new OrderItemLine
        {
            Sku = x.Sku,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice
        }).ToList()
    }, cancellationToken);

    return Results.Accepted($"/orders/{order.Id}", new { order.Id });
});

public record CheckoutHttpRequest(string UserId, List<CheckoutItemHttpRequest> Items);
public record CheckoutItemHttpRequest(string Sku, int Quantity, decimal UnitPrice);
```

Một biến thể production-friendly hơn là không `Publish` trực tiếp từ endpoint, mà ghi outbox record cùng transaction DB rồi để outbox delivery service phát message. Điều này tránh tình huống API lưu DB thành công nhưng publish thất bại giữa chừng.

---

## 31. Versioning contract và chiến lược nâng cấp không làm gãy hệ thống

Versioning message là kỷ luật rất quan trọng.

Một vài nguyên tắc thực tế:

1. ưu tiên **thêm field mới** thay vì đổi nghĩa field cũ
2. tránh xóa field ngay khi chưa chắc mọi consumer cũ đã nâng cấp
3. dùng event mới khi semantic thay đổi mạnh, ví dụ `OrderSubmittedV2` hoặc message name mới rõ hơn
4. tài liệu hóa ownership của từng contract
5. nếu public integration contract cho đối tác ngoài, hãy bảo thủ hơn nhiều so với internal contract

Ví dụ thêm field mới an toàn hơn:

```csharp
public record OrderSubmitted
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string? CouponCode { get; init; }
}
```

Consumer cũ không cần dùng `CouponCode` vẫn có thể chạy. Nhưng nếu bạn đổi nghĩa `TotalAmount` từ gross sang net mà vẫn giữ tên cũ, bạn sẽ tạo bug business rất khó phát hiện.

---

## 32. Kết luận

MassTransit rất đáng giá khi bạn cần xây hệ thống .NET theo hướng **message-driven, event-driven và resilient**. Giá trị lớn nhất của nó là giúp đội ngũ tập trung vào **nghiệp vụ và contract** thay vì chìm trong chi tiết broker plumbing. Consumer, saga, outbox, retry, scheduling, test harness, topology convention, tất cả kết hợp lại tạo nên một nền rất mạnh cho distributed application.

Tuy nhiên, để dùng tốt, bạn phải chấp nhận vài nguyên tắc sống còn:

- message có thể delivery hơn một lần
- failure là bình thường
- outbox không phải tính năng xa xỉ
- saga cần persistence và test cẩn thận
- observability không được làm sau cùng

Nếu team của bạn đang ở ngưỡng từ monolith hoặc CRUD service bước sang thế giới integration bất đồng bộ, MassTransit là một lựa chọn cực sáng giá trong hệ .NET. Bắt đầu từ một workflow nhỏ như order submitted hoặc payment requested, dựng contract cho sạch, thêm outbox và retry đúng cách, rồi mở rộng dần. Làm như vậy, bạn sẽ có một hệ thống linh hoạt hơn, chịu lỗi tốt hơn, và dễ tiến hóa hơn rất nhiều so với cách nối service bằng những lời gọi đồng bộ mong manh.
