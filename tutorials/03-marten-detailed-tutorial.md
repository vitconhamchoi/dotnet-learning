# Marten chuyên sâu, document database, event sourcing và CQRS thực chiến trên PostgreSQL

## Marten là gì, và vì sao nhiều team .NET thích nó

Marten là một thư viện .NET cho phép bạn dùng PostgreSQL như một document database và event store, thay vì chỉ xem Postgres như nơi chứa bảng quan hệ truyền thống. Điểm làm Marten khác biệt không nằm ở chỗ “lưu JSON trong Postgres”, vì kỹ thuật đó bản thân PostgreSQL đã hỗ trợ từ lâu với `jsonb`. Giá trị thật của Marten là nó mang đến một programming model hoàn chỉnh cho application layer trong .NET, nơi bạn có thể:

- lưu aggregate dưới dạng document JSON nhưng vẫn truy vấn mạnh nhờ PostgreSQL
- dùng optimistic concurrency để bảo vệ invariant của domain
- xây dựng event stream cho từng aggregate
- materialize read model bằng projection
- kết hợp event sourcing và CQRS mà không phải tự viết quá nhiều plumbing
- tận dụng transaction, index, backup, observability, replication, tooling của PostgreSQL

Nếu bạn từng thấy nhiều hệ thống .NET đi từ một codebase CRUD đơn giản sang microservice, event-driven, hay distributed app và bắt đầu đau vì phải ghép quá nhiều mảnh như EF Core + custom event store + outbox + read model updater + background jobs, thì Marten thường xuất hiện như một giải pháp giúp giảm rất nhiều chi phí kiến trúc.

Marten đặc biệt hấp dẫn trong vài tình huống sau:

1. Bạn muốn event sourcing nhưng không muốn vận hành một event store riêng.
2. Bạn muốn document-oriented model nhưng hạ tầng công ty đã chuẩn hóa PostgreSQL.
3. Bạn muốn read model evolve nhanh hơn mô hình quan hệ cứng nhắc.
4. Bạn muốn giữ domain model gần business hơn thay vì bó nó hoàn toàn vào schema bảng.
5. Bạn muốn ghép với Wolverine để có một story rất đẹp cho command handling, durable messaging, projection và integration event.

Bài này đi rất sâu theo hướng thực chiến. Mục tiêu không phải chỉ là chạy vài câu lệnh `Store` hay `Load`, mà là hiểu khi nào Marten thực sự đáng dùng, cách thiết kế aggregate, stream, projection, và cách tích hợp với ASP.NET Core cũng như Wolverine.

---

## Khi nào nên dùng Marten, khi nào không nên

### Nên dùng Marten khi

- Domain có aggregate rõ ràng như Order, Invoice, Cart, Subscription, Shipment.
- Cần audit trail hoặc lịch sử thay đổi.
- Cần event sourcing hoặc ít nhất muốn có append-only history.
- Mô hình dữ liệu thay đổi khá nhanh, cần schema flexibility.
- Team đã quen PostgreSQL và muốn tận dụng hạ tầng sẵn có.
- Muốn kết hợp command side với projection/read side tương đối mượt.

### Không nên dùng Marten khi

- Hệ thống chủ yếu là CRUD quan hệ, join phức tạp, báo cáo SQL truyền thống, gần như không cần event sourcing hay aggregate boundary.
- Team rất yếu PostgreSQL và chưa sẵn sàng học thêm khái niệm event stream, projection, optimistic concurrency.
- Bạn có workload analytic cực nặng, star schema, ETL, OLAP, nơi Marten không phải công cụ chính.
- Bạn cần tương thích sâu với cả một hệ sinh thái EF Core cũ và chưa muốn thay đổi cách tổ chức domain.

### So với EF Core thì sao

EF Core mạnh khi bạn muốn ánh xạ bảng quan hệ sang object graph, tận dụng LINQ, migrations, tracking, navigation property. Marten mạnh hơn khi bạn muốn:

- coi aggregate như tài liệu JSON toàn khối
- tránh mapping phức tạp của object graph quan hệ
- append domain event và tái tạo state từ stream
- build read model theo projection thay vì join query ad-hoc mọi nơi

Nói ngắn gọn, EF Core là ORM cho mô hình quan hệ. Marten là document DB + event sourcing framework chạy trên PostgreSQL. Có vùng giao nhau, nhưng trọng tâm tư duy khác nhau.

---

## Kiến trúc tổng thể của Marten

Có 5 khối rất quan trọng bạn cần nắm:

1. **Document store**: cấu hình trung tâm, giống như factory để mở session.
2. **Session**: đơn vị làm việc để đọc, ghi, query, append event.
3. **Document mapping**: Marten biết cách lưu kiểu .NET nào xuống bảng `mt_doc_*`.
4. **Event store**: nơi lưu event stream, metadata, version.
5. **Projection**: cơ chế tạo read model từ event stream.

Một kiến trúc điển hình trông như sau:

```text
HTTP API / gRPC / Message Handler
        |
        v
Application Service / Command Handler
        |
        +--> IDocumentSession.Store(document)
        |
        +--> session.Events.Append(streamId, events)
        |
        +--> session.SaveChangesAsync()
        |
        v
PostgreSQL
  - mt_doc_order
  - mt_events
  - mt_streams
  - projection tables/documents
```

Nếu kết hợp Wolverine, một flow còn đẹp hơn:

```text
HTTP POST /orders
   -> Wolverine command handler
   -> load stream or aggregate state from Marten
   -> validate business rules
   -> append events
   -> publish integration event via Wolverine outbox
   -> commit once
   -> async projections / downstream handlers chạy durable
```

Đây chính là điểm khiến Marten rất hợp cho distributed application, vì nó không chỉ là nơi lưu dữ liệu, mà còn là xương sống của domain history.

---

## Cài đặt và bootstrap dự án

Giả sử bạn đang tạo một API cho quản lý đơn hàng.

### Tạo project và package

```bash
dotnet new webapi -n OrderService
cd OrderService
dotnet add package Marten
dotnet add package Weasel.Postgresql
```

Nếu muốn dùng Wolverine sau này:

```bash
dotnet add package WolverineFx
dotnet add package WolverineFx.Marten
```

### appsettings.json

```json
{
  "ConnectionStrings": {
    "postgres": "Host=localhost;Port=5432;Database=order_service;Username=postgres;Password=postgres"
  }
}
```

### Program.cs tối thiểu

```csharp
using Marten;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("postgres")!);
    options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
});

var app = builder.Build();

app.MapGet("/", () => "Order service is running");

app.Run();
```

`AutoCreate.CreateOrUpdate` rất tiện cho local dev. Trong production, bạn thường cẩn thận hơn, dùng migration có kiểm soát hoặc exported database patch.

---

## Document storage, cách nghĩ đúng về aggregate document

Một hiểu nhầm phổ biến là cứ có Marten thì mọi thứ đều nên nhét vào document. Không đúng. Document trong Marten hợp nhất khi nó đại diện cho một aggregate hoặc read model có lifecycle rõ ràng.

Ví dụ một `CustomerProfile` rất phù hợp để lưu dạng document:

```csharp
public class CustomerProfile
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Tier { get; set; } = "Standard";
    public Address PrimaryAddress { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Address
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string Country { get; set; } = default!;
}
```

### Ghi document

```csharp
app.MapPost("/customers", async (CustomerProfile input, IDocumentSession session) =>
{
    input.Id = Guid.NewGuid();
    input.UpdatedAt = DateTimeOffset.UtcNow;

    session.Store(input);
    await session.SaveChangesAsync();

    return Results.Created($"/customers/{input.Id}", input);
});
```

### Đọc document

```csharp
app.MapGet("/customers/{id:guid}", async (Guid id, IQuerySession querySession) =>
{
    var customer = await querySession.LoadAsync<CustomerProfile>(id);
    return customer is not null ? Results.Ok(customer) : Results.NotFound();
});
```

### Query bằng LINQ

```csharp
app.MapGet("/customers", async (
    string? tier,
    string? city,
    IQuerySession querySession) =>
{
    var query = querySession.Query<CustomerProfile>();

    if (!string.IsNullOrWhiteSpace(tier))
        query = query.Where(x => x.Tier == tier);

    if (!string.IsNullOrWhiteSpace(city))
        query = query.Where(x => x.PrimaryAddress.City == city);

    var customers = await query
        .OrderBy(x => x.FullName)
        .ToListAsync();

    return Results.Ok(customers);
});
```

Từ góc nhìn của lập trình viên, bạn đang query object. Từ góc nhìn của PostgreSQL, Marten sẽ translate xuống JSONB access hoặc cột được duplicate/index tùy mapping.

---

## Mapping, index và tối ưu query

Nếu bạn chỉ dùng Marten như nơi lưu JSON mà không chú ý tới mapping, index, duplicated fields, hiệu năng query sẽ nhanh chóng trở thành vấn đề.

### Cấu hình document mapping

```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("postgres")!);
    options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    options.Schema.For<CustomerProfile>()
        .Index(x => x.Email, x =>
        {
            x.IsUnique = true;
        })
        .Duplicate(x => x.Tier)
        .Duplicate(x => x.Email)
        .GinIndexJsonData();
});
```

Ý nghĩa:

- `Index(x => x.Email)` tạo index để lookup nhanh hơn.
- `IsUnique = true` giúp enforce ràng buộc nghiệp vụ ở mức database.
- `Duplicate(x => x.Tier)` copy field đó thành cột riêng, query/filter/sort sẽ nhanh hơn so với đào vào JSON.
- `GinIndexJsonData()` hữu ích cho các truy vấn trên JSONB.

### Metadata columns

Bạn có thể muốn audit document-level metadata:

```csharp
options.Schema.For<CustomerProfile>()
    .Metadata(m =>
    {
        m.LastModifiedBy.Enabled = true;
        m.Headers.Enabled = true;
        m.CorrelationId.Enabled = true;
        m.CausationId.Enabled = true;
    });
```

Điều này rất có ích khi hệ thống có tracing, background processing, hay khi bạn cần điều tra ai đã sửa gì.

---

## Session lifecycle, lightweight, identity và dirty-tracked

Marten có nhiều kiểu session. Đây là điểm nhiều người bỏ qua rồi vô tình tạo overhead không cần thiết.

### 1. Lightweight session

```csharp
using var session = documentStore.LightweightSession();
```

- nhẹ nhất
- không theo dõi thay đổi object đã load
- hợp cho command handler rõ ràng, bạn chủ động `Store` và `SaveChanges`

### 2. Identity session

```csharp
using var session = documentStore.IdentitySession();
```

- có identity map trong phạm vi session
- load cùng một document nhiều lần sẽ trả về cùng instance
- hữu ích khi một use case truy cập cùng aggregate nhiều lần

### 3. Dirty tracked session

```csharp
using var session = documentStore.DirtyTrackedSession();
```

- theo dõi thay đổi object và tự xác định cần update
- tiện, nhưng nặng hơn
- không phải lựa chọn mặc định cho throughput cao

Trong application service hoặc message handler, `LightweightSession` thường là lựa chọn tốt vì explicit và hiệu quả.

---

## Optimistic concurrency, thứ cực kỳ quan trọng trong domain model

Nếu hai request cùng sửa một aggregate, bạn cần tránh lost update. Marten hỗ trợ optimistic concurrency rất tốt.

### Với document

```csharp
public class InventoryItem
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = default!;
    public int Available { get; set; }
}
```

Cấu hình:

```csharp
options.Schema.For<InventoryItem>().UseOptimisticConcurrency(true);
```

Cập nhật:

```csharp
app.MapPost("/inventory/{id:guid}/reserve", async (
    Guid id,
    ReserveInventoryRequest request,
    IDocumentSession session) =>
{
    var item = await session.LoadAsync<InventoryItem>(id);
    if (item is null) return Results.NotFound();

    if (item.Available < request.Quantity)
        return Results.BadRequest("Not enough stock");

    item.Available -= request.Quantity;
    session.Store(item);

    try
    {
        await session.SaveChangesAsync();
        return Results.Ok(item);
    }
    catch (Marten.Exceptions.ConcurrencyException)
    {
        return Results.Conflict("Inventory was modified by another request");
    }
});

public record ReserveInventoryRequest(int Quantity);
```

Optimistic concurrency đặc biệt quan trọng khi nhiều command có thể chạm cùng aggregate như Order, Payment, Inventory, Wallet.

---

## Event sourcing với Marten, trái tim của nhiều hệ thống domain phức tạp

Đây là phần Marten thực sự tỏa sáng.

Thay vì lưu trạng thái cuối cùng của aggregate rồi ghi đè mỗi lần, bạn lưu các sự kiện domain theo dòng thời gian. Ví dụ với đơn hàng:

- `OrderCreated`
- `OrderItemAdded`
- `OrderSubmitted`
- `PaymentAuthorized`
- `OrderShipped`
- `OrderCancelled`

### Khai báo event

```csharp
public record CreateOrder(Guid OrderId, Guid CustomerId);
public record AddOrderItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
public record SubmitOrder(DateTimeOffset SubmittedAt);
public record CancelOrder(string Reason, DateTimeOffset CancelledAt);

public record OrderCreated(Guid OrderId, Guid CustomerId, DateTimeOffset CreatedAt);
public record OrderItemAdded(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
public record OrderSubmitted(DateTimeOffset SubmittedAt);
public record OrderCancelled(string Reason, DateTimeOffset CancelledAt);
```

### Aggregate áp dụng event

```csharp
public class Order
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Status { get; private set; } = "Draft";
    public List<OrderLine> Items { get; } = new();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public static Order Create(OrderCreated e)
    {
        return new Order
        {
            Id = e.OrderId,
            CustomerId = e.CustomerId,
            CreatedAt = e.CreatedAt,
            Status = "Draft"
        };
    }

    public void Apply(OrderItemAdded e)
    {
        Items.Add(new OrderLine(e.ProductId, e.ProductName, e.UnitPrice, e.Quantity));
    }

    public void Apply(OrderSubmitted e)
    {
        Status = "Submitted";
        SubmittedAt = e.SubmittedAt;
    }

    public void Apply(OrderCancelled e)
    {
        Status = "Cancelled";
        CancelledAt = e.CancelledAt;
    }
}

public record OrderLine(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
```

### Command handler append event

```csharp
app.MapPost("/orders", async (CreateOrder command, IDocumentSession session) =>
{
    var created = new OrderCreated(command.OrderId, command.CustomerId, DateTimeOffset.UtcNow);

    session.Events.StartStream<Order>(command.OrderId, created);
    await session.SaveChangesAsync();

    return Results.Created($"/orders/{command.OrderId}", new { command.OrderId });
});
```

Thêm item:

```csharp
app.MapPost("/orders/{id:guid}/items", async (
    Guid id,
    AddOrderItem command,
    IDocumentSession session) =>
{
    var order = await session.Events.AggregateStreamAsync<Order>(id);
    if (order is null) return Results.NotFound();

    if (order.Status != "Draft")
        return Results.BadRequest("Cannot modify a non-draft order");

    var @event = new OrderItemAdded(
        command.ProductId,
        command.ProductName,
        command.UnitPrice,
        command.Quantity);

    session.Events.Append(id, @event);
    await session.SaveChangesAsync();

    return Results.Ok();
});
```

Submit order:

```csharp
app.MapPost("/orders/{id:guid}/submit", async (Guid id, IDocumentSession session) =>
{
    var order = await session.Events.AggregateStreamAsync<Order>(id);
    if (order is null) return Results.NotFound();

    if (!order.Items.Any())
        return Results.BadRequest("Order must have at least one item");

    if (order.Status != "Draft")
        return Results.BadRequest("Only draft order can be submitted");

    session.Events.Append(id, new OrderSubmitted(DateTimeOffset.UtcNow));
    await session.SaveChangesAsync();

    return Results.Accepted();
});
```

Tư duy ở đây là business logic quyết định event nào được phép phát sinh. State hiện tại của aggregate chỉ là kết quả của việc fold các event trước đó.

---

## Phiên bản stream và concurrency trên event stream

Một stream event có version. Đây là lá chắn cực tốt trước concurrent command.

Ví dụ 2 request cùng submit một order. Bạn có thể yêu cầu append event chỉ khi stream đang ở expected version nhất định.

```csharp
var stream = await session.Events.FetchForWriting<Order>(id);

if (stream.Aggregate.Status != "Draft")
    throw new InvalidOperationException("Invalid status");

stream.AppendOne(new OrderSubmitted(DateTimeOffset.UtcNow));
await session.SaveChangesAsync();
```

Hoặc explicit hơn với expected version. Tư tưởng này rất quan trọng trong event sourced system: concurrency nằm ở stream version chứ không phải row lock kiểu truyền thống.

---

## Aggregate projection, live aggregation và snapshot

Marten cho nhiều cách để lấy aggregate từ stream:

1. **Live aggregation**: đọc event stream rồi fold tại runtime.
2. **Inline projection**: update aggregate/read model ngay trong transaction ghi event.
3. **Async projection**: projection chạy nền, eventual consistency.
4. **Snapshot**: lưu trạng thái aggregate materialized để đọc nhanh hơn.

### Live aggregation

```csharp
var order = await session.Events.AggregateStreamAsync<Order>(orderId);
```

Ưu điểm:
- đơn giản
- luôn chính xác theo stream hiện tại

Nhược điểm:
- stream dài sẽ chậm
- không hợp cho read-heavy path

### Snapshot/inline aggregate

```csharp
options.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);
```

Khi append event, Marten sẽ materialize `Order` và lưu snapshot. Đọc lại nhanh hơn nhiều.

### Async lifecycle

```csharp
options.Projections.Snapshot<Order>(SnapshotLifecycle.Async);
```

Thích hợp khi write path cần gọn và bạn chấp nhận eventual consistency ở read side.

Chọn lifecycle là quyết định kiến trúc quan trọng:

- **Inline** khi read-after-write rất quan trọng và projection logic không quá nặng.
- **Async** khi throughput write cao hơn, projection có thể chậm, hoặc bạn có nhiều read model khác nhau.

---

## Multi-stream projection và read model cho dashboard

Thực tế, UI không đọc trực tiếp aggregate event stream mọi lúc. Nó đọc read model tối ưu cho màn hình hoặc API. Marten projection giúp bạn materialize read model theo cách rất tự nhiên.

Ví dụ read model `OrderSummary`:

```csharp
public class OrderSummary
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = "Draft";
    public int TotalItems { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
}
```

Projection:

```csharp
using Marten.Events.Aggregation;

public class OrderSummaryProjection : SingleStreamProjection<OrderSummary, Guid>
{
    public static OrderSummary Create(OrderCreated e) => new()
    {
        Id = e.OrderId,
        CustomerId = e.CustomerId,
        CreatedAt = e.CreatedAt,
        Status = "Draft"
    };

    public void Apply(OrderSummary view, OrderItemAdded e)
    {
        view.TotalItems += e.Quantity;
        view.TotalAmount += e.UnitPrice * e.Quantity;
    }

    public void Apply(OrderSummary view, OrderSubmitted e)
    {
        view.Status = "Submitted";
        view.SubmittedAt = e.SubmittedAt;
    }

    public void Apply(OrderSummary view, OrderCancelled e)
    {
        view.Status = "Cancelled";
    }
}
```

Đăng ký:

```csharp
options.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Async);
```

Đọc read model:

```csharp
app.MapGet("/order-summaries/{id:guid}", async (Guid id, IQuerySession querySession) =>
{
    var summary = await querySession.LoadAsync<OrderSummary>(id);
    return summary is not null ? Results.Ok(summary) : Results.NotFound();
});
```

Đây chính là CQRS thực dụng. Command side append event. Query side đọc projection đã được tối ưu cho màn hình hoặc báo cáo nghiệp vụ.

---

## Daemon projection và eventual consistency

Khi dùng async projection, Marten có projection daemon chạy nền để bắt event mới và update read model. Bạn cần hiểu rõ hệ quả kiến trúc:

- Write thành công không có nghĩa read model lập tức cập nhật.
- UI có thể tạm thời chưa thấy dữ liệu mới vài trăm mili giây hoặc vài giây.
- Bạn cần chọn nơi nào cần read-your-own-write, nơi nào chấp nhận eventual consistency.

Trong hệ thống lớn, đây không phải nhược điểm, mà là trade-off quan trọng. Inline projection giữ tính đồng bộ mạnh hơn nhưng tăng chi phí write. Async projection cho throughput tốt hơn và cho phép projection nặng chạy ngoài transaction chính.

---

## Strong typed IDs, tenancy và phân tách dữ liệu

Với hệ thống multi-tenant hoặc domain lớn, strongly typed id rất hữu ích.

```csharp
public readonly record struct OrderId(Guid Value);
public readonly record struct CustomerId(Guid Value);
```

Ngoài ra Marten có hỗ trợ multi-tenancy. Nếu bạn có SaaS nhiều tenant, có thể dùng tenant id để tách dữ liệu. Tùy mô hình, bạn chọn:

- tenant theo database
- tenant theo schema
- tenant theo cột/metadata trong cùng store

Mức độ tách nào phù hợp tùy yêu cầu cô lập dữ liệu, vận hành, backup, compliance.

---

## Transaction boundary, outbox và vì sao Marten rất hợp Wolverine

Đây là đoạn quan trọng nhất nếu bạn xây distributed app.

Một pain kinh điển là use case như sau:

1. Nhận command `SubmitOrder`
2. Lưu event `OrderSubmitted`
3. Publish message `OrderSubmittedIntegrationEvent` sang broker

Nếu bạn lưu DB xong rồi publish broker sau, có thể crash giữa chừng. Nếu publish trước rồi DB fail, downstream nhận event “ma”. Đây là bài toán dual write.

Marten riêng lẻ giúp bạn quản lý persistence rất tốt, nhưng khi kết hợp Wolverine, bạn có một transaction story liền mạch hơn hẳn:

- command handler chạy qua Wolverine
- Marten session và Wolverine outbox phối hợp cùng transaction
- event/domain state được lưu
- outgoing messages được bền vững
- background dispatch/retry diễn ra durable

Ví dụ command và integration event:

```csharp
public record SubmitOrderCommand(Guid OrderId);
public record OrderSubmittedIntegrationEvent(Guid OrderId, Guid CustomerId, DateTimeOffset SubmittedAt);
```

Handler với Wolverine + Marten:

```csharp
public static class SubmitOrderHandler
{
    public static async Task<object> Handle(
        SubmitOrderCommand command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var stream = await session.Events.FetchForWriting<Order>(command.OrderId, cancellationToken: cancellationToken);
        var order = stream.Aggregate ?? throw new InvalidOperationException("Order not found");

        if (order.Status != "Draft")
            throw new InvalidOperationException("Only draft orders can be submitted");

        if (!order.Items.Any())
            throw new InvalidOperationException("Order has no items");

        var submittedAt = DateTimeOffset.UtcNow;
        stream.AppendOne(new OrderSubmitted(submittedAt));

        await bus.PublishAsync(
            new OrderSubmittedIntegrationEvent(order.Id, order.CustomerId, submittedAt),
            cancellation: cancellationToken);

        await session.SaveChangesAsync(cancellationToken);

        return new { command.OrderId, Status = "Submitted" };
    }
}
```

Trong cấu hình đúng, Wolverine sẽ gắn outgoing envelope vào durable outbox liên kết với persistence boundary. Đây là điểm Marten + Wolverine được đánh giá rất cao, vì chúng được thiết kế để chơi với nhau thay vì chỉ ghép nối hời hợt.

---

## Thiết kế projection thực tế, đừng biến projection thành business brain thứ hai

Sai lầm lớn khi mới dùng event sourcing là nhét quá nhiều business logic vào projection. Projection nên chủ yếu làm 3 việc:

- materialize read model
- tính toán chỉ số tổng hợp cần cho query
- chuẩn bị dữ liệu cho downstream screen/API/reporting

Projection không nên là nơi quyết định invariant cốt lõi của domain. Invariant nên được enforce ở command handling/aggregate logic, trước khi event được append.

Ví dụ tốt:

- tính `TotalAmount`
- đánh dấu `SubmittedAt`
- gom số liệu dashboard theo ngày

Ví dụ xấu:

- trong projection mới kiểm tra tồn kho rồi hủy event
- dùng projection để quyết định order có được submit hay không
- để nhiều projection ghi chéo vào cùng một nguồn truth dẫn đến khó debug

---

## Rebuild projection, migration và tiến hóa schema

Một sức mạnh lớn của event sourcing là bạn có thể rebuild projection khi logic read model thay đổi. Nhưng điều này không phải phép màu miễn phí.

### Tình huống thường gặp

- Ban đầu `OrderSummary` chỉ có `TotalAmount`
- Sau này cần thêm `DiscountAmount`, `NetAmount`, `LastUpdatedAt`
- Bạn sửa projection rồi rebuild từ stream cũ

Ưu điểm:
- Không cần sửa từng row tay
- Có thể tái tính toàn bộ read model từ lịch sử thật

Nhược điểm:
- Stream quá lớn sẽ rebuild lâu
- Rebuild toàn hệ thống cần kế hoạch downtime hoặc side-by-side deployment
- Nếu event schema thay đổi kém kỷ luật, việc replay sẽ đau

### Kỷ luật version event

Nên xem event như hợp đồng lịch sử. Tránh chỉnh sửa meaning của event cũ. Nếu business thay đổi, nhiều khi tốt hơn là tạo event mới thay vì sửa semantics của event cũ.

Ví dụ thay vì sửa `OrderSubmitted`, bạn có thể thêm `OrderSubmissionConfirmed` hay `OrderPricingRecalculated` nếu nghiệp vụ thay đổi.

---

## Metadata của event, correlation và tracing

Trong distributed app, chỉ lưu event payload là chưa đủ. Bạn thường cần metadata:

- `CorrelationId`
- `CausationId`
- tenant id
- user id
- source system
- timestamp chính xác
- headers phục vụ observability

Marten hỗ trợ metadata trên event rất hữu ích cho debugging. Khi một đơn hàng bị sai trạng thái, bạn có thể lần lại chuỗi event, request nào phát ra, message nào gây ra, correlation nào liên quan.

Nếu kết hợp OpenTelemetry và Wolverine, câu chuyện tracing còn rõ hơn nữa. Bạn có thể nối request ban đầu, command handler, event append, outgoing message, downstream consumer vào cùng một trace.

---

## Mô hình hóa aggregate đúng cách trong Marten

Một số nguyên tắc rất đáng giữ:

### 1. Aggregate vừa phải

Không nên biến một stream thành “thùng rác của mọi sự kiện”. Một aggregate stream nên xoay quanh một consistency boundary rõ ràng.

Tốt:
- `Order-{id}` chứa vòng đời order
- `Subscription-{id}` chứa vòng đời subscription

Không tốt:
- một stream chứa cả customer, order, payment, shipment chỉ vì cùng liên quan đến một user

### 2. Event là sự thật đã xảy ra

Tên event nên ở thì quá khứ:

- `OrderCreated`
- `PaymentAuthorized`
- `ShipmentPrepared`

Tránh dùng command giả làm event như `CreateOrderRequested` trong event store nếu đó chưa phải sự thật domain.

### 3. Aggregate method nên bảo vệ invariant

Ví dụ:

```csharp
public IEnumerable<object> Submit()
{
    if (Status != "Draft")
        throw new InvalidOperationException("Order is not draft");

    if (Items.Count == 0)
        throw new InvalidOperationException("Order has no items");

    yield return new OrderSubmitted(DateTimeOffset.UtcNow);
}
```

Pattern này giúp business rule rõ ràng và test tốt.

---

## Testing với Marten

Nếu dùng Marten nghiêm túc, hãy test ít nhất ở 3 tầng:

1. **Unit test aggregate behavior**: command -> events
2. **Integration test persistence**: append event, load aggregate, query projection
3. **End-to-end test flow**: API/message -> persisted state -> read model

### Unit test aggregate

```csharp
[Fact]
public void submit_order_should_raise_order_submitted_event()
{
    var order = Order.Create(new OrderCreated(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
    order.Apply(new OrderItemAdded(Guid.NewGuid(), "Keyboard", 100, 2));

    var events = order.Submit().ToList();

    events.ShouldHaveSingleItem().ShouldBeOfType<OrderSubmitted>();
}
```

### Integration test với Postgres container

```csharp
[Fact]
public async Task append_and_project_order_summary()
{
    await using var store = DocumentStore.For(options =>
    {
        options.Connection("Host=localhost;Port=5432;Database=test_db;Username=postgres;Password=postgres");
        options.AutoCreateSchemaObjects = AutoCreate.All;
        options.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Inline);
    });

    await using var session = store.LightweightSession();

    var orderId = Guid.NewGuid();
    session.Events.StartStream<Order>(orderId,
        new OrderCreated(orderId, Guid.NewGuid(), DateTimeOffset.UtcNow),
        new OrderItemAdded(Guid.NewGuid(), "Mouse", 20, 3),
        new OrderSubmitted(DateTimeOffset.UtcNow));

    await session.SaveChangesAsync();

    await using var query = store.QuerySession();
    var summary = await query.LoadAsync<OrderSummary>(orderId);

    summary.ShouldNotBeNull();
    summary.TotalItems.ShouldBe(3);
    summary.Status.ShouldBe("Submitted");
}
```

Thực chiến hơn nữa thì nên chạy PostgreSQL bằng Testcontainers trong test suite.

---

## Vận hành production, những điều cần nghĩ sớm

Marten chạy trên PostgreSQL, nên nhiều bài toán production cũng xoay quanh PostgreSQL:

- connection pool
- autovacuum
- disk growth của event table
- backup và point-in-time recovery
- index bloat
- monitoring slow query
- projection daemon health

### Một số lời khuyên thực dụng

- Đừng bật quá nhiều duplicated fields và index nếu chưa có use case query rõ ràng.
- Phân tách read model nặng khỏi aggregate nếu query pattern khác xa.
- Theo dõi kích thước `mt_events` và chiến lược retention nếu event rất nhiều.
- Chuẩn hóa metadata, correlation id ngay từ đầu.
- Chọn inline hay async projection bằng số đo thật, không phải cảm giác.
- Dùng migration/schema management có kỷ luật khi lên production.

---

## Demo kiến trúc hoàn chỉnh cho order service

Một kiến trúc Marten tương đối đẹp có thể như sau:

### Command side

- API nhận request
- map sang command
- command handler load aggregate từ stream
- enforce business rule
- append event
- lưu và publish integration event qua Wolverine

### Query side

- projection `OrderSummary`
- projection `CustomerOrderStats`
- projection `DailyRevenue`
- API dashboard/query chỉ đọc projection

### Integration side

- sự kiện `OrderSubmittedIntegrationEvent`
- downstream payment service tiêu thụ event
- khi payment ok, service khác phát `PaymentAuthorized`
- command/handler tiếp tục append vào stream order hoặc stream payment tùy boundary

Điều quan trọng là Marten giúp bạn tách command model và query model mà không cần dựng một bộ hạ tầng quá nặng.

---

## Ví dụ end-to-end ngắn gọn

```csharp
public record CreateOrderApiRequest(Guid CustomerId);

app.MapPost("/orders", async (CreateOrderApiRequest request, IDocumentSession session) =>
{
    var orderId = Guid.NewGuid();

    session.Events.StartStream<Order>(orderId,
        new OrderCreated(orderId, request.CustomerId, DateTimeOffset.UtcNow));

    await session.SaveChangesAsync();

    return Results.Created($"/orders/{orderId}", new { orderId });
});

app.MapPost("/orders/{id:guid}/items", async (Guid id, AddOrderItem request, IDocumentSession session) =>
{
    var stream = await session.Events.FetchForWriting<Order>(id);
    if (stream.Aggregate is null) return Results.NotFound();

    if (stream.Aggregate.Status != "Draft")
        return Results.BadRequest("Order is not draft");

    stream.AppendOne(new OrderItemAdded(
        request.ProductId,
        request.ProductName,
        request.UnitPrice,
        request.Quantity));

    await session.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/orders/{id:guid}", async (Guid id, IQuerySession querySession) =>
{
    var order = await querySession.LoadAsync<Order>(id);
    return order is not null ? Results.Ok(order) : Results.NotFound();
});
```

Trong code thật, bạn thường đóng gói logic tốt hơn bằng handler/service riêng thay vì nhét vào minimal API, nhưng ví dụ này cho thấy flow tổng thể rất mạch lạc.

---

## Những lỗi phổ biến khi mới dùng Marten

1. **Biến mọi object thành document**
   - Kết quả là model lộn xộn, query yếu, boundary mơ hồ.

2. **Không nghĩ về index và duplicated field**
   - Lúc đầu chạy ngon, sau đó query production chậm bất ngờ.

3. **Dùng event sourcing cho mọi thứ**
   - Có những nơi CRUD document là đủ, không cần replay lịch sử.

4. **Nhét business logic nặng vào projection**
   - Dẫn tới khó debug và replay nguy hiểm.

5. **Không quản lý event contract cẩn thận**
   - Đến lúc rebuild projection thì event cũ không còn ý nghĩa rõ ràng.

6. **Không phân biệt command model và read model**
   - Cuối cùng query trực tiếp aggregate cho mọi màn hình, mất hết lợi ích projection.

7. **Xem Marten chỉ là storage library**
   - Thực ra sức mạnh của nó là mô hình kiến trúc quanh aggregate, event stream và projection.

---

## Marten và Wolverine, bộ đôi nên hiểu cùng nhau

Nếu Marten là persistence backbone cho domain state và event history, thì Wolverine là execution và messaging backbone cho application workflow. Khi ghép lại, bạn có một stack rất hợp cho distributed .NET app:

- Marten giữ document, stream, projection
- Wolverine xử lý command/message handler
- Wolverine durable inbox/outbox giải bài toán dual write
- Marten lưu state nhất quán
- background processing và downstream integration trở nên mượt hơn nhiều

Một pattern rất mạnh là:

1. HTTP hoặc external message đi vào Wolverine handler.
2. Handler dùng Marten để load aggregate hoặc stream.
3. Business rule chạy trong handler/domain.
4. Append event vào Marten.
5. Publish outgoing message qua Wolverine.
6. Commit một lần.
7. Projection hoặc downstream handler cập nhật read model, gửi email, đồng bộ service khác.

Trong hệ thống vừa cần event sourcing vừa cần messaging durability, đây là một trong những cặp thư viện đáng học nhất trong hệ .NET hiện nay.

---

## Checklist thiết kế trước khi đưa Marten vào dự án

Trước khi đội ngũ commit nghiêm túc với Marten, tôi thường đi qua một checklist khá đơn giản nhưng cực hữu ích. Nếu phần lớn câu trả lời là “có”, Marten thường rất hợp:

- Bạn có aggregate boundary khá rõ chưa?
- Bạn có cần lịch sử thay đổi hoặc audit trail có ý nghĩa nghiệp vụ không?
- Bạn có ít nhất vài flow đáng được model bằng event thay vì update row trực tiếp không?
- Bạn có read model khác biệt rõ với write model không?
- Team có sẵn sàng chấp nhận eventual consistency ở một số màn hình không?
- Team có kỷ luật version event và migration tốt không?
- Bạn có hạ tầng PostgreSQL đủ tốt để trở thành persistence backbone không?

Ngược lại, nếu câu chuyện thực tế chỉ là vài bảng quản lý danh mục, vài CRUD endpoint, một ít báo cáo join SQL, thì việc đưa Marten vào chỉ làm tăng cognitive load. Một kiến trúc tốt không phải kiến trúc nhiều pattern nhất, mà là kiến trúc vừa đủ cho mức độ phức tạp thật của bài toán.

## Gợi ý lộ trình học Marten theo từng nấc

Nếu bạn mới bắt đầu, đừng cố nuốt cả event sourcing, projection, async daemon, outbox, multi-tenancy trong một tuần. Lộ trình dễ hấp thụ hơn thường là:

### Giai đoạn 1, dùng Marten như document database

- tạo 1 service nhỏ
- lưu 2 hoặc 3 document aggregate cơ bản
- học query, index, duplicated field
- làm quen session lifecycle và optimistic concurrency

### Giai đoạn 2, thêm event sourcing cho một aggregate có lifecycle rõ

- chọn Order, Subscription hoặc Booking
- model 5 đến 8 event quan trọng nhất
- viết aggregate apply methods
- load aggregate từ stream
- thêm snapshot/projection đơn giản

### Giai đoạn 3, tách read model bằng projection

- dựng `Summary` view cho UI
- thử inline projection trước
- đo write cost và read benefit
- sau đó mới chuyển vài projection sang async nếu cần

### Giai đoạn 4, ghép với Wolverine

- nhận command qua handler
- append event bằng Marten
- publish integration event bằng Wolverine
- hiểu rõ transaction boundary và outbox story

Cách học theo nấc như vậy giúp team thấy giá trị dần dần, thay vì biến Marten thành một cú nhảy kiến trúc quá lớn ngay từ đầu.

## Kết luận

Marten không phải là “EF Core nhưng lưu JSON”. Nó là một cách tổ chức application state và domain history rất khác. Giá trị lớn nhất của Marten nằm ở chỗ nó cho phép bạn:

- mô hình hóa aggregate tự nhiên hơn
- lưu event stream có kỷ luật
- dựng projection/read model thực dụng
- tận dụng sức mạnh của PostgreSQL thay vì vận hành thêm một event store riêng
- ghép rất tốt với Wolverine để xử lý command, outbox và workflow bền vững

Nếu hệ thống của bạn thật sự cần audit trail, domain event, CQRS thực dụng, hoặc event sourcing mà vẫn muốn bám vào PostgreSQL, Marten là một lựa chọn cực kỳ đáng đầu tư. Còn nếu nhu cầu chỉ là CRUD quan hệ đơn giản, hãy thẳng thắn, có thể EF Core sẽ nhẹ nhàng hơn.

Điều quan trọng nhất khi học Marten không phải là nhớ API nào để `Store` hay `Append`. Điều quan trọng là học cách nhìn domain theo aggregate boundary, event history, consistency model, và read model. Khi nắm được những thứ đó, Marten trở thành một công cụ rất sắc bén, không chỉ là một thư viện persistence.
