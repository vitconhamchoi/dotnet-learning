# Wolverine chuyên sâu, durable command handling, messaging và application workflow thực chiến trong .NET

## Wolverine là gì, và nó giải bài toán nào

Wolverine là một thư viện .NET tập trung vào message-driven application development, durable execution, command handling, background processing và integration messaging. Nếu phải tóm tắt trong một câu dễ nhớ, Wolverine là công cụ giúp bạn viết application logic theo kiểu handler-first, đồng thời giải quyết phần khó nhất của hệ thống phân tán là độ bền của message, retry, scheduling, transport, outbox, inbox và execution pipeline.

Nhiều người mới nhìn Wolverine thường hỏi: nó là MassTransit, MediatR, Hangfire, hay NServiceBus? Câu trả lời đúng nhất là Wolverine chạm vào một phần đất của tất cả những cái tên đó, nhưng trọng tâm thiết kế của nó khác. Wolverine không cố làm một “mediator trong process” đơn giản, cũng không chỉ là một abstraction quanh broker. Nó cố trở thành xương sống cho application workflow hiện đại trong .NET, nơi command có thể bắt đầu từ HTTP, từ queue, từ scheduled work, từ local background task, và tất cả cùng đi qua một mô hình handler thống nhất.

Wolverine rất mạnh trong các bài toán như:

- xử lý command theo handler rõ ràng
- publish/subcribe message bền vững
- tiêu thụ message từ broker hoặc từ local queue
- retry thông minh khi handler lỗi
- delayed/scheduled execution
- tích hợp HTTP endpoint theo kiểu pipeline liền mạch
- kết hợp với Marten để giải quyết dual write qua outbox/inbox
- tổ chức background work mà không phải dựng riêng một stack queue/job nặng nề

Nếu Marten mạnh về state, stream và projection, thì Wolverine mạnh về execution, message flow và application durability. Hai thư viện này ghép với nhau rất tự nhiên, đến mức nhiều team xem chúng như một stack đồng bộ cho domain-centric distributed app.

---

## Khi nào nên dùng Wolverine

### Wolverine rất hợp khi

- Bạn muốn tổ chức app theo command/query/message handler thay vì controller/service truyền thống nặng nề.
- Bạn có asynchronous workflow, background processing hoặc integration event.
- Bạn muốn durability nhưng không muốn tự viết outbox/inbox/retry/scheduler.
- Bạn dùng Marten và muốn transaction story sạch hơn.
- Bạn muốn cùng một programming model cho local messages, HTTP-triggered commands và broker messages.

### Wolverine không phải lựa chọn tối ưu khi

- Ứng dụng của bạn chỉ là CRUD đồng bộ đơn giản, gần như không có background workflow hay messaging.
- Team chưa sẵn sàng học message-driven design, idempotency, retry semantics.
- Bạn đã đầu tư rất sâu vào một messaging stack khác như MassTransit hoặc NServiceBus và không có nhu cầu đổi trục.

### So với MediatR

MediatR giải quyết dispatch in-process rất nhẹ. Wolverine giải quyết nhiều hơn rất nhiều:

- durable messaging
- queues local/remote
- retries
- scheduled messages
- transports
- middleware/execution policies
- outbox/inbox
- handler discovery mạnh hơn
- tích hợp HTTP và background agents

Nếu MediatR giống một cái dispatcher trong code, Wolverine giống một execution backbone cho cả application.

---

## Tư duy cốt lõi của Wolverine

Để dùng Wolverine tốt, cần hiểu vài khái niệm trụ cột.

### 1. Message là đơn vị công việc

Message có thể là command, event, request hay instruction. Wolverine không ép bạn phải có interface marker phức tạp. Một record hoặc class bình thường là đủ:

```csharp
public record CreateInvoice(Guid CustomerId, decimal Amount);
public record InvoiceCreated(Guid InvoiceId, Guid CustomerId, decimal Amount);
public record SendInvoiceEmail(Guid InvoiceId, string Email);
```

### 2. Handler là nơi thực thi nghiệp vụ

Thay vì nhét logic vào controller, service layer chung chung, rồi background job gọi vòng vèo, Wolverine khuyến khích đặt logic vào handler rõ ràng.

```csharp
public static class CreateInvoiceHandler
{
    public static InvoiceCreated Handle(CreateInvoice command)
    {
        var invoiceId = Guid.NewGuid();
        return new InvoiceCreated(invoiceId, command.CustomerId, command.Amount);
    }
}
```

Handler có thể trả về message khác, object response, hoặc ghi side effect qua dependency injection.

### 3. Delivery semantics rất quan trọng

Wolverine quan tâm mạnh tới chuyện message được xử lý ra sao khi lỗi, có retry không, có delay không, có cần durable không, có gửi qua broker không. Đây là phần mà framework tạo giá trị lớn hơn một mediator đơn giản.

### 4. Local first nhưng không bị khóa trong process

Một command có thể được invoke trong memory, queued local, hoặc gửi qua RabbitMQ/Azure Service Bus. Programming model gần như không đổi quá nhiều, nhưng độ bền và topology thay đổi theo cấu hình.

---

## Cài đặt và bootstrap cơ bản

### Packages

```bash
dotnet new webapi -n ShippingService
cd ShippingService
dotnet add package WolverineFx
```

Nếu cần HTTP endpoint integration:

```bash
dotnet add package WolverineFx.Http
```

Nếu dùng với Marten:

```bash
dotnet add package Marten
dotnet add package WolverineFx.Marten
```

Nếu dùng transport như RabbitMQ:

```bash
dotnet add package WolverineFx.RabbitMQ
```

### Program.cs tối thiểu

```csharp
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine();

var app = builder.Build();

app.MapGet("/", () => "Shipping service is running");

app.Run();
```

Chỉ vậy là Wolverine đã bắt đầu scan handler và dựng runtime cơ bản.

---

## Handler discovery và các kiểu handler phổ biến

Wolverine có convention mạnh. Bạn có thể viết handler theo nhiều kiểu, nhưng phổ biến nhất là static method hoặc instance class.

### Handler dạng static

```csharp
public record CreateShipment(Guid OrderId, string Address);
public record ShipmentCreated(Guid ShipmentId, Guid OrderId, string Address);

public static class CreateShipmentHandler
{
    public static ShipmentCreated Handle(CreateShipment command)
    {
        var shipmentId = Guid.NewGuid();
        return new ShipmentCreated(shipmentId, command.OrderId, command.Address);
    }
}
```

### Handler inject dependency

```csharp
public interface IShipmentNumberGenerator
{
    string Next();
}

public static class CreateShipmentHandler
{
    public static ShipmentCreated Handle(CreateShipment command, IShipmentNumberGenerator generator)
    {
        var shipmentId = Guid.NewGuid();
        var shipmentNo = generator.Next();

        return new ShipmentCreated(shipmentId, command.OrderId, $"{shipmentNo}:{command.Address}");
    }
}
```

### Handler async

```csharp
public static class CreateShipmentHandler
{
    public static async Task<ShipmentCreated> Handle(
        CreateShipment command,
        IAddressValidationService validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAsync(command.Address, cancellationToken);
        return new ShipmentCreated(Guid.NewGuid(), command.OrderId, command.Address);
    }
}
```

### Handler trả về nhiều message tiếp theo

```csharp
public record ShipmentCreatedNotification(Guid ShipmentId, string Address);

public static class CreateShipmentHandler
{
    public static IEnumerable<object> Handle(CreateShipment command)
    {
        var shipmentId = Guid.NewGuid();

        yield return new ShipmentCreated(shipmentId, command.OrderId, command.Address);
        yield return new ShipmentCreatedNotification(shipmentId, command.Address);
    }
}
```

Điều này cho thấy Wolverine không chỉ xử lý request-response, mà còn khuyến khích message choreography.

---

## Invoke, enqueue, publish, sự khác nhau cực kỳ quan trọng

Khi làm việc với Wolverine, bạn cần phân biệt 3 kiểu gửi thông điệp phổ biến.

### 1. Invoke

`Invoke` là thực thi message như một lời gọi application-level, có thể nhận về response. Nó phù hợp cho command nội bộ hoặc HTTP endpoint cần kết quả.

### 2. Enqueue / Send

`Send` đẩy message vào queue để xử lý bất đồng bộ. Đây là cách tốt khi công việc chậm, có thể retry, hoặc không cần trả kết quả ngay.

### 3. Publish

`Publish` phát event cho nhiều subscriber. Đây là mô hình integration/event-driven.

Ví dụ trong code:

```csharp
app.MapPost("/shipments", async (CreateShipment request, IMessageBus bus) =>
{
    var result = await bus.InvokeAsync<ShipmentCreated>(request);
    return Results.Created($"/shipments/{result.ShipmentId}", result);
});

app.MapPost("/shipments/{id:guid}/label", async (Guid id, IMessageBus bus) =>
{
    await bus.SendAsync(new GenerateShippingLabel(id));
    return Results.Accepted();
});

app.MapPost("/shipments/{id:guid}/dispatched", async (Guid id, IMessageBus bus) =>
{
    await bus.PublishAsync(new ShipmentDispatched(id, DateTimeOffset.UtcNow));
    return Results.Accepted();
});

public record GenerateShippingLabel(Guid ShipmentId);
public record ShipmentDispatched(Guid ShipmentId, DateTimeOffset DispatchedAt);
```

Nếu bạn dùng sai semantics, thiết kế hệ thống sẽ rối rất nhanh. Command nội bộ cần response mà lại `Publish` thì sai. Background work mà cứ `Invoke` đồng bộ thì phí. Event fan-out mà dùng `Send` vào một queue duy nhất cũng không đúng ý nghĩa.

---

## Local queue, durable local queue và background processing

Một trong những điểm rất hay của Wolverine là bạn có thể bắt đầu từ local queue rồi tiến hóa dần lên distributed transport mà không phải thay toàn bộ mô hình handler.

### Cấu hình local queue

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();

    opts.LocalQueue("important-jobs")
        .Sequential()
        .UseDurableInbox();
});
```

Ở đây:

- `Sequential()` đảm bảo xử lý tuần tự, hữu ích cho một số workload cần tránh race ở queue đó.
- `UseDurableInbox()` giúp message không mất nếu process crash.

### Gửi message vào local queue cụ thể

```csharp
await bus.SendAsync(new GenerateShippingLabel(shipmentId), new DeliveryOptions
{
    Destination = new Uri("local://important-jobs")
});
```

### Handler cho background job

```csharp
public static class GenerateShippingLabelHandler
{
    public static async Task Handle(
        GenerateShippingLabel command,
        ILabelService labelService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await labelService.GenerateAsync(command.ShipmentId, cancellationToken);
        logger.LogInformation("Generated label for shipment {ShipmentId}", command.ShipmentId);
    }
}
```

Điểm đáng giá ở đây là background work không phải “một thế giới khác”. Nó dùng chính message model và handler model mà command bình thường đang dùng.

---

## Retry, error handling và failure policy

Hệ thống phân tán hiếm khi chạy hoàn hảo. API ngoài timeout, database lock, broker chậm, SMTP chết. Wolverine được thiết kế với giả định lỗi là chuyện bình thường.

### Retry theo policy

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies
        .OnException<TimeoutException>()
        .RetryWithCooldown(250.Milliseconds(), 500.Milliseconds(), 1.Seconds());

    opts.Policies
        .OnException<HttpRequestException>()
        .RetryTimes(3);
});
```

### Xử lý dead letter

Tùy transport và cấu hình, message lỗi quá số lần retry có thể đi vào error queue/dead letter handling. Đây là điểm sống còn trong vận hành, vì bạn cần biết cái gì fail vĩnh viễn để can thiệp thay vì để nó biến mất.

### Phân loại transient và permanent error

Một nguyên tắc rất thực dụng:

- `TimeoutException`, `SocketException`, `HttpRequestException` thường là transient, nên retry.
- `ValidationException`, `BusinessRuleViolation`, `ArgumentException` thường là permanent, không nên retry mù quáng.

Ví dụ handler:

```csharp
public static class SendInvoiceEmailHandler
{
    public static async Task Handle(
        SendInvoiceEmail command,
        IEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new InvalidOperationException("Email is required");

        await emailSender.SendAsync(command.Email, "Your invoice", "Invoice attached", cancellationToken);
    }
}
```

Nếu `Email` rỗng thì retry 100 lần cũng vô ích. Nên mô hình hóa lỗi sao cho policy phù hợp.

---

## Scheduled message và delayed execution

Rất nhiều workflow nghiệp vụ cần chạy trễ:

- gửi email nhắc thanh toán sau 24h
- auto cancel đơn hàng sau 15 phút chưa thanh toán
- retry đồng bộ với đối tác sau 10 phút

Wolverine hỗ trợ delayed delivery rất tự nhiên.

```csharp
await bus.ScheduleAsync(
    new CancelUnpaidOrder(orderId),
    DateTimeOffset.UtcNow.AddMinutes(15));

public record CancelUnpaidOrder(Guid OrderId);
```

Handler:

```csharp
public static class CancelUnpaidOrderHandler
{
    public static async Task Handle(CancelUnpaidOrder command, IDocumentSession session)
    {
        var order = await session.LoadAsync<OrderReadModel>(command.OrderId);
        if (order is null) return;

        if (order.Status == "PendingPayment")
        {
            order.Status = "Cancelled";
            session.Store(order);
            await session.SaveChangesAsync();
        }
    }
}
```

Ở mức kiến trúc, đây là một lợi thế rất lớn. Bạn không cần thêm một scheduler riêng cho mọi use case delayed message cơ bản.

---

## Wolverine HTTP, endpoint tối giản nhưng vẫn message-first

Wolverine có thể tích hợp với ASP.NET Core để HTTP endpoint đi thẳng vào handler, giảm boilerplate controller.

### Program.cs

```csharp
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine();
builder.Services.AddWolverineHttp();

var app = builder.Build();
app.MapWolverineEndpoints();
app.Run();
```

### Endpoint bằng method convention

```csharp
public static class ShipmentEndpoints
{
    [WolverinePost("/shipments")]
    public static ShipmentCreated post(CreateShipment command)
    {
        return new ShipmentCreated(Guid.NewGuid(), command.OrderId, command.Address);
    }
}
```

Tất nhiên trong code thật bạn thường inject dependency, gọi handler/service, hoặc dùng Marten session. Nhưng lợi ích ở đây là HTTP chỉ còn là một cửa vào message flow, không phải một nhánh lập trình tách biệt khỏi phần còn lại của ứng dụng.

---

## Middleware và cross-cutting concern

Cũng như nhiều framework pipeline khác, Wolverine cho phép bạn áp chính sách cắt ngang như logging, transaction, validation, authorization nội bộ, correlation.

Ví dụ bạn muốn auto transaction cho handler có đụng persistence:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();
});
```

Hoặc dùng custom middleware để enrich log context:

```csharp
public class CorrelationMiddleware
{
    public static void Before(Envelope envelope, ILogger logger)
    {
        logger.LogInformation("Handling message {MessageType} with id {MessageId}",
            envelope.Message.GetType().Name,
            envelope.Id);
    }
}
```

Cross-cutting concern nên nằm ở pipeline hoặc policy, không nên copy paste vào từng handler.

---

## Transports, từ local queue đến RabbitMQ

Wolverine có thể chạy purely in-process/local durable queue, nhưng cũng hỗ trợ transport bên ngoài. RabbitMQ là ví dụ phổ biến.

### Cấu hình RabbitMQ

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = "localhost";
        rabbit.Port = 5672;
    })
    .AutoProvision();

    opts.PublishMessage<OrderSubmittedIntegrationEvent>()
        .ToRabbitExchange("orders-events");

    opts.ListenToRabbitQueue("billing-service")
        .UseDurableInbox();
});
```

Publisher:

```csharp
await bus.PublishAsync(new OrderSubmittedIntegrationEvent(orderId, customerId, submittedAt));
```

Consumer:

```csharp
public static class OrderSubmittedIntegrationHandler
{
    public static Task Handle(OrderSubmittedIntegrationEvent @event, ILogger logger)
    {
        logger.LogInformation("Received order submitted event {OrderId}", @event.OrderId);
        return Task.CompletedTask;
    }
}
```

Điều hay là local handler hay remote transport consumer đều đi qua cùng một mô hình logic. Điều thay đổi chủ yếu là topology và delivery guarantees.

---

## Wolverine với Marten, câu chuyện đáng học nhất

Nếu bạn chỉ học một integration của Wolverine, hãy học integration với Marten. Đây là vùng mà Wolverine thật sự khác biệt vì nó giải bài toán rất khó trong distributed app: vừa lưu state, vừa phát message đáng tin cậy mà không rơi vào dual write.

### Bài toán dual write

Use case:

1. `PlaceOrder` command đến hệ thống.
2. Bạn append event `OrderPlaced` vào Marten.
3. Bạn publish `OrderPlacedIntegrationEvent` cho payment service.

Nếu bước 2 thành công nhưng bước 3 fail do process crash, payment service không biết gì. Nếu bước 3 thành công mà bước 2 fail, downstream thấy event không có thật. Đây là tình huống kinh điển.

### Cấu hình Marten + Wolverine

```csharp
using Marten;
using Wolverine;
using Wolverine.Marten;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("postgres")!);
    options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
});

builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();
    opts.UseMartenTransactionalMiddleware();
});
```

### Command và event

```csharp
public record PlaceOrder(Guid OrderId, Guid CustomerId, decimal Amount);
public record OrderPlaced(Guid OrderId, Guid CustomerId, decimal Amount, DateTimeOffset CreatedAt);
public record OrderPlacedIntegrationEvent(Guid OrderId, Guid CustomerId, decimal Amount);
```

### Handler thực chiến

```csharp
public static class PlaceOrderHandler
{
    public static async Task<OrderPlaced> Handle(
        PlaceOrder command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var @event = new OrderPlaced(
            command.OrderId,
            command.CustomerId,
            command.Amount,
            DateTimeOffset.UtcNow);

        session.Events.StartStream<Order>(command.OrderId, @event);

        await bus.PublishAsync(
            new OrderPlacedIntegrationEvent(command.OrderId, command.CustomerId, command.Amount),
            cancellation: cancellationToken);

        await session.SaveChangesAsync(cancellationToken);
        return @event;
    }
}
```

Điểm quan trọng không nằm ở cú pháp, mà ở semantics. Khi cấu hình đúng, Wolverine sẽ dùng outbox/inbox bền vững để outgoing message gắn với transaction boundary. Nếu commit thất bại, message không được coi là thành công. Nếu commit thành công, message sẽ được dispatch durable kể cả process chết ngay sau đó.

Đây là một trong những lý do lớn nhất để chọn Wolverine khi bạn đã chọn Marten làm persistence backbone.

---

## Handler chaining và workflow orchestration nhẹ

Wolverine không phải workflow engine đồ sộ, nhưng nó hỗ trợ chaining message rất tốt. Điều này đủ cho nhiều business flow thực tế.

Ví dụ sau khi `OrderPlaced`, bạn muốn:

- reserve inventory
- send confirmation email
- schedule payment timeout check

Handler có thể trả về nhiều message kế tiếp:

```csharp
public record ReserveInventory(Guid OrderId);
public record SendOrderConfirmation(Guid OrderId);
public record CheckPaymentTimeout(Guid OrderId);

public static class OrderPlacedHandler
{
    public static IEnumerable<object> Handle(OrderPlaced @event)
    {
        yield return new ReserveInventory(@event.OrderId);
        yield return new SendOrderConfirmation(@event.OrderId);
        yield return new CheckPaymentTimeout(@event.OrderId);
    }
}
```

Hoặc có message một phần sync, một phần async/scheduled:

```csharp
public static class PlaceOrderHandler
{
    public static async Task<object[]> Handle(
        PlaceOrder command,
        IDocumentSession session,
        IMessageContext context,
        CancellationToken cancellationToken)
    {
        var placed = new OrderPlaced(command.OrderId, command.CustomerId, command.Amount, DateTimeOffset.UtcNow);
        session.Events.StartStream<Order>(command.OrderId, placed);
        await session.SaveChangesAsync(cancellationToken);

        await context.ScheduleAsync(new CheckPaymentTimeout(command.OrderId), 15.Minutes());

        return new object[]
        {
            placed,
            new ReserveInventory(command.OrderId),
            new SendOrderConfirmation(command.OrderId)
        };
    }
}
```

Đây là kiểu orchestration đủ mạnh cho nhiều hệ thống nghiệp vụ vừa và lớn, mà không cần kéo vào một BPM engine nặng nề.

---

## Idempotency, thứ bạn phải nghĩ nếu làm message-driven app nghiêm túc

Framework tốt không cứu được một hệ thống không idempotent. Dù Wolverine có durable inbox/outbox, message có thể vẫn được xử lý lại trong vài hoàn cảnh như retry, re-delivery sau crash, hoặc external transport semantics.

### Một số kỹ thuật idempotency phổ biến

1. Kiểm tra business state trước khi xử lý.
2. Dùng unique constraint ở database.
3. Lưu processed message id cho một loại nghiệp vụ quan trọng.
4. Dùng stream version hoặc optimistic concurrency trong Marten.

Ví dụ với handler tạo shipment:

```csharp
public static class ReserveInventoryHandler
{
    public static async Task Handle(ReserveInventory command, IDocumentSession session)
    {
        var reservation = await session.Query<InventoryReservation>()
            .FirstOrDefaultAsync(x => x.OrderId == command.OrderId);

        if (reservation is not null)
            return;

        session.Store(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            OrderId = command.OrderId,
            ReservedAt = DateTimeOffset.UtcNow
        });

        await session.SaveChangesAsync();
    }
}

public class InventoryReservation
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
}
```

Nếu queue gửi lại message, handler vẫn an toàn.

---

## Wolverine như application backbone, không chỉ là message bus wrapper

Một sai lầm phổ biến là nhìn Wolverine như “công cụ để nghe queue”. Cách nhìn đúng hơn là nó có thể trở thành trục điều phối application logic.

Bạn có thể tổ chức một service theo mô hình:

- HTTP request vào Wolverine endpoint
- endpoint tạo command
- command handler dùng Marten load state
- handler phát domain/integration event
- event handler local cập nhật side effect
- message quan trọng gửi qua durable queue
- retry/scheduling do Wolverine runtime đảm nhận

Với cách làm này, controller, hosted service, background job, bus consumer không còn là những thế giới tách rời với style code khác nhau. Chúng cùng quy về message + handler + policy.

---

## Phân biệt Wolverine với MassTransit và NServiceBus

### So với MassTransit

MassTransit rất mạnh ở bus abstraction, saga state machine, transport support, enterprise messaging pattern quen thuộc. Wolverine gần application core hơn, handler-first hơn và gắn bó chặt với Marten hơn. Nếu team của bạn muốn bus-centric integration layer thuần túy, MassTransit rất hợp. Nếu bạn muốn một execution model thống nhất cho local command, background work và integration, Wolverine thường cho cảm giác gọn hơn.

### So với NServiceBus

NServiceBus trưởng thành, giàu tính năng enterprise, nhưng cũng nặng và có cost/ceremony riêng. Wolverine mang phong cách hiện đại hơn, nhẹ hơn, gần code thuần .NET hơn, nhưng hệ sinh thái và độ phổ biến nhỏ hơn.

### So với Hangfire

Hangfire chủ yếu mạnh về background job scheduling/execution. Wolverine làm được delayed/background processing nhưng trong khung message-driven application rộng hơn nhiều. Nếu bài toán chỉ là cron và dashboard job đơn giản, Hangfire có thể đủ. Nếu bạn muốn message flow bền vững xuyên cả application, Wolverine hợp hơn.

---

## Cấu trúc codebase gợi ý khi dùng Wolverine

Một cấu trúc rất thực dụng:

```text
/src
  /Contracts
    PlaceOrder.cs
    OrderPlacedIntegrationEvent.cs
  /Orders
    PlaceOrderHandler.cs
    SubmitOrderHandler.cs
    Order.cs
    OrderSummaryProjection.cs
  /Billing
    CapturePaymentHandler.cs
  /Infrastructure
    WolverineSetup.cs
    MartenSetup.cs
  Program.cs
```

Nguyên tắc:

- message contract rõ ràng, tên theo nghiệp vụ
- handler đặt gần feature, không dồn vào thư mục “Services” chung chung
- domain state/projection nằm cùng feature boundary
- transport/persistence config gom ở Infrastructure

Điều này giúp codebase rõ ràng hơn rất nhiều khi số lượng workflow tăng lên.

---

## Testing Wolverine

Nên test ở 3 mức giống bất kỳ message-driven system tử tế nào.

### 1. Unit test handler business logic

```csharp
[Fact]
public void create_shipment_should_return_created_event()
{
    var result = CreateShipmentHandler.Handle(new CreateShipment(Guid.NewGuid(), "Hanoi"));
    result.OrderId.ShouldNotBe(Guid.Empty);
    result.Address.ShouldContain("Hanoi");
}
```

### 2. Integration test local bus + persistence

```csharp
[Fact]
public async Task place_order_should_persist_and_emit_integration_event()
{
    await using var host = await WolverineHost.For(opts =>
    {
        opts.Services.AddMarten(o =>
        {
            o.Connection("Host=localhost;Port=5432;Database=test_db;Username=postgres;Password=postgres");
            o.AutoCreateSchemaObjects = AutoCreate.All;
        });

        opts.UseMartenTransactionalMiddleware();
    });

    var bus = host.Services.GetRequiredService<IMessageBus>();

    var orderId = Guid.NewGuid();
    var result = await bus.InvokeAsync<OrderPlaced>(new PlaceOrder(orderId, Guid.NewGuid(), 125));

    result.OrderId.ShouldBe(orderId);
}
```

### 3. End-to-end test với transport thật hoặc test harness gần production

Khi có RabbitMQ/Azure Service Bus, nên có test môi trường staging để xác minh topology, retry, dead letter, serialization, versioning hoạt động như mong đợi.

---

## Observability, logging và vận hành

Message-driven app khó debug hơn CRUD app, nên observability rất quan trọng.

Bạn nên sớm chuẩn hóa:

- correlation id
- message id
- causation id
- structured logging theo handler/message type
- metrics queue depth, retry count, failure count
- tracing nếu có OpenTelemetry

Ví dụ log có cấu trúc:

```csharp
public static class ShipmentDispatchedHandler
{
    public static Task Handle(ShipmentDispatched message, ILogger logger)
    {
        logger.LogInformation(
            "Shipment dispatched. ShipmentId={ShipmentId} DispatchedAt={DispatchedAt}",
            message.ShipmentId,
            message.DispatchedAt);

        return Task.CompletedTask;
    }
}
```

Trong production, bạn rất cần trả lời nhanh các câu hỏi kiểu:

- message này đã được xử lý chưa?
- đang retry lần thứ mấy?
- fail ở handler nào?
- outgoing event đã được dispatch hay còn nằm trong outbox?
- projection/read model chậm vì đâu?

Nếu dùng Marten + Wolverine, việc nối trạng thái persistence với message runtime trong log/tracing đem lại lợi thế vận hành rất lớn.

---

## Một flow hoàn chỉnh, từ HTTP tới durable message

Hãy xem một ví dụ đầy đủ hơn về `SubmitOrder`.

### Contract

```csharp
public record SubmitOrder(Guid OrderId);
public record OrderSubmitted(Guid OrderId, DateTimeOffset SubmittedAt);
public record NotifyWarehouse(Guid OrderId);
public record SendOrderReceipt(Guid OrderId);
```

### HTTP endpoint

```csharp
app.MapPost("/orders/{id:guid}/submit", async (Guid id, IMessageBus bus) =>
{
    var result = await bus.InvokeAsync<OrderSubmitted>(new SubmitOrder(id));
    return Results.Accepted($"/orders/{id}", result);
});
```

### Handler chính

```csharp
public static class SubmitOrderHandler
{
    public static async Task<object[]> Handle(
        SubmitOrder command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var stream = await session.Events.FetchForWriting<Order>(command.OrderId, cancellationToken: cancellationToken);
        var order = stream.Aggregate ?? throw new InvalidOperationException("Order not found");

        if (order.Status != "Draft")
            throw new InvalidOperationException("Only draft orders can be submitted");

        if (!order.Items.Any())
            throw new InvalidOperationException("Order has no items");

        var submitted = new OrderSubmitted(command.OrderId, DateTimeOffset.UtcNow);

        stream.AppendOne(new DomainOrderSubmitted(command.OrderId, submitted.SubmittedAt));
        await session.SaveChangesAsync(cancellationToken);

        return new object[]
        {
            submitted,
            new NotifyWarehouse(command.OrderId),
            new SendOrderReceipt(command.OrderId)
        };
    }
}

public record DomainOrderSubmitted(Guid OrderId, DateTimeOffset SubmittedAt);
```

### Downstream handlers

```csharp
public static class NotifyWarehouseHandler
{
    public static Task Handle(NotifyWarehouse command, ILogger logger)
    {
        logger.LogInformation("Warehouse notified for order {OrderId}", command.OrderId);
        return Task.CompletedTask;
    }
}

public static class SendOrderReceiptHandler
{
    public static async Task Handle(SendOrderReceipt command, IEmailService emailService)
    {
        await emailService.SendReceiptAsync(command.OrderId);
    }
}
```

Flow này cho thấy HTTP chỉ kích hoạt command. Business logic và side effect nằm ở handler chain. Nếu `SendOrderReceipt` fail, Wolverine có thể retry theo policy mà không ảnh hưởng việc order đã được submit.

---

## Một kiến trúc mẫu cho distributed system dùng Wolverine

### Service đơn lẻ nhưng có background workflow

- user POST request
- invoke command
- local durable queue xử lý tác vụ nặng
- retry khi gọi external API lỗi
- delayed message cho timeout/reminder

### Microservice có integration event

- internal command được xử lý qua handler
- publish event ra broker
- consumer service khác cũng dùng handler style
- inbox/outbox đảm bảo không mất message quan trọng

### Event sourced service với Marten

- state và stream ở Marten
- handler Wolverine append event
- outgoing integration event qua Wolverine
- async projection/read model cập nhật qua Marten daemon

Kiểu kiến trúc thứ ba là nơi Wolverine phát huy tối đa giá trị.

---

## Những lỗi phổ biến khi mới dùng Wolverine

1. **Dùng message semantics lẫn lộn**
   - command đáng lẽ `Invoke` thì lại `Publish`
   - event fan-out đáng lẽ `Publish` thì lại `Send`

2. **Không nghĩ về idempotency**
   - message retry lại là tạo shipment/deduct balance lần hai.

3. **Nhét quá nhiều side effect vào một handler đồng bộ**
   - DB, email, webhook, report generation, broker publish cùng một chỗ, rất khó rollback/retry đúng nghĩa.

4. **Không định nghĩa retry policy rõ**
   - lỗi business retry vô tận, lỗi transient lại fail thẳng.

5. **Xem local queue như fire-and-forget vô trách nhiệm**
   - background work quan trọng phải có durability, quan sát và policy rõ ràng.

6. **Thiếu observability**
   - đến lúc production fail thì không biết message nào đi đâu.

7. **Tách HTTP, background job, bus consumer thành 3 phong cách code khác nhau**
   - bỏ lỡ sức mạnh thống nhất của Wolverine.

---

## Cách ra quyết định dùng Wolverine trong dự án mới

Bạn có thể dùng checklist thực dụng này.

### Chọn Wolverine nếu đa số câu trả lời là có

- Hệ thống có command/event rõ ràng?
- Có background work đáng kể?
- Có nhu cầu retry/scheduling?
- Có integration với service khác qua message?
- Muốn handler-first thay vì controller/service layer dày?
- Có Marten hoặc cần outbox/inbox bền vững?

### Cân nhắc giải pháp nhẹ hơn nếu đa số câu trả lời là không

- Chỉ là CRUD app nhỏ
- Không có integration event
- Không có queue, scheduler, retry, background workflow đáng kể
- Team muốn đơn giản tối đa

Không có framework nào nên bị dùng vì “ngầu”. Wolverine đáng giá nhất khi bài toán thực sự cần durability và message-driven design.

---

## Mối quan hệ giữa Wolverine và Marten, nhìn từ kiến trúc tổng thể

Đây là cách tôi thường giải thích rất ngắn cho team:

- **Marten** trả lời câu hỏi: state và history của domain được lưu thế nào?
- **Wolverine** trả lời câu hỏi: command, event, background work và integration được thực thi bền vững thế nào?

Khi đi cùng nhau:

- Marten cung cấp aggregate state, event stream, projection.
- Wolverine cung cấp handler pipeline, durable messaging, scheduling, retry, transport.
- Cả hai gặp nhau ở transaction boundary để giải bài toán dual write.

Một flow đẹp sẽ là:

1. HTTP request hoặc broker message đi vào Wolverine.
2. Handler load aggregate từ Marten.
3. Business rule quyết định event mới.
4. Marten lưu event/document.
5. Wolverine ghi outgoing envelope vào outbox.
6. Commit một lần.
7. Wolverine dispatch message ra local queue hoặc broker.
8. Marten projection/read model bắt kịp theo lifecycle đã chọn.

Nếu bạn đang xây một distributed .NET app theo hướng domain-centric, rất khó tìm một cặp thư viện kết hợp hài hòa như vậy.

---

## Một chiến lược áp dụng Wolverine an toàn trong dự án thật

Nếu đội của bạn chưa từng làm message-driven application nghiêm túc, cách tốt nhất không phải là đưa cả broker, saga, delayed delivery, HTTP endpoint convention và một rừng policy vào sprint đầu tiên. Một đường đi an toàn hơn thường là:

### Bước 1, dùng Wolverine như command bus nội bộ

- viết handler cho vài use case chính
- gọi bằng `InvokeAsync`
- chuẩn hóa dependency injection, validation, logging
- bỏ bớt controller/service boilerplate

### Bước 2, chuyển tác vụ chậm sang local durable queue

- email
- tạo file PDF
- đồng bộ webhook
- làm giàu dữ liệu từ API ngoài

Ở bước này team bắt đầu học retry, queue semantics và idempotency nhưng vẫn trong phạm vi một service.

### Bước 3, thêm delayed message và failure policy

- timeout thanh toán
- reminder email
- retry external API có cooldown

Khi đó team sẽ cảm nhận rõ giá trị vận hành của Wolverine thay vì xem nó chỉ như lớp wrapper quanh method call.

### Bước 4, ghép với Marten hoặc broker ngoài

- nếu hệ thống cần event sourcing hoặc outbox chắc chắn, ghép Marten
- nếu cần integration giữa service, thêm RabbitMQ hoặc transport phù hợp
- giữ message contract rõ ràng ngay từ đầu

Đi theo lộ trình này giúp kiến trúc tiến hóa có kiểm soát, ít gây sốc cho team hơn rất nhiều.

## Checklist trước khi chọn Wolverine

Trước khi quyết định, hãy hỏi vài câu rất thực tế:

- Hệ thống có cần background processing đáng kể không?
- Có cần retry và delayed execution không?
- Có integration event giữa nhiều service không?
- Có cần outbox/inbox để tránh dual write không?
- Team có sẵn sàng học message semantics và idempotency không?
- Bạn muốn HTTP, local queue và remote message dùng chung một handler model không?

Nếu phần lớn câu trả lời là “có”, Wolverine rất đáng cân nhắc. Nếu gần như tất cả là “không”, một stack đơn giản hơn có thể phù hợp hơn.

## Mẫu phân vai trách nhiệm trong một service dùng Wolverine

Một service dùng Wolverine bền vững thường rõ ràng ở chỗ ai chịu trách nhiệm cho việc gì:

- **HTTP endpoint** chỉ nhận input, gọi command, trả response thích hợp.
- **Command handler** kiểm tra business rule, gọi persistence, phát message kế tiếp.
- **Event handler** phản ứng với sự kiện đã xảy ra, tránh quay lại nhầm vai command.
- **Background handler** làm tác vụ chậm hoặc có thể retry như email, webhook, file generation.
- **Infrastructure policy** giữ transaction, retry, logging, correlation, transport.

Khi phân vai rõ như vậy, codebase sẽ dễ đọc hơn rất nhiều. Người mới vào dự án có thể lần theo flow từ contract đến handler, thay vì đi qua controller, service, repository, background job class, bus publisher và mấy lớp helper tản mạn.

Một quy ước đặt tên cũng rất hữu ích:

- command: `SubmitOrder`, `CapturePayment`, `GenerateInvoicePdf`
- event: `OrderSubmitted`, `PaymentCaptured`, `InvoicePdfGenerated`
- handler: `SubmitOrderHandler`, `CapturePaymentHandler`

Chỉ riêng việc giữ naming nhất quán đã giảm mạnh chi phí onboarding và debugging trong hệ thống message-driven.

## Kết luận

Wolverine là một framework rất đáng học nếu bạn muốn bước từ ứng dụng CRUD thông thường sang hệ thống message-driven, có background workflow, retry, scheduling và durable execution mà vẫn giữ codebase gọn, feature-centric và khá gần .NET thuần.

Điểm mạnh lớn nhất của Wolverine không chỉ là nó nghe queue hay publish event. Điểm mạnh thật sự là nó thống nhất nhiều cửa vào của application, từ HTTP đến background message, dưới cùng một mô hình handler và policy. Điều đó giúp kiến trúc nhất quán hơn, dễ tiến hóa hơn và bớt copy-paste hơn rất nhiều.

Khi kết hợp với Marten, Wolverine còn giải quyết được bài toán mà rất nhiều hệ thống phân tán vấp phải: vừa lưu state đúng, vừa phát message đúng, không bị mất hoặc phát “ma” vì dual write. Chính sự kết hợp này làm Wolverine đặc biệt hấp dẫn với các đội xây distributed app, internal platform service, order/payment workflow, hay event-driven business system trong .NET.

Nếu dự án của bạn thực sự có messaging, delayed work, background handler, retry, durability và integration event, Wolverine không chỉ là một lựa chọn thú vị. Nó có thể trở thành xương sống của cả application.
