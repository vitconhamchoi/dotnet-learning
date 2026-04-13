# Dapr chuyên sâu cho .NET: sidecar runtime, pub/sub, state store, bindings, secrets, workflow và service invocation

## Dapr là gì và vì sao nó đáng học?

Dapr, viết tắt của Distributed Application Runtime, không phải là một framework ép bạn viết ứng dụng theo một khuôn cứng. Nó là một runtime dạng sidecar, cung cấp một tập building blocks cho distributed application: service invocation, state management, pub/sub, bindings, secrets, configuration, actors, workflow, lock, cryptography và observability. Điểm rất khác của Dapr là phần lớn năng lực này được đưa ra ngoài process ứng dụng, rồi expose qua HTTP/gRPC để app .NET, Go, Java, Python, Node, thậm chí cả app legacy đều có thể dùng được.

Nếu bạn đã quen với ASP.NET Core, RabbitMQ, Redis, PostgreSQL, Kafka, Azure Service Bus, Kubernetes, có thể bạn sẽ hỏi: Dapr có thay thế hết đống đó không? Câu trả lời là không. Dapr không thay broker, không thay database, không thay orchestrator. Nó đứng ở giữa để chuẩn hóa cách app của bạn dùng những thành phần đó. Nhờ vậy, business code ít dính chặt vào vendor hơn, việc local development dễ hơn, và nhiều distributed primitive trở nên đồng nhất hơn.

Điểm hấp dẫn nhất của Dapr với hệ .NET là bạn vẫn viết ứng dụng bằng ASP.NET Core quen thuộc, vẫn dùng DI, Minimal API, controller, hosted service, logging, OpenTelemetry, nhưng bạn có thêm một lớp runtime giúp:

- gọi service khác dễ hơn mà không hard-code URL
- publish event mà không khóa chặt vào một broker cụ thể
- lưu state qua abstraction ổn định
- bind tới queue, storage, cron, webhook bằng component config
- lấy secret và config theo cách chuẩn hóa
- xây workflow dài, có retry, checkpoint, compensation
- scale trên local Docker hoặc Kubernetes mà ít đổi code

Nói ngắn gọn, Dapr đáng học khi bạn muốn xây distributed system thực dụng, multi-service, nhiều integration point, nhưng không muốn tự code quá nhiều plumbing.

---

## Khi nào nên dùng Dapr, khi nào không nên dùng?

### Nên dùng Dapr khi

1. Bạn có nhiều service cần giao tiếp qua HTTP/gRPC và muốn service discovery cùng retry policy nhất quán.
2. Bạn cần pub/sub nhưng không muốn business code phụ thuộc nặng vào RabbitMQ, Kafka hay Azure Service Bus SDK.
3. Bạn muốn một abstraction chung cho Redis state, secret store, bindings, config store, workflow.
4. Bạn cần chạy local trước rồi mới lên Kubernetes, với trải nghiệm dev tương đối giống production.
5. Hệ thống có nhiều thành phần khác ngôn ngữ, ví dụ .NET backend, Python worker, Node gateway.
6. Team muốn giảm boilerplate cho distributed concerns, nhất là observability, retries, sidecar-mediated invocation.

### Không nên dùng Dapr khi

1. Bạn chỉ có một monolith hoặc 1-2 service đơn giản, không có nhu cầu distributed primitives. Lúc này Dapr có thể là thêm complexity.
2. Bạn cần quyền kiểm soát rất thấp-level trên transport/broker và muốn tận dụng đầy đủ tính năng vendor-specific.
3. Team chưa quen với sidecar model, trong khi môi trường vận hành rất hạn chế và không muốn thêm process/container phụ.
4. Bạn đang có kiến trúc event-driven ổn định với framework như MassTransit hoặc NServiceBus, và không có pain rõ ràng cần Dapr giải quyết.
5. Bạn muốn toàn bộ logic stateful cực chặt trong process theo actor model chuyên dụng, khi đó Orleans có thể tự nhiên hơn cho một số bài toán.

Dapr không phải thuốc tiên. Nó mạnh nhất khi team thật sự cần một lớp chuẩn hóa cho distributed building blocks, chứ không phải vì nghe thấy từ khóa cloud-native.

---

## Mental model: sidecar hoạt động như thế nào?

Một ứng dụng Dapr thường có hai process chính:

- process app của bạn, ví dụ ASP.NET Core API chạy cổng 5000
- process sidecar `daprd`, ví dụ chạy cổng HTTP 3500 và gRPC 50001

App không cần trực tiếp nói chuyện với Redis hay RabbitMQ nếu dùng Dapr building blocks. Thay vào đó app gọi sidecar, sidecar dùng component config để biết phải nói chuyện với backend nào.

```text
Client -> ASP.NET Core API -> Dapr sidecar -> Redis / RabbitMQ / Kafka / Secret store / Another service
```

Khi service A muốn gọi service B, A thường gọi sidecar local, sidecar local dựa vào app id của B để định tuyến sang sidecar của B rồi vào app B.

```text
Service A code
   -> localhost:3500/v1.0/invoke/order-service/method/orders/123
      -> daprd của A
         -> service discovery / mTLS / retries
            -> daprd của order-service
               -> HTTP endpoint trong order-service
```

Lợi ích của model này:

- app code ít biết chi tiết infrastructure
- cross-cutting concerns có thể tập trung ở sidecar
- nhiều ngôn ngữ dùng cùng API contract
- component backend có thể thay tương đối nhẹ

Cái giá phải trả:

- thêm một hop
- thêm process cần quan sát
- debugging cần hiểu cả app lẫn sidecar
- abstraction có thể che bớt tính năng riêng của backend

---

## Cài Dapr cho local development

Trên macOS hoặc Linux, bạn có thể cài Dapr CLI rồi init local environment. Ví dụ:

```bash
brew install dapr/tap/dapr-cli

dapr init
```

Lệnh `dapr init` thường kéo về placement service, scheduler, Redis và Zipkin cho môi trường local mặc định. Sau đó kiểm tra:

```bash
dapr --version
dapr status -k   # nếu dùng Kubernetes
```

Nếu chạy self-hosted local:

```bash
dapr list
```

Trong .NET project, bạn sẽ thường cài các package sau:

```bash
dotnet add package Dapr.AspNetCore
dotnet add package Dapr.Client
dotnet add package Dapr.Workflow
```

Tuỳ phần nào bạn dùng mà chọn package tương ứng. `Dapr.AspNetCore` tích hợp service invocation, pub/sub subscription, bindings input. `Dapr.Client` cho phép code chủ động gọi Dapr API. `Dapr.Workflow` dùng cho workflow runtime mới.

---

## Cấu trúc project demo xuyên suốt bài này

Để tutorial thực chiến, ta dùng một hệ mini-commerce gồm các service sau:

- `api-gateway`: nhận request từ client
- `catalog-service`: đọc thông tin sản phẩm
- `order-service`: tạo đơn hàng, lưu state, publish event
- `payment-worker`: xử lý thanh toán từ event
- `notification-worker`: gửi email giả lập qua binding

Backend infrastructure local:

- Redis cho state store
- Redis pub/sub hoặc RabbitMQ cho message bus, tùy component
- file/console binding cho demo
- secret store local file

Một phần của solution có thể như sau:

```text
src/
  ApiGateway/
  CatalogService/
  OrderService/
  PaymentWorker/
  NotificationWorker/
components/
  statestore.yaml
  pubsub.yaml
  secretstore.yaml
  bindings.yaml
```

---

## Chạy một ASP.NET Core service với Dapr sidecar

Tạo một API đơn giản:

```bash
dotnet new webapi -n OrderService
cd OrderService
dotnet add package Dapr.AspNetCore
dotnet add package Dapr.Client
```

`Program.cs` cơ bản:

```csharp
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.Run();
```

Một controller kiểm tra health:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", service = "order-service" });
}
```

Chạy app bình thường:

```bash
dotnet run
```

Hoặc chạy qua Dapr sidecar:

```bash
dapr run \
  --app-id order-service \
  --app-port 5167 \
  --dapr-http-port 3500 \
  --resources-path ./components \
  dotnet run
```

Giờ sidecar đã đứng cạnh app. Bạn có thể gọi API app trực tiếp hoặc qua Dapr invocation endpoint.

Gọi trực tiếp:

```bash
curl http://localhost:5167/orders/health
```

Gọi qua sidecar:

```bash
curl http://localhost:3500/v1.0/invoke/order-service/method/orders/health
```

Đây là điểm khởi đầu cho hầu hết building blocks khác.

---

## Service invocation: gọi service khác mà không hard-code URL

Giả sử `api-gateway` cần gọi `catalog-service`. Không cần lưu base URL của catalog trong config app như cách truyền thống. Chỉ cần biết app id của service kia.

`Program.cs` cho gateway:

```csharp
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();

app.MapGet("/products/{id}", async (string id, DaprClient daprClient, CancellationToken ct) =>
{
    var product = await daprClient.InvokeMethodAsync<object>(
        HttpMethod.Get,
        "catalog-service",
        $"products/{id}",
        cancellationToken: ct);

    return Results.Ok(product);
});

app.Run();
```

Bên `catalog-service`:

```csharp
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var products = new Dictionary<string, object>
{
    ["p-100"] = new { Id = "p-100", Name = "Mechanical Keyboard", Price = 120m },
    ["p-200"] = new { Id = "p-200", Name = "4K Monitor", Price = 480m }
};

app.MapGet("/products/{id}", (string id) =>
{
    return products.TryGetValue(id, out var product)
        ? Results.Ok(product)
        : Results.NotFound();
});

app.Run();
```

Chạy hai service với hai sidecar khác nhau:

```bash
# terminal 1
cd CatalogService
DAPR_HTTP_PORT=3501 dapr run --app-id catalog-service --app-port 5201 --dapr-http-port 3501 dotnet run

# terminal 2
cd ApiGateway
dapr run --app-id api-gateway --app-port 5101 --dapr-http-port 3500 dotnet run
```

Client chỉ gọi gateway:

```bash
curl http://localhost:5101/products/p-100
```

### Service invocation có gì hay ngoài việc gọi hộ URL?

Dapr còn có thể gắn retry policy, service discovery, tracing, và khi chạy trên Kubernetes thì app id map với service identity một cách tự nhiên hơn.

Bạn cũng có thể gọi raw HTTP:

```csharp
var request = daprClient.CreateInvokeMethodRequest(HttpMethod.Post, "order-service", "orders");
request.Content = JsonContent.Create(new { ProductId = "p-100", Quantity = 2 });

var response = await daprClient.InvokeMethodWithResponseAsync(request);
response.EnsureSuccessStatusCode();
```

Khi service-to-service traffic trở nên dày hơn, sidecar model giúp bạn giữ business code gọn, nhất là khi hệ thống có nhiều environment.

---

## State management: lưu state qua Dapr state store

Đây là feature rất thực dụng. Thay vì mỗi service trực tiếp biết Redis SDK hoặc Cosmos SDK, app chỉ nói chuyện với một state store tên logic, ví dụ `statestore`.

### Component state store

`components/statestore.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
  metadata:
    - name: redisHost
      value: localhost:6379
    - name: redisPassword
      value: ""
```

### Mô hình dữ liệu order

```csharp
public record CreateOrderRequest(string ProductId, int Quantity, string CustomerEmail);

public class OrderState
{
    public string OrderId { get; set; } = default!;
    public string ProductId { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Status { get; set; } = "Pending";
    public string CustomerEmail { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}
```

### Ghi state

```csharp
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly DaprClient _daprClient;

    public OrdersController(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var order = new OrderState
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            UnitPrice = 120m,
            CustomerEmail = request.CustomerEmail,
            Status = "PendingPayment",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _daprClient.SaveStateAsync("statestore", $"order:{order.OrderId}", order);

        return Accepted(order);
    }
}
```

### Đọc state

```csharp
[HttpGet("{orderId}")]
public async Task<IActionResult> Get(string orderId)
{
    var order = await _daprClient.GetStateAsync<OrderState>("statestore", $"order:{orderId}");

    return order is null ? NotFound() : Ok(order);
}
```

### Xoá state

```csharp
[HttpDelete("{orderId}")]
public async Task<IActionResult> Delete(string orderId)
{
    await _daprClient.DeleteStateAsync("statestore", $"order:{orderId}");
    return NoContent();
}
```

### ETag và optimistic concurrency

State store trong distributed system hiếm khi chỉ là chuyện save/get đơn giản. Bạn thường cần chống lost update. Dapr hỗ trợ ETag:

```csharp
[HttpPost("{orderId}/confirm")]
public async Task<IActionResult> Confirm(string orderId)
{
    var state = await _daprClient.GetStateAndETagAsync<OrderState>("statestore", $"order:{orderId}");
    if (state.Value is null)
    {
        return NotFound();
    }

    if (state.Value.Status != "Paid")
    {
        return Conflict(new { message = "Order is not paid yet." });
    }

    state.Value.Status = "Confirmed";

    await _daprClient.TrySaveStateAsync(
        "statestore",
        $"order:{orderId}",
        state.Value,
        state.ETag,
        StateOptions.ConcurrencyMode.FirstWrite,
        cancellationToken: HttpContext.RequestAborted);

    return Ok(state.Value);
}
```

Nếu có ghi đè cạnh tranh, save có thể fail để bạn retry hoặc báo conflict. Đây là thứ rất quan trọng khi nhiều worker cùng xử lý một entity.

### Transactional state operations

Một số state store hỗ trợ transaction nhiều operation trong một lần gọi:

```csharp
var operations = new List<StateTransactionRequest>
{
    new("upsert", "order:123", order),
    new("upsert", "outbox:123", new { Event = "OrderCreated", OrderId = "123" })
};

await _daprClient.ExecuteStateTransactionAsync("statestore", operations);
```

Tuy không phải mọi store đều support transaction giống nhau, nhưng đây là building block hữu ích cho những flow cần atomicity mức vừa phải.

---

## Pub/Sub: event-driven giữa các service

Dapr pub/sub là lý do nhiều team chú ý đến nó. Bạn định nghĩa một component pubsub rồi service publish event mà không bị buộc vào SDK broker cụ thể.

### Component pubsub với Redis

`components/pubsub.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
    - name: redisHost
      value: localhost:6379
    - name: redisPassword
      value: ""
```

### Publish event khi tạo order

```csharp
public record OrderCreatedIntegrationEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal UnitPrice,
    string CustomerEmail);

[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
{
    var order = new OrderState
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        UnitPrice = 120m,
        CustomerEmail = request.CustomerEmail,
        Status = "PendingPayment",
        CreatedAtUtc = DateTime.UtcNow
    };

    await _daprClient.SaveStateAsync("statestore", $"order:{order.OrderId}", order);

    var evt = new OrderCreatedIntegrationEvent(
        order.OrderId,
        order.ProductId,
        order.Quantity,
        order.UnitPrice,
        order.CustomerEmail);

    await _daprClient.PublishEventAsync("pubsub", "orders.created", evt);

    return Accepted(order);
}
```

### Subscribe event ở payment worker

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();
app.Run();
```

Controller subscriber:

```csharp
using Dapr;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class PaymentSubscriberController : ControllerBase
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<PaymentSubscriberController> _logger;

    public PaymentSubscriberController(DaprClient daprClient, ILogger<PaymentSubscriberController> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    [Topic("pubsub", "orders.created")]
    [HttpPost("payments/process")]
    public async Task<IActionResult> Process(OrderCreatedIntegrationEvent message)
    {
        _logger.LogInformation("Processing payment for order {OrderId}", message.OrderId);

        await Task.Delay(500);

        await _daprClient.PublishEventAsync("pubsub", "payments.completed", new
        {
            message.OrderId,
            PaidAtUtc = DateTime.UtcNow,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..12]
        });

        return Ok();
    }
}
```

### Subscriber cập nhật state order khi thanh toán xong

```csharp
[ApiController]
public class PaymentCompletedController : ControllerBase
{
    private readonly DaprClient _daprClient;

    public PaymentCompletedController(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    [Topic("pubsub", "payments.completed")]
    [HttpPost("orders/payment-completed")]
    public async Task<IActionResult> PaymentCompleted([FromBody] JsonElement payload)
    {
        var orderId = payload.GetProperty("OrderId").GetString()!;
        var order = await _daprClient.GetStateAsync<OrderState>("statestore", $"order:{orderId}");

        if (order is null)
        {
            return NotFound();
        }

        order.Status = "Paid";
        await _daprClient.SaveStateAsync("statestore", $"order:{orderId}", order);

        return Ok();
    }
}
```

### Dead letter, retry, idempotency

Pub/sub abstraction giúp code gọn, nhưng bạn vẫn phải nghĩ như một người làm distributed system thực thụ:

- message có thể được deliver lại
- handler có thể crash ở giữa chừng
- event order không phải lúc nào cũng guaranteed như bạn tưởng
- idempotency vẫn là trách nhiệm của app

Một pattern phổ biến là lưu processed message id:

```csharp
public class ProcessedMessage
{
    public string Id { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}
```

```csharp
[Topic("pubsub", "orders.created")]
[HttpPost("payments/process")]
public async Task<IActionResult> Process(
    [FromBody] OrderCreatedIntegrationEvent message,
    [FromHeader(Name = "ce-id")] string cloudEventId)
{
    var key = $"msg:{cloudEventId}";
    var alreadyProcessed = await _daprClient.GetStateAsync<ProcessedMessage>("statestore", key);
    if (alreadyProcessed is not null)
    {
        return Ok();
    }

    await _daprClient.SaveStateAsync("statestore", key, new ProcessedMessage
    {
        Id = cloudEventId,
        ProcessedAtUtc = DateTime.UtcNow
    });

    // process actual business logic

    return Ok();
}
```

Dapr giúp vận chuyển event, nhưng tư duy idempotent consumer vẫn là bắt buộc.

---

## Input và output bindings

Bindings là cách rất hay để nối app với các external system hoặc trigger mà không cần trực tiếp dùng SDK của từng nơi. Có hai loại tư duy chính:

- input binding: external system gọi vào app, ví dụ queue, cron, blob event
- output binding: app đẩy dữ liệu ra external target, ví dụ queue, email adapter, storage

### Ví dụ output binding để gửi notification giả lập

`components/notification.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: notification
spec:
  type: bindings.http
  version: v1
  metadata:
    - name: url
      value: https://example.org/hooks/notify
```

Gọi binding từ .NET:

```csharp
public class NotificationService
{
    private readonly DaprClient _daprClient;

    public NotificationService(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    public async Task SendOrderPaidEmailAsync(OrderState order)
    {
        await _daprClient.InvokeBindingAsync(
            "notification",
            "post",
            new
            {
                to = order.CustomerEmail,
                subject = $"Order {order.OrderId} paid successfully",
                body = $"Your order for {order.ProductId} has been paid."
            });
    }
}
```

### Input binding với cron

`components/order-reminder-cron.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: order-reminder
spec:
  type: bindings.cron
  version: v1
  metadata:
    - name: schedule
      value: "@every 30s"
```

Endpoint nhận trigger:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class ReminderController : ControllerBase
{
    private readonly ILogger<ReminderController> _logger;

    public ReminderController(ILogger<ReminderController> logger)
    {
        _logger = logger;
    }

    [HttpPost("reminders/pending-orders")]
    public IActionResult Run([FromBody] object payload)
    {
        _logger.LogInformation("Cron trigger received at {Time}", DateTime.UtcNow);
        return Ok();
    }
}
```

Khi dùng bindings, bạn biến nhiều integration point thành contract HTTP/gRPC khá đồng nhất. Điều này rất tiện cho team muốn triển khai nhanh các external trigger.

---

## Secrets và configuration

Không có distributed app nghiêm túc nào lại hard-code password trong `appsettings.json` rồi commit lên repo. Dapr hỗ trợ secret store abstraction, ví dụ local file, Kubernetes secret, HashiCorp Vault, Azure Key Vault.

### Local secret store demo

`components/secretstore.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: localsecretstore
spec:
  type: secretstores.local.file
  version: v1
  metadata:
    - name: secretsFile
      value: ./components/secrets.json
```

`components/secrets.json`:

```json
{
  "smtp-password": "super-secret-value",
  "payment-api-key": "dev-key-123"
}
```

Đọc secret trong .NET:

```csharp
var secret = await daprClient.GetSecretAsync("localsecretstore", "payment-api-key");
var apiKey = secret["payment-api-key"];
```

### Tại sao secret abstraction hữu ích?

- dev local và production cùng coding model
- business code ít biết backend secret provider
- dễ xoay từ local file sang Vault hoặc Key Vault
- tránh đẩy quá nhiều infrastructure logic vào app layer

Tuy vậy, bạn vẫn cần governance chuẩn: rotate secret, audit access, scope permission, đừng xem abstraction là lý do để lỏng kỷ luật bảo mật.

---

## Configuration subscribe và dynamic config

Dapr cũng có configuration building block, cho phép đọc config từ backend store và subscribe thay đổi. Đây là thứ hữu ích nếu bạn cần dynamic config không muốn redeploy app cho mọi thay đổi nhỏ.

Ví dụ ý tưởng dùng cho feature flag đơn giản:

```csharp
var items = await daprClient.GetConfiguration("configstore", new List<string>
{
    "checkout.maxItems",
    "checkout.enableCoupons"
});

var maxItems = items["checkout.maxItems"].Value;
```

Với những app cần runtime tuning, rate limit config, toggles theo môi trường, config building block có thể rất tiện. Nhưng nếu hệ thống của bạn đã có giải pháp config mạnh rồi, không nhất thiết phải ép dùng Dapr chỉ vì nó có sẵn.

---

## Actors trong Dapr

Dapr có actor model riêng. Nó không giống Orleans hoàn toàn, nhưng cùng giải một lớp bài toán: stateful unit theo identity, activation theo nhu cầu, method invocation tuần tự trên từng actor instance.

### Khi nào actor Dapr hợp lý?

- cần state gắn với từng entity nhỏ
- mỗi entity có lifecycle đơn giản
- muốn timer/reminder trên entity
- hệ thống đa ngôn ngữ, không muốn lock vào Orleans/.NET runtime

### Khi nào actor Dapr không phải lựa chọn tốt nhất?

- bạn cần hệ sinh thái actor sâu và native .NET hơn, Orleans có thể mạnh hơn
- domain logic phức tạp cần patterns/grain ecosystem quanh .NET
- bạn không thật sự cần actor, chỉ cần state store + worker là đủ

Ví dụ interface actor:

```csharp
using Dapr.Actors;

public interface ICartActor : IActor
{
    Task AddItemAsync(string productId, int quantity);
    Task<CartState> GetStateAsync();
}

public class CartState
{
    public Dictionary<string, int> Items { get; set; } = new();
}
```

Implementation:

```csharp
using Dapr.Actors.Runtime;

public class CartActor : Actor, ICartActor
{
    public CartActor(ActorHost host) : base(host)
    {
    }

    public async Task AddItemAsync(string productId, int quantity)
    {
        var state = await StateManager.TryGetStateAsync<CartState>("cart");
        var cart = state.HasValue ? state.Value : new CartState();

        cart.Items[productId] = cart.Items.TryGetValue(productId, out var existing)
            ? existing + quantity
            : quantity;

        await StateManager.SetStateAsync("cart", cart);
    }

    public async Task<CartState> GetStateAsync()
    {
        var state = await StateManager.TryGetStateAsync<CartState>("cart");
        return state.HasValue ? state.Value : new CartState();
    }
}
```

Đăng ký actor trong app:

```csharp
using Dapr.Actors.AspNetCore;
using Dapr.Actors.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<CartActor>();
});

var app = builder.Build();
app.MapActorsHandlers();
app.Run();
```

Client gọi actor:

```csharp
using Dapr.Actors;
using Dapr.Actors.Client;

var actorId = new ActorId("cart-user-123");
var proxy = ActorProxy.Create<ICartActor>(actorId, nameof(CartActor));
await proxy.AddItemAsync("p-100", 2);
var cart = await proxy.GetStateAsync();
```

Actor là phần thú vị của Dapr, nhưng trong thực chiến rất nhiều team dùng Dapr chủ yếu vì service invocation, state, pub/sub, bindings trước khi đụng tới actors.

---

## Workflow trong Dapr

Workflow là một capability mới và rất đáng chú ý. Nó giải quyết bài toán long-running orchestration, checkpoint state, timer, retries, compensation, tách biệt orchestration logic với activity thực thi.

Nếu bạn từng viết saga thủ công bằng hàng đống status flag trong DB, cron retry, background worker tự chế, bạn sẽ thấy workflow đáng giá.

### Use case mẫu

Khi khách tạo order:

1. reserve inventory
2. process payment
3. create shipment
4. send notification
5. nếu payment fail thì release inventory

Đây là luồng điển hình có compensation.

### Cài package và đăng ký

```bash
dotnet add package Dapr.Workflow
```

`Program.cs`:

```csharp
using Dapr.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddWorkflow(options =>
{
    options.RegisterWorkflow<OrderFulfillmentWorkflow>();
    options.RegisterActivity<ReserveInventoryActivity>();
    options.RegisterActivity<ProcessPaymentActivity>();
    options.RegisterActivity<CreateShipmentActivity>();
    options.RegisterActivity<ReleaseInventoryActivity>();
    options.RegisterActivity<SendNotificationActivity>();
});

var app = builder.Build();
app.MapPost("/checkout", async (CheckoutRequest request, DaprWorkflowClient workflowClient) =>
{
    var instanceId = await workflowClient.ScheduleNewWorkflowAsync(
        nameof(OrderFulfillmentWorkflow),
        request);

    return Results.Accepted($"/workflows/{instanceId}", new { instanceId });
});

app.Run();
```

Model request:

```csharp
public record CheckoutRequest(string OrderId, string ProductId, int Quantity, decimal Amount, string CustomerEmail);
```

### Workflow definition

```csharp
using Dapr.Workflow;

public class OrderFulfillmentWorkflow : Workflow<CheckoutRequest, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, CheckoutRequest input)
    {
        var reserved = await context.CallActivityAsync<bool>(nameof(ReserveInventoryActivity), input);
        if (!reserved)
        {
            return "InventoryRejected";
        }

        try
        {
            var paymentResult = await context.CallActivityAsync<bool>(nameof(ProcessPaymentActivity), input);
            if (!paymentResult)
            {
                await context.CallActivityAsync(nameof(ReleaseInventoryActivity), input);
                return "PaymentFailed";
            }

            await context.CallActivityAsync(nameof(CreateShipmentActivity), input);
            await context.CallActivityAsync(nameof(SendNotificationActivity), input);

            return "Completed";
        }
        catch
        {
            await context.CallActivityAsync(nameof(ReleaseInventoryActivity), input);
            throw;
        }
    }
}
```

### Activity implementation

```csharp
using Dapr.Client;
using Dapr.Workflow;

public class ReserveInventoryActivity : WorkflowActivity<CheckoutRequest, bool>
{
    public override Task<bool> RunAsync(WorkflowActivityContext context, CheckoutRequest input)
    {
        return Task.FromResult(true);
    }
}

public class ProcessPaymentActivity : WorkflowActivity<CheckoutRequest, bool>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ProcessPaymentActivity> _logger;

    public ProcessPaymentActivity(DaprClient daprClient, ILogger<ProcessPaymentActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<bool> RunAsync(WorkflowActivityContext context, CheckoutRequest input)
    {
        _logger.LogInformation("Charging payment for order {OrderId}", input.OrderId);

        await _daprClient.PublishEventAsync("pubsub", "payments.requested", new
        {
            input.OrderId,
            input.Amount
        });

        return true;
    }
}

public class CreateShipmentActivity : WorkflowActivity<CheckoutRequest, object>
{
    public override Task<object> RunAsync(WorkflowActivityContext context, CheckoutRequest input)
    {
        return Task.FromResult<object>(new { TrackingCode = $"SHIP-{Guid.NewGuid():N}"[..12] });
    }
}

public class ReleaseInventoryActivity : WorkflowActivity<CheckoutRequest, object>
{
    public override Task<object> RunAsync(WorkflowActivityContext context, CheckoutRequest input)
    {
        return Task.FromResult<object>(new { Released = true });
    }
}

public class SendNotificationActivity : WorkflowActivity<CheckoutRequest, object>
{
    public override Task<object> RunAsync(WorkflowActivityContext context, CheckoutRequest input)
    {
        return Task.FromResult<object>(new { Sent = true });
    }
}
```

### Timer và human interaction

Workflow còn hỗ trợ chờ đợi với timer. Ví dụ chờ khách xác nhận thanh toán trong 15 phút:

```csharp
await context.CreateTimer(TimeSpan.FromMinutes(15));
```

Hoặc chờ external event:

```csharp
var approval = await context.WaitForExternalEventAsync<bool>("ManagerApproval");
if (!approval)
{
    await context.CallActivityAsync(nameof(ReleaseInventoryActivity), input);
    return "RejectedByManager";
}
```

Đây là chỗ Dapr workflow rất hữu ích cho nghiệp vụ dài hơi, thay vì nhồi tất cả vào message consumer đơn lẻ.

---

## Resiliency policies

Distributed system mà không có retry, timeout, circuit breaker thì chỉ đang chờ lỗi production. Dapr có resiliency spec để định nghĩa policy riêng cho service invocation, pub/sub và component interactions.

Ví dụ file policy:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Resiliency
metadata:
  name: order-resiliency
scopes:
  - order-service
spec:
  policies:
    retries:
      paymentRetry:
        policy: exponential
        duration: 2s
        maxRetries: 5
        maxInterval: 30s
    timeouts:
      shortTimeout: 3s
  targets:
    apps:
      payment-service:
        retry: paymentRetry
        timeout: shortTimeout
```

Ý tưởng là chuyển một phần concern liên quan retry/timeouts ra khỏi code business. Tuy nhiên không phải cái gì cũng nên đẩy hết ra YAML. Những rule mang tính nghiệp vụ, ví dụ retry charge payment có thể tạo double charge, vẫn cần app-level design rất cẩn thận.

---

## Observability với tracing, metrics, logs

Một ưu điểm quan trọng của Dapr là nó tích hợp khá tốt với OpenTelemetry, tracing và metrics. Vì sidecar ngồi giữa nhiều lời gọi, nó có thể đóng góp trace span hữu ích cho service invocation và pub/sub.

### Logging trong app .NET

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
```

### Tracing flow

Khi client gọi `api-gateway`, gateway invoke `catalog-service`, sau đó publish event tới `order-service`, trace có thể đi xuyên qua nhiều bước nếu correlation được giữ đúng. Khi debug một order fail, trace thống nhất giúp bạn biết lỗi ở đâu nhanh hơn rất nhiều.

### Điều quan trọng trong thực chiến

- đừng log secret hoặc payload nhạy cảm chỉ vì tracing tiện
- define correlation id strategy rõ ràng
- phân biệt log business và infra log
- sidecar metrics hữu ích, nhưng không thay application metrics

Observability là lý do khiến sidecar model hấp dẫn trong môi trường microservices vừa và lớn.

---

## Kubernetes deployment với annotation Dapr

Trên Kubernetes, Dapr rất tiện vì bạn không cần tự launch `daprd` sidecar bằng tay. Chỉ cần annotate deployment:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 2
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
      annotations:
        dapr.io/enabled: "true"
        dapr.io/app-id: "order-service"
        dapr.io/app-port: "8080"
        dapr.io/app-protocol: "http"
    spec:
      containers:
        - name: order-service
          image: myregistry/order-service:1.0.0
          ports:
            - containerPort: 8080
```

Khi đó sidecar được inject tự động. App code gần như không đổi từ local self-hosted lên K8s. Đây là một selling point cực mạnh nếu team muốn local-to-cluster parity tương đối tốt.

---

## Ví dụ end-to-end: pipeline tạo đơn hàng

Hãy ráp lại một flow hoàn chỉnh:

1. Client gọi `POST /orders` vào `order-service`
2. `order-service` lưu order state = `PendingPayment`
3. `order-service` publish `orders.created`
4. `payment-worker` subscribe, xử lý charge
5. `payment-worker` publish `payments.completed`
6. `order-service` subscribe `payments.completed`, update state = `Paid`
7. `notification-worker` subscribe cùng event, gọi output binding để gửi email

Điểm đáng chú ý là từng service không cần biết toàn bộ topology hệ thống. Nó chỉ biết app id, topic name, state store name, binding name. Dapr sidecar chịu trách nhiệm kết nối phía dưới.

### Sample order endpoint hoàn chỉnh hơn

```csharp
app.MapPost("/orders", async (CreateOrderRequest request, DaprClient daprClient) =>
{
    var order = new OrderState
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        UnitPrice = 120m,
        CustomerEmail = request.CustomerEmail,
        Status = "PendingPayment",
        CreatedAtUtc = DateTime.UtcNow
    };

    await daprClient.SaveStateAsync("statestore", $"order:{order.OrderId}", order);

    await daprClient.PublishEventAsync("pubsub", "orders.created", new OrderCreatedIntegrationEvent(
        order.OrderId,
        order.ProductId,
        order.Quantity,
        order.UnitPrice,
        order.CustomerEmail));

    return Results.Accepted($"/orders/{order.OrderId}", order);
});
```

### Notification worker dùng binding

```csharp
[ApiController]
public class NotificationController : ControllerBase
{
    private readonly DaprClient _daprClient;

    public NotificationController(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    [Topic("pubsub", "payments.completed")]
    [HttpPost("notifications/order-paid")]
    public async Task<IActionResult> Handle([FromBody] JsonElement payload)
    {
        var orderId = payload.GetProperty("OrderId").GetString();

        await _daprClient.InvokeBindingAsync("notification", "post", new
        {
            to = "customer@example.com",
            subject = $"Order {orderId} has been paid",
            body = "Thank you for your purchase."
        });

        return Ok();
    }
}
```

Kiến trúc này không thần kỳ, nhưng rất sạch với một team muốn tiến từng bước sang distributed system mà không viết quá nhiều plumbing code.

---

## So sánh Dapr với MassTransit, Orleans, Aspire

### Dapr vs MassTransit

- Dapr là distributed runtime với nhiều building blocks
- MassTransit là framework messaging chuyên sâu cho .NET

Nếu pain chính của bạn là bus semantics, saga, consumer pipeline, transport tuning trong .NET, MassTransit có thể mạnh và tự nhiên hơn. Nếu pain của bạn rộng hơn, gồm invocation, state, secret, bindings, workflow, Dapr hấp dẫn hơn.

### Dapr vs Orleans

- Orleans tập trung vào virtual actor model cho stateful domain execution
- Dapr cung cấp actor nhưng đó chỉ là một phần trong bộ building blocks lớn hơn

Nếu domain xoay mạnh quanh entity stateful và bạn thuần .NET, Orleans thường sâu hơn. Nếu hệ thống đa ngôn ngữ hoặc bạn chủ yếu cần platform abstraction, Dapr hợp hơn.

### Dapr vs .NET Aspire

- Aspire không cạnh tranh trực tiếp với Dapr
- Aspire mạnh ở local orchestration, service discovery, telemetry, developer experience cho cloud-native .NET
- Dapr mạnh ở runtime building blocks

Thực tế, chúng có thể đi cùng nhau. Aspire chạy các service và resource cục bộ, Dapr cung cấp invocation/pubsub/state/workflow.

---

## Những lỗi tư duy phổ biến khi dùng Dapr

### 1. Nghĩ rằng có Dapr thì không cần hiểu distributed system

Sai. Dapr giảm boilerplate nhưng không xóa các vấn đề như consistency, duplication, ordering, retries, backpressure, partial failure.

### 2. Dùng mọi building block chỉ vì nó tồn tại

Không phải app nào cũng cần actor, workflow, bindings, pub/sub, config subscription cùng lúc. Hãy dùng phần thực sự giải pain.

### 3. Quá phụ thuộc abstraction, quên mất backend thật

Redis state store khác Cosmos state store về consistency, latency, TTL, transaction support. Pub/sub Redis khác Kafka hay Service Bus rất nhiều. Đừng giả vờ chúng giống hệt nhau chỉ vì code .NET giống nhau.

### 4. Không định nghĩa naming convention

Tên app id, topic, state key, binding name nếu không có convention rõ ràng sẽ rất nhanh rối.

Ví dụ tốt:

- app id: `order-service`, `catalog-service`
- topic: `orders.created`, `payments.completed`
- state key: `order:{id}`, `msg:{cloudEventId}`

### 5. Không thiết kế idempotency

Đây là lỗi cực phổ biến. Event-driven code mà không idempotent thì production sẽ đau.

---

## Best practices thực chiến

### 1. Bắt đầu từ 2-3 building blocks, không ôm cả vũ trụ

Một lộ trình hợp lý:

- bước 1: service invocation + pub/sub
- bước 2: state store
- bước 3: secrets/config
- bước 4: workflow nếu có long-running process
- bước 5: actors nếu domain thật sự cần

### 2. Thiết kế hợp đồng sự kiện cẩn thận

Dùng record rõ ràng, version payload khi cần, tránh publish object internal tuỳ tiện.

```csharp
public record OrderCreatedV1(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal UnitPrice,
    string CustomerEmail,
    DateTime CreatedAtUtc);
```

### 3. Tách business model và transport model

Đừng để DTO publish event trùng hoàn toàn với entity nội bộ nếu entity thay đổi thường xuyên.

### 4. Đo độ trễ sidecar và backend

Nếu request chậm, có thể lỗi không nằm ở sidecar mà nằm ở state backend hoặc broker. Instrument đủ sâu.

### 5. Test cả happy path lẫn failure path

- service downstream down
- broker unavailable
- duplicate event
- state conflict
- secret missing
- binding trả lỗi 500

### 6. Có fallback cho feature quan trọng

Ví dụ nếu binding gửi email fail, cần retry hoặc queue bù. Không nên coi `InvokeBindingAsync` là xong chuyện.

---

## Bộ mã mẫu tối giản nhưng thực tế hơn

### Shared contracts

```csharp
public record CreateOrderRequest(string ProductId, int Quantity, string CustomerEmail);
public record OrderCreatedIntegrationEvent(string OrderId, string ProductId, int Quantity, decimal UnitPrice, string CustomerEmail);
public record PaymentCompletedIntegrationEvent(string OrderId, string PaymentReference, DateTime PaidAtUtc);
```

### Order service handlers

```csharp
app.MapPost("/orders", async (CreateOrderRequest request, DaprClient daprClient, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Orders");

    var order = new OrderState
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        UnitPrice = 120m,
        CustomerEmail = request.CustomerEmail,
        Status = "PendingPayment",
        CreatedAtUtc = DateTime.UtcNow
    };

    await daprClient.SaveStateAsync("statestore", $"order:{order.OrderId}", order);
    logger.LogInformation("Saved order {OrderId}", order.OrderId);

    await daprClient.PublishEventAsync("pubsub", "orders.created", new OrderCreatedIntegrationEvent(
        order.OrderId,
        order.ProductId,
        order.Quantity,
        order.UnitPrice,
        order.CustomerEmail));

    logger.LogInformation("Published orders.created for {OrderId}", order.OrderId);

    return Results.Accepted($"/orders/{order.OrderId}", new { order.OrderId, order.Status });
});
```

### Payment worker

```csharp
[Topic("pubsub", "orders.created")]
[HttpPost("payments/process")]
public async Task<IActionResult> Process(OrderCreatedIntegrationEvent evt)
{
    await Task.Delay(250);

    await _daprClient.PublishEventAsync("pubsub", "payments.completed", new PaymentCompletedIntegrationEvent(
        evt.OrderId,
        $"PAY-{Guid.NewGuid():N}"[..10],
        DateTime.UtcNow));

    return Ok();
}
```

### Order service payment subscriber

```csharp
[Topic("pubsub", "payments.completed")]
[HttpPost("orders/payment-completed")]
public async Task<IActionResult> Complete(PaymentCompletedIntegrationEvent evt)
{
    var order = await _daprClient.GetStateAsync<OrderState>("statestore", $"order:{evt.OrderId}");
    if (order is null) return NotFound();

    order.Status = "Paid";
    await _daprClient.SaveStateAsync("statestore", $"order:{evt.OrderId}", order);

    return Ok(order);
}
```

Đây là skeleton đủ để bạn cảm nhận Dapr giúp wiring giữa các service nhẹ hơn như thế nào.

---

## Kết luận

Dapr là một runtime rất đáng học cho .NET developer đang bước vào distributed application. Điểm mạnh thật sự của nó không nằm ở một tính năng đơn lẻ, mà ở việc nó gom nhiều distributed building blocks vào một mô hình sidecar nhất quán: service invocation, pub/sub, state store, bindings, secrets, actors, workflow, resiliency và observability.

Nếu bạn dùng Dapr đúng chỗ, bạn sẽ có một kiến trúc gọn hơn ở tầng integration, ít boilerplate hơn, local dev dễ hơn, khả năng đổi backend infrastructure bớt đau hơn, và code business tập trung hơn vào nghiệp vụ. Nếu bạn dùng Dapr sai chỗ, hoặc lạm dụng abstraction mà không hiểu backend thật, nó sẽ chỉ biến thành một lớp complexity mới.

Cách học tốt nhất là không cố nuốt cả bộ API một lúc. Hãy bắt đầu bằng một ứng dụng nhỏ có 2 service, chạy service invocation + pub/sub + state store. Khi đã quen flow và sidecar mental model, bạn thêm bindings, secret store, rồi workflow. Đến lúc đó bạn sẽ hiểu vì sao Dapr được xem là một trong những công cụ thực dụng nhất cho distributed app hiện đại.

Nếu phải nhớ một câu sau bài này, tôi sẽ chọn câu sau: Dapr không thay bạn thiết kế hệ thống, nhưng nó cho bạn một bộ gạch rất tốt để xây hệ thống phân tán theo cách sạch hơn, nhất quán hơn, và ít phải lặp lại plumbing hơn.
