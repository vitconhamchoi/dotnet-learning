# Orleans chuyên sâu, xây dựng ứng dụng distributed bằng Virtual Actor trong .NET

## 1. Orleans là gì, vì sao nó khác với microservice truyền thống

Orleans là framework distributed application của .NET dựa trên mô hình **Virtual Actor**. Ý tưởng cốt lõi là thay vì bạn tự quản lý instance service, queue, sharding, sticky session, cache và state placement, bạn chỉ mô tả các actor logic gọi là **grain**, sau đó runtime Orleans tự lo activation, placement, state persistence, reminder, stream, scale-out và failover.

Nếu bạn từng xây một hệ thống game server, notification engine, workflow engine, IoT ingestion, order orchestration hoặc social feed, bạn sẽ thấy rất nhiều logic thực ra xoay quanh một thực thể có danh tính rõ ràng như user, cart, order, device, room, inventory item. Với microservice kiểu truyền thống, bạn thường phải giải bài toán:

- route request đến đúng node đang giữ trạng thái
- đồng bộ cache hoặc session giữa nhiều instance
- khóa cạnh tranh khi cùng update một entity
- retry khi message đến sai thứ tự
- quét cron job để xử lý timeout hoặc schedule
- partition dữ liệu để scale ngang

Orleans giải bài toán đó bằng cách cho mỗi entity logic một định danh. Bạn gọi `GetGrain<IOrderGrain>(orderId)` và runtime đảm bảo tại một thời điểm chỉ có một activation chính xử lý luồng gọi cho grain đó. Điều này làm đơn giản hóa concurrency, rất hợp với bài toán stateful distributed domain.

Điểm khác biệt quan trọng:

1. **Không cần tự quản lý actor lifecycle**. Grain được kích hoạt khi có request, có thể bị deactivate khi idle.
2. **Single-threaded execution per activation**. Bên trong một grain, code nhìn gần giống code tuần tự, giảm race condition.
3. **Vị trí của grain là ảo**. Client không quan tâm grain đang ở node nào.
4. **State là tùy chọn nhưng dễ dùng**. Có thể lưu vào memory, ADO.NET, Redis, Azure Table, Cosmos DB, custom storage.
5. **Tích hợp timer, reminder, stream**. Rất hợp với workflow, reactive processing, scheduled tasks.

Orleans không thay thế mọi microservice. Nó đặc biệt mạnh khi:

- domain có nhiều entity stateful
- concurrency quanh từng entity phức tạp
- cần fan-out hoặc orchestrate theo entity key
- cần online processing độ trễ thấp
- muốn scale ngang nhưng giữ mô hình lập trình đơn giản

Ngược lại, nếu bạn chỉ cần CRUD stateless API rất mỏng, hoặc workload chủ yếu là batch analytics, Orleans có thể là overkill.

---

## 2. Mental model, các khái niệm cốt lõi

### 2.1 Silo, Cluster, Client, Grain

- **Silo**: tiến trình host Orleans runtime, tương tự một node trong cluster.
- **Cluster**: nhóm nhiều silo cùng phục vụ một namespace logic.
- **Client**: ứng dụng bên ngoài cluster gọi vào grains, ví dụ ASP.NET Core API.
- **Grain**: đơn vị tính toán logic có identity.

Hãy hình dung một hệ thống thương mại điện tử:

- `CartGrain` quản lý giỏ hàng theo `userId`
- `InventoryGrain` quản lý tồn kho theo `sku`
- `OrderGrain` quản lý vòng đời đơn hàng theo `orderId`
- `PaymentSessionGrain` quản lý trạng thái phiên thanh toán

Thay vì API gọi DB trực tiếp và tự xử lý locking, API có thể gọi grain tương ứng.

### 2.2 Virtual Actor

“Virtual” nghĩa là actor luôn được xem là tồn tại về mặt logic, nhưng runtime chỉ tạo activation vật lý khi cần. Bạn không new object, không deploy actor cụ thể, không giữ registry actor thủ công.

```csharp
var cart = grainFactory.GetGrain<ICartGrain>(userId);
await cart.AddItemAsync("SKU-01", 2);
```

Nếu grain chưa active, Orleans tự activate. Nếu grain đang active ở node khác, call được route tới đúng nơi. Nếu node chết, grain có thể được activate lại ở node khác.

### 2.3 Grain identity và grain key

Identity phải ổn định và mang ý nghĩa domain. Các key thường gặp:

- `Guid`
- `long`
- `string`
- compound key thông qua string conventions

Ví dụ:

```csharp
public interface IOrderGrain : IGrainWithGuidKey
{
    Task SubmitAsync(SubmitOrderRequest request);
    Task<OrderStateDto> GetStateAsync();
}

public interface IUserNotificationGrain : IGrainWithStringKey
{
    Task EnqueueAsync(NotificationCommand command);
}
```

Chọn key đúng giúp scale và reasoning tốt hơn. `orderId` cho order, `userId` cho cart, `sku` cho inventory là lựa chọn tự nhiên.

### 2.4 Single-threaded turn-based execution

Mỗi activation grain xử lý request theo kiểu turn-based. Điều này có nghĩa code bên trong grain gần như không cần lock thủ công cho state nội bộ.

```csharp
public class CounterGrain : Grain, ICounterGrain
{
    private int _value;

    public Task IncrementAsync()
    {
        _value++;
        return Task.CompletedTask;
    }

    public Task<int> GetValueAsync() => Task.FromResult(_value);
}
```

Trong service thông thường, bạn có thể lo `Interlocked` hay lock. Trong Orleans, cùng activation này sẽ không chạy song song theo cách phá state nội bộ, trừ khi bạn cố ý dùng reentrancy hoặc gọi ra ngoài theo pattern tạo interleaving.

### 2.5 Grain state, persistence provider

Grain có thể stateless hoặc stateful. Với stateful grain, bạn thường inject `IPersistentState<T>` hoặc dùng storage abstraction.

Ví dụ state giỏ hàng:

```csharp
[GenerateSerializer]
public class CartState
{
    [Id(0)] public Dictionary<string, int> Items { get; set; } = new();
    [Id(1)] public DateTime LastUpdatedUtc { get; set; }
}
```

```csharp
public interface ICartGrain : IGrainWithStringKey
{
    Task AddItemAsync(string sku, int quantity);
    Task RemoveItemAsync(string sku, int quantity);
    Task<CartDto> GetAsync();
    Task CheckoutAsync();
}
```

```csharp
public class CartGrain : Grain, ICartGrain
{
    private readonly IPersistentState<CartState> _state;

    public CartGrain(
        [PersistentState("cart", "cartStore")]
        IPersistentState<CartState> state)
    {
        _state = state;
    }

    public async Task AddItemAsync(string sku, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        if (_state.State.Items.TryGetValue(sku, out var current))
            _state.State.Items[sku] = current + quantity;
        else
            _state.State.Items[sku] = quantity;

        _state.State.LastUpdatedUtc = DateTime.UtcNow;
        await _state.WriteStateAsync();
    }

    public async Task RemoveItemAsync(string sku, int quantity)
    {
        if (!_state.State.Items.TryGetValue(sku, out var current))
            return;

        var next = current - quantity;
        if (next <= 0) _state.State.Items.Remove(sku);
        else _state.State.Items[sku] = next;

        _state.State.LastUpdatedUtc = DateTime.UtcNow;
        await _state.WriteStateAsync();
    }

    public Task<CartDto> GetAsync()
        => Task.FromResult(new CartDto(
            this.GetPrimaryKeyString(),
            _state.State.Items.Select(x => new CartItemDto(x.Key, x.Value)).ToList(),
            _state.State.LastUpdatedUtc));

    public Task CheckoutAsync()
    {
        // thực tế sẽ gọi order grain, inventory grain, payment workflow...
        return Task.CompletedTask;
    }
}
```

---

## 3. Kiến trúc một ứng dụng Orleans thực chiến

Một cấu trúc repo điển hình:

```text
src/
  Shop.Api/
  Shop.GrainInterfaces/
  Shop.Grains/
  Shop.Contracts/
  Shop.Infrastructure/
```

- `Shop.GrainInterfaces`: interface của grains
- `Shop.Grains`: implementation
- `Shop.Contracts`: DTO, command, serializer types
- `Shop.Api`: ASP.NET Core host vừa là Orleans silo vừa expose HTTP API, hoặc tách client host riêng
- `Shop.Infrastructure`: cấu hình DB, observability, custom storage

Có 2 kiểu triển khai phổ biến:

1. **Silo + HTTP API cùng process**: đơn giản cho hệ vừa và nhỏ.
2. **API là client, Silo cluster chạy riêng**: phù hợp scale độc lập và phân lớp hạ tầng.

### 3.1 Mẫu host tối thiểu với ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();

    silo.AddMemoryGrainStorage("cartStore");
    silo.AddMemoryGrainStorageAsDefault();

    silo.AddStreams("sms", streams => streams.AddMemoryStreams());

    silo.UseDashboard(options =>
    {
        options.HostSelf = true;
        options.Port = 8081;
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapPost("/carts/{userId}/items", async (
    string userId,
    AddCartItemHttpRequest request,
    IGrainFactory grains) =>
{
    var cart = grains.GetGrain<ICartGrain>(userId);
    await cart.AddItemAsync(request.Sku, request.Quantity);
    return Results.Accepted();
});

app.MapGet("/carts/{userId}", async (string userId, IGrainFactory grains) =>
{
    var cart = grains.GetGrain<ICartGrain>(userId);
    return Results.Ok(await cart.GetAsync());
});

app.Run();

public record AddCartItemHttpRequest(string Sku, int Quantity);
```

Khi cần production, bạn sẽ thay `UseLocalhostClustering()` bằng cấu hình membership thật như ADO.NET, Kubernetes, Azure Storage hoặc Consul tùy stack.

---

## 4. Xây demo, order workflow với nhiều grains phối hợp

Chúng ta thử thiết kế một flow đặt hàng:

1. User thêm item vào cart
2. Cart checkout tạo order
3. Order reserve inventory
4. Order mở payment session
5. Khi thanh toán thành công, order chuyển sang `Paid`
6. Nếu timeout, order tự hủy bằng reminder

### 4.1 Contracts

```csharp
public record CartItemDto(string Sku, int Quantity);
public record CartDto(string UserId, List<CartItemDto> Items, DateTime LastUpdatedUtc);

public record SubmitOrderRequest(string UserId, List<CartItemDto> Items);
public record OrderStateDto(Guid OrderId, string UserId, string Status, List<CartItemDto> Items, decimal Total);
```

### 4.2 Inventory grain

```csharp
public interface IInventoryGrain : IGrainWithStringKey
{
    Task SeedAsync(int quantity);
    Task<bool> ReserveAsync(Guid orderId, int quantity);
    Task ReleaseAsync(Guid orderId);
    Task<int> GetAvailableAsync();
}
```

```csharp
[GenerateSerializer]
public class InventoryState
{
    [Id(0)] public int Available { get; set; }
    [Id(1)] public Dictionary<Guid, int> Reservations { get; set; } = new();
}
```

```csharp
public class InventoryGrain : Grain, IInventoryGrain
{
    private readonly IPersistentState<InventoryState> _state;

    public InventoryGrain(
        [PersistentState("inventory", "Default")]
        IPersistentState<InventoryState> state)
    {
        _state = state;
    }

    public async Task SeedAsync(int quantity)
    {
        _state.State.Available = quantity;
        await _state.WriteStateAsync();
    }

    public async Task<bool> ReserveAsync(Guid orderId, int quantity)
    {
        if (_state.State.Available < quantity)
            return false;

        _state.State.Available -= quantity;
        _state.State.Reservations[orderId] = quantity;
        await _state.WriteStateAsync();
        return true;
    }

    public async Task ReleaseAsync(Guid orderId)
    {
        if (_state.State.Reservations.Remove(orderId, out var qty))
        {
            _state.State.Available += qty;
            await _state.WriteStateAsync();
        }
    }

    public Task<int> GetAvailableAsync() => Task.FromResult(_state.State.Available);
}
```

### 4.3 Order grain

```csharp
public interface IOrderGrain : IGrainWithGuidKey
{
    Task SubmitAsync(SubmitOrderRequest request);
    Task MarkPaymentSucceededAsync(string transactionId);
    Task CancelAsync(string reason);
    Task<OrderStateDto> GetStateAsync();
}
```

```csharp
[GenerateSerializer]
public class OrderState
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<CartItemDto> Items { get; set; } = new();
    [Id(2)] public string Status { get; set; } = "Draft";
    [Id(3)] public decimal Total { get; set; }
    [Id(4)] public string? PaymentTransactionId { get; set; }
    [Id(5)] public DateTime CreatedUtc { get; set; }
}
```

```csharp
public class OrderGrain : Grain, IOrderGrain, IRemindable
{
    private readonly IPersistentState<OrderState> _state;
    private const string PaymentTimeoutReminder = "payment-timeout";

    public OrderGrain(
        [PersistentState("order", "Default")]
        IPersistentState<OrderState> state)
    {
        _state = state;
    }

    public async Task SubmitAsync(SubmitOrderRequest request)
    {
        if (_state.State.Status is not "Draft")
            throw new InvalidOperationException("Order already initialized");

        _state.State.UserId = request.UserId;
        _state.State.Items = request.Items;
        _state.State.Total = request.Items.Sum(x => x.Quantity * 10m);
        _state.State.Status = "PendingPayment";
        _state.State.CreatedUtc = DateTime.UtcNow;

        foreach (var item in request.Items)
        {
            var inventory = GrainFactory.GetGrain<IInventoryGrain>(item.Sku);
            var reserved = await inventory.ReserveAsync(this.GetPrimaryKey(), item.Quantity);
            if (!reserved)
            {
                await RollbackReservationsAsync();
                _state.State.Status = "Rejected";
                await _state.WriteStateAsync();
                return;
            }
        }

        await _state.WriteStateAsync();

        await RegisterOrUpdateReminder(
            PaymentTimeoutReminder,
            dueTime: TimeSpan.FromMinutes(15),
            period: TimeSpan.FromDays(365));
    }

    public async Task MarkPaymentSucceededAsync(string transactionId)
    {
        if (_state.State.Status != "PendingPayment")
            return;

        _state.State.Status = "Paid";
        _state.State.PaymentTransactionId = transactionId;
        await _state.WriteStateAsync();

        var reminder = await GetReminder(PaymentTimeoutReminder);
        if (reminder is not null)
            await UnregisterReminder(reminder);
    }

    public async Task CancelAsync(string reason)
    {
        if (_state.State.Status is "Paid" or "Cancelled")
            return;

        await RollbackReservationsAsync();
        _state.State.Status = $"Cancelled:{reason}";
        await _state.WriteStateAsync();
    }

    public Task<OrderStateDto> GetStateAsync()
        => Task.FromResult(new OrderStateDto(
            this.GetPrimaryKey(),
            _state.State.UserId,
            _state.State.Status,
            _state.State.Items,
            _state.State.Total));

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == PaymentTimeoutReminder && _state.State.Status == "PendingPayment")
            await CancelAsync("payment-timeout");
    }

    private async Task RollbackReservationsAsync()
    {
        foreach (var item in _state.State.Items)
        {
            var inventory = GrainFactory.GetGrain<IInventoryGrain>(item.Sku);
            await inventory.ReleaseAsync(this.GetPrimaryKey());
        }
    }
}
```

Ở đây có vài điểm rất Orleans:

- `OrderGrain` giữ state vòng đời đơn hàng
- reserve inventory theo từng `sku`, mỗi `InventoryGrain` là một aggregate stateful
- reminder giúp timeout bền vững hơn timer trong bộ nhớ
- logic domain nằm gần entity thay vì rải khắp handler, cron, cache, DB transaction scripts

### 4.4 Cart checkout gọi sang Order grain

```csharp
public async Task CheckoutAsync()
{
    if (_state.State.Items.Count == 0)
        throw new InvalidOperationException("Cart is empty");

    var orderId = Guid.NewGuid();
    var order = GrainFactory.GetGrain<IOrderGrain>(orderId);

    await order.SubmitAsync(new SubmitOrderRequest(
        this.GetPrimaryKeyString(),
        _state.State.Items.Select(x => new CartItemDto(x.Key, x.Value)).ToList()));

    _state.State.Items.Clear();
    _state.State.LastUpdatedUtc = DateTime.UtcNow;
    await _state.WriteStateAsync();
}
```

---

## 5. Persistent state, storage providers, và chiến lược dữ liệu

Orleans không ép bạn dùng một loại DB duy nhất. Đây là lợi thế lớn. Bạn có thể kết hợp:

- grain state trong PostgreSQL hoặc SQL Server qua ADO.NET storage
- event log riêng trong Kafka hoặc EventStore
- read model ở Elasticsearch
- cache ngoài ở Redis

### 5.1 Cấu hình ADO.NET storage

```csharp
builder.Host.UseOrleans(silo =>
{
    silo.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = builder.Configuration.GetConnectionString("Orleans");
    });

    silo.AddAdoNetGrainStorage("Default", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = builder.Configuration.GetConnectionString("Orleans");
        options.UseJsonFormat = true;
    });

    silo.AddAdoNetReminderService(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = builder.Configuration.GetConnectionString("Orleans");
    });
});
```

Thực tế, bạn cần chạy script schema tương ứng cho provider. Orleans cung cấp script mẫu trong package và docs.

### 5.2 Khi nào mỗi grain nên giữ bao nhiêu state

Nguyên tắc hữu ích:

- state đủ để xử lý hành vi của entity nhanh chóng
- tránh nhồi object graph quá lớn vào một grain
- tách grain nếu access pattern khác nhau nhiều
- dùng read model ngoài nếu cần query phức tạp, filter, search, analytics

Ví dụ xấu:

- một `UserGrain` ôm toàn bộ profile, settings, followers, feed cache, shopping cart, device sessions

Ví dụ tốt hơn:

- `UserProfileGrain`
- `UserSettingsGrain`
- `UserFollowersGrain`
- `UserFeedProjection` ở DB query riêng
- `CartGrain`

Orleans mạnh ở hành vi theo identity, không phải query ad-hoc trên hàng triệu bản ghi.

---

## 6. Timers và reminders, đừng nhầm hai khái niệm này

Đây là điểm nhiều team mới dùng Orleans hay nhầm.

### 6.1 Timer

- sống theo activation
- mất nếu activation bị deactivate hoặc silo restart
- hợp với tác vụ ngắn hạn, in-memory

```csharp
private IDisposable? _timer;

public override Task OnActivateAsync(CancellationToken cancellationToken)
{
    _timer = RegisterTimer(
        callback: _ => FlushBufferAsync(),
        state: null,
        dueTime: TimeSpan.FromSeconds(10),
        period: TimeSpan.FromSeconds(10));

    return Task.CompletedTask;
}
```

### 6.2 Reminder

- bền vững hơn timer
- được lưu qua reminder storage
- sau restart vẫn có thể tiếp tục trigger
- hợp với payment timeout, subscription renewal, SLA deadline, delayed workflow

```csharp
await RegisterOrUpdateReminder(
    reminderName: "renew-subscription",
    dueTime: TimeSpan.FromHours(24),
    period: TimeSpan.FromDays(30));
```

Chọn đúng rất quan trọng. Nếu bạn cần “nhắc lại kể cả sau failover”, dùng reminder.

---

## 7. Orleans Streams, xử lý sự kiện reactive trong cluster

Streams cho phép grain publish/subscribe message theo kiểu reactive. Nó hữu ích khi bạn muốn:

- fan-out event đến nhiều grain
- build workflow không coupling chặt
- cập nhật projection, notification, analytics gần real-time

### 7.1 Khai báo provider

```csharp
builder.Host.UseOrleans(silo =>
{
    silo.AddMemoryStreams("order-stream");
});
```

### 7.2 Publish event

```csharp
public record OrderPaidEvent(Guid OrderId, string UserId, decimal Total);
```

```csharp
public async Task MarkPaymentSucceededAsync(string transactionId)
{
    if (_state.State.Status != "PendingPayment")
        return;

    _state.State.Status = "Paid";
    _state.State.PaymentTransactionId = transactionId;
    await _state.WriteStateAsync();

    var stream = this.GetStreamProvider("order-stream")
        .GetStream<OrderPaidEvent>(StreamId.Create("orders", this.GetPrimaryKey()));

    await stream.OnNextAsync(new OrderPaidEvent(
        this.GetPrimaryKey(),
        _state.State.UserId,
        _state.State.Total));
}
```

### 7.3 Subscribe từ notification grain hoặc projection grain

```csharp
public interface IOrderProjectionGrain : IGrainWithGuidKey
{
    Task EnsureSubscribedAsync();
}
```

```csharp
public class OrderProjectionGrain : Grain, IOrderProjectionGrain, IAsyncObserver<OrderPaidEvent>
{
    private StreamSubscriptionHandle<OrderPaidEvent>? _handle;

    public async Task EnsureSubscribedAsync()
    {
        if (_handle is not null) return;

        var stream = this.GetStreamProvider("order-stream")
            .GetStream<OrderPaidEvent>(StreamId.Create("orders", this.GetPrimaryKey()));

        _handle = await stream.SubscribeAsync(this);
    }

    public Task OnNextAsync(OrderPaidEvent item, StreamSequenceToken? token = null)
    {
        // cập nhật projection, gửi email, publish integration event...
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync() => Task.CompletedTask;
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
}
```

Trong production, bạn thường dùng stream provider bền hơn hoặc kết nối broker ngoài để phục vụ integration boundaries. Streams nội bộ Orleans rất tiện cho decoupling logic trong cluster.

---

## 8. Reentrancy, interleaving và deadlock avoidance

Mặc định, grain giúp bạn tránh concurrency phức tạp. Nhưng khi bắt đầu có call chéo giữa grains, bạn cần hiểu luồng gọi để tránh deadlock hoặc latency xấu.

Ví dụ nguy hiểm:

- `UserGrain` gọi `RoomGrain.JoinAsync(userId)`
- trong `RoomGrain.JoinAsync`, lại gọi `UserGrain.SetCurrentRoom(roomId)`

Nếu không thiết kế khéo, bạn tạo vòng chờ logic. Orleans có cơ chế scheduling nội bộ tốt, nhưng bạn vẫn nên thiết kế aggregate boundaries rõ ràng.

Khuyến nghị:

1. Tránh chain call sâu giữa nhiều grain khi có thể.
2. Dùng event hoặc stream cho side effect.
3. Chỉ bật `[Reentrant]` hoặc interleave khi thực sự hiểu hệ quả.
4. Tách read-heavy logic ra projection/query store thay vì hỏi chéo nhiều grain.

Ví dụ grain reentrant:

```csharp
[Reentrant]
public class ChatRoomGrain : Grain, IChatRoomGrain
{
    // dùng khi grain cần nhận nhiều call không chặn lẫn nhau theo pattern phù hợp
}
```

Đừng thêm `[Reentrant]` chỉ để “cho nhanh”. Nó làm mô hình đơn giản của Orleans bớt an toàn hơn.

---

## 9. Stateless worker grain, khi nào dùng

Không phải grain nào cũng cần persistent state hoặc identity business mạnh. Orleans có **stateless worker** cho workload song song kiểu CPU-bound hoặc transform ngắn.

Ví dụ resize ảnh hoặc tính toán quote:

```csharp
[StatelessWorker]
public class PricingGrain : Grain, IPricingGrain
{
    public Task<decimal> CalculateAsync(List<CartItemDto> items)
    {
        var total = items.Sum(x => x.Quantity * 10m);
        return Task.FromResult(total);
    }
}
```

Stateless worker có thể tạo nhiều activation để tăng throughput. Đây là pattern tốt cho logic không cần affinity theo key.

---

## 10. Gọi từ ASP.NET Core, background service và client ngoài cluster

### 10.1 ASP.NET Core chạy cùng silo

Đây là cách bắt đầu dễ nhất, như ví dụ ở trên.

### 10.2 Client riêng

Bạn có thể để API chỉ là Orleans client:

```csharp
builder.Services.AddOrleansClient(client =>
{
    client.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = builder.Configuration.GetConnectionString("Orleans");
    });
});
```

Sau đó dùng `IClusterClient`:

```csharp
app.MapGet("/orders/{orderId:guid}", async (Guid orderId, IClusterClient client) =>
{
    var order = client.GetGrain<IOrderGrain>(orderId);
    return Results.Ok(await order.GetStateAsync());
});
```

Tách client và silo giúp bạn scale HTTP independently, useful nếu traffic API lớn nhưng logic stateful nằm ở cluster.

---

## 11. Serialization trong Orleans

Orleans dùng source generation serializer rất nhanh. Bạn thường thấy attribute:

- `[GenerateSerializer]`
- `[Id(n)]`

Ví dụ:

```csharp
[GenerateSerializer]
public class PaymentResult
{
    [Id(0)] public bool Succeeded { get; set; }
    [Id(1)] public string? TransactionId { get; set; }
    [Id(2)] public string? ErrorCode { get; set; }
}
```

Lưu ý:

- gán `Id` ổn định khi versioning
- không thay đổi tùy tiện thứ tự semantic của field
- ưu tiên DTO rõ ràng thay vì truyền object EF hoặc object quá phức tạp

Versioning dữ liệu là phần cần kỷ luật. Khi evolve state schema, hãy nghĩ đến tương thích ngược khi đọc state cũ.

---

## 12. Testing Orleans

### 12.1 Unit test logic thuần

Với logic domain phức tạp, hãy tách phần thuần thành service hoặc method nội bộ để test độc lập.

### 12.2 Integration test với TestCluster

```csharp
public class CartGrainTests : IDisposable
{
    private readonly TestCluster _cluster;

    public CartGrainTests()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        _cluster.Deploy();
    }

    [Fact]
    public async Task AddItem_ShouldPersistItem()
    {
        var grain = _cluster.GrainFactory.GetGrain<ICartGrain>("user-1");
        await grain.AddItemAsync("SKU-1", 2);

        var cart = await grain.GetAsync();
        Assert.Single(cart.Items);
        Assert.Equal(2, cart.Items[0].Quantity);
    }

    public void Dispose() => _cluster.StopAllSilos();
}

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddMemoryGrainStorageAsDefault();
        siloBuilder.AddMemoryGrainStorage("cartStore");
    }
}
```

Integration test cực kỳ quan trọng vì nhiều giá trị của Orleans nằm ở runtime behavior chứ không chỉ code C# thuần.

---

## 13. Observability, logging, metrics, tracing

Một cluster Orleans production cần quan sát được:

- số activation theo grain type
- request latency theo grain method
- storage latency và lỗi ghi state
- message queue length nội bộ
- reminder execution failures
- dead silo, membership churn

Khuyến nghị:

1. Bật OpenTelemetry cho logs, metrics, traces.
2. Gắn correlation ID từ HTTP vào grain call nếu cần trace end-to-end.
3. Dùng dashboard hoặc metrics exporter để xem hot grains.
4. Thiết kế log có grain key nhưng tránh lộ PII.

Ví dụ logging trong grain:

```csharp
public class PaymentSessionGrain : Grain, IPaymentSessionGrain
{
    private readonly ILogger<PaymentSessionGrain> _logger;

    public PaymentSessionGrain(ILogger<PaymentSessionGrain> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(Guid orderId)
    {
        _logger.LogInformation("Starting payment session for order {OrderId}", orderId);
        return Task.CompletedTask;
    }
}
```

---

## 14. Triển khai production, cluster membership và failover

Các câu hỏi production điển hình:

- Silo membership lưu ở đâu?
- Grain state lưu ở đâu?
- Reminder service lưu ở đâu?
- Khi scale node, placement ra sao?
- Khi node chết, activation hồi phục thế nào?

Một setup phổ biến trên Kubernetes + PostgreSQL:

- Orleans silos chạy thành nhiều pod
- Membership dùng ADO.NET clustering với PostgreSQL
- Grain state dùng ADO.NET storage hoặc Redis tùy use case
- Reminder dùng ADO.NET reminder service
- API gateway chạy riêng hoặc cùng silo

Các nguyên tắc triển khai:

1. **Idempotency cho side effect**. Grain có thể retry hoặc recover sau failover.
2. **State write không phải distributed transaction toàn cục**. Thiết kế theo eventual consistency.
3. **Đừng coi grain memory là source of truth duy nhất** nếu state cần durability.
4. **Phân tách integration event với internal grain call** cho rõ boundary.

---

## 15. Pattern thường dùng với Orleans

### 15.1 Entity actor pattern

Mỗi aggregate root là một grain. Ví dụ `AccountGrain`, `CartGrain`, `DeviceGrain`.

### 15.2 Coordinator grain

Một grain điều phối workflow theo business key, ví dụ `OrderGrain`, `FulfillmentGrain`.

### 15.3 Session grain

Rất hợp với payment session, websocket session, multiplayer match session.

### 15.4 Saga-lite bằng grain state + reminder

Nếu workflow không cần broker ngoài, bạn có thể dùng grain như một saga coordinator nhẹ.

### 15.5 Per-tenant grain boundary

Hữu ích cho multi-tenant config cache hoặc quota control:

```csharp
public interface ITenantQuotaGrain : IGrainWithStringKey
{
    Task<bool> TryConsumeAsync(int units);
}
```

---

## 16. Khi nào không nên dùng Orleans

Đây là phần thực tế nhất.

Không nên chọn Orleans nếu:

- team chỉ cần CRUD API + SQL rất đơn giản
- domain không có entity stateful đáng kể
- workload chủ yếu batch ETL hoặc data lake
- team chưa sẵn sàng vận hành distributed runtime
- yêu cầu query linh hoạt mạnh hơn behavior theo entity

Nói ngắn gọn, Orleans là công cụ cực mạnh cho đúng bài toán. Dùng sai bài toán thì chi phí nhận thức cao hơn lợi ích.

---

## 17. So sánh nhanh Orleans với các lựa chọn khác

### So với ASP.NET Core + EF Core thuần

- EF Core thuần hợp CRUD và transaction DB-centric
- Orleans hợp domain stateful online, concurrency theo entity, workflow dài hơi

### So với Redis + background worker tự làm

- Tự làm cho bạn tự do hơn nhưng phải tự gánh placement, retry, recovery, concurrency
- Orleans cung cấp runtime model nhất quán hơn

### So với Akka.NET

- Cùng họ actor model, nhưng Orleans thiên về virtual actor, developer experience trong .NET business apps và persistence integration thuận tiện hơn
- Akka.NET mạnh ở actor systems truyền thống, stream, topology explicit

### So với Dapr workflow/service invocation

- Dapr thiên về platform building blocks
- Orleans thiên về programming model stateful theo entity
- Nhiều hệ thống thực tế có thể dùng cả hai cho các lớp khác nhau

---

## 18. Một ví dụ API hoàn chỉnh hơn

```csharp
app.MapPost("/orders/{orderId:guid}/pay", async (
    Guid orderId,
    PayOrderHttpRequest request,
    IGrainFactory grains) =>
{
    var order = grains.GetGrain<IOrderGrain>(orderId);
    await order.MarkPaymentSucceededAsync(request.TransactionId);
    return Results.Ok(await order.GetStateAsync());
});

app.MapDelete("/orders/{orderId:guid}", async (Guid orderId, IGrainFactory grains) =>
{
    var order = grains.GetGrain<IOrderGrain>(orderId);
    await order.CancelAsync("manual-cancel");
    return Results.NoContent();
});

public record PayOrderHttpRequest(string TransactionId);
```

Một flow test thủ công:

1. `POST /carts/u1/items` thêm 2 sản phẩm
2. `GET /carts/u1` xem cart
3. `POST /carts/u1/checkout`
4. `GET /orders/{id}` xem trạng thái `PendingPayment`
5. `POST /orders/{id}/pay`
6. `GET /orders/{id}` thấy `Paid`

---

## 19. Những lỗi thiết kế người mới hay gặp

1. **Biến Orleans thành lớp RPC mỏng cho DB**. Nếu grain chỉ nhận request rồi query DB vô hồn, bạn chưa tận dụng đúng mô hình.
2. **Nhồi quá nhiều state vào một grain**. Dẫn đến activation nặng, serialization lớn, khó evolve schema.
3. **Call chéo grain quá nhiều**. Tăng latency và coupling.
4. **Không phân biệt timer và reminder**. Kết quả là task quan trọng biến mất sau restart.
5. **Thiếu idempotency cho side effect**. Ví dụ gửi email hai lần sau retry.
6. **Dùng grain cho truy vấn ad-hoc phức tạp** thay vì projection/read store.
7. **Không nghĩ đến versioning state**. Sau vài tháng nâng cấp sẽ đau đầu.

---

## 20. Checklist để bắt đầu một dự án Orleans nghiêm túc

- Xác định aggregate/entity nào thật sự stateful
- Chọn grain key rõ ràng theo domain
- Quyết định grain nào cần persistence
- Chọn storage cho state, membership, reminder
- Tách read model nơi cần query linh hoạt
- Thiết kế event hoặc stream cho side effect
- Bổ sung integration tests với TestCluster
- Đưa metrics và logs vào từ sớm
- Kiểm tra recovery khi silo restart
- Review idempotency cho email, payment, publish event

---

## 21. Lộ trình học Orleans hiệu quả

Nếu bạn đang bắt đầu, tôi khuyên học theo thứ tự:

1. Grain interface và implementation cơ bản
2. Persistent state
3. Grain-to-grain communication
4. Timers và reminders
5. Streams
6. Testing bằng TestCluster
7. Clustering thật với PostgreSQL hoặc SQL Server
8. Observability và production hardening

Sau khi đi qua chuỗi này, bạn sẽ hiểu Orleans không chỉ là một thư viện lạ, mà là một mô hình tư duy giúp biến distributed stateful programming thành thứ dễ kiểm soát hơn nhiều.

---

## 22. Mẫu use case thứ hai, quản lý thiết bị IoT với Orleans

Để thấy Orleans không chỉ hợp cho e-commerce, ta xét một bài toán IoT:

- mỗi thiết bị có `deviceId`
- gửi heartbeat vài giây một lần
- có cấu hình hiện tại và firmware version
- có cảnh báo nếu quá lâu không heartbeat
- có command cập nhật cấu hình từ control plane

Thiết kế tự nhiên bằng Orleans:

- `DeviceGrain(deviceId)` giữ online status, last heartbeat, config hiện tại
- `TenantDeviceIndexGrain(tenantId)` giữ danh sách device quan trọng hoặc thống kê nhanh
- `AlertDispatchGrain(alertId)` phụ trách luồng gửi cảnh báo, retry, dedupe

### 22.1 Interface và state

```csharp
public interface IDeviceGrain : IGrainWithStringKey
{
    Task ReportHeartbeatAsync(DeviceHeartbeat heartbeat);
    Task ApplyConfigAsync(DeviceConfig config);
    Task<DeviceSnapshotDto> GetSnapshotAsync();
}

public record DeviceHeartbeat(string FirmwareVersion, double Temperature, DateTime ReceivedAtUtc);
public record DeviceConfig(string SamplingMode, int IntervalSeconds);
public record DeviceSnapshotDto(string DeviceId, bool IsOnline, string FirmwareVersion, DateTime LastHeartbeatUtc, DeviceConfig? Config, double LastTemperature);
```

```csharp
[GenerateSerializer]
public class DeviceState
{
    [Id(0)] public bool IsOnline { get; set; }
    [Id(1)] public string FirmwareVersion { get; set; } = string.Empty;
    [Id(2)] public DateTime LastHeartbeatUtc { get; set; }
    [Id(3)] public DeviceConfig? Config { get; set; }
    [Id(4)] public double LastTemperature { get; set; }
}
```

### 22.2 Grain với reminder phát hiện offline

```csharp
public class DeviceGrain : Grain, IDeviceGrain, IRemindable
{
    private readonly IPersistentState<DeviceState> _state;
    private const string OfflineCheckReminder = "offline-check";

    public DeviceGrain([PersistentState("device", "Default")] IPersistentState<DeviceState> state)
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await RegisterOrUpdateReminder(
            OfflineCheckReminder,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public async Task ReportHeartbeatAsync(DeviceHeartbeat heartbeat)
    {
        _state.State.IsOnline = true;
        _state.State.FirmwareVersion = heartbeat.FirmwareVersion;
        _state.State.LastHeartbeatUtc = heartbeat.ReceivedAtUtc;
        _state.State.LastTemperature = heartbeat.Temperature;
        await _state.WriteStateAsync();
    }

    public async Task ApplyConfigAsync(DeviceConfig config)
    {
        _state.State.Config = config;
        await _state.WriteStateAsync();
    }

    public Task<DeviceSnapshotDto> GetSnapshotAsync()
        => Task.FromResult(new DeviceSnapshotDto(
            this.GetPrimaryKeyString(),
            _state.State.IsOnline,
            _state.State.FirmwareVersion,
            _state.State.LastHeartbeatUtc,
            _state.State.Config,
            _state.State.LastTemperature));

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != OfflineCheckReminder)
            return;

        var offline = DateTime.UtcNow - _state.State.LastHeartbeatUtc > TimeSpan.FromMinutes(5);
        if (offline && _state.State.IsOnline)
        {
            _state.State.IsOnline = false;
            await _state.WriteStateAsync();

            var alert = GrainFactory.GetGrain<IAlertDispatchGrain>($"device-offline/{this.GetPrimaryKeyString()}");
            await alert.DispatchAsync($"Thiết bị {this.GetPrimaryKeyString()} đã offline quá 5 phút");
        }
    }
}
```

Ví dụ này cho thấy Orleans đặc biệt hợp với những thực thể online/offline, session-like, cần timeout, cần giữ trạng thái mới nhất và xử lý theo identity.

---

## 23. Tích hợp Orleans với messaging hoặc HTTP ecosystem xung quanh

Trong hệ thống lớn, Orleans hiếm khi sống một mình. Một kiến trúc phổ biến là:

- HTTP API nhận request ngoài
- Orleans xử lý domain stateful nội bộ
- broker như MassTransit/RabbitMQ dùng cho integration event ra ngoài bounded context
- read model riêng cho dashboard hoặc search

Ví dụ sau khi `OrderGrain` được thanh toán, thay vì để các grain khác ôm hết side effect, bạn có thể publish integration event ra broker:

```csharp
public interface IIntegrationEventBridgeGrain : IGrainWithGuidKey
{
    Task PublishOrderPaidAsync(OrderPaidEvent evt);
}
```

```csharp
public class IntegrationEventBridgeGrain : Grain, IIntegrationEventBridgeGrain
{
    private readonly IPublishEndpoint _publishEndpoint;

    public IntegrationEventBridgeGrain(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishOrderPaidAsync(OrderPaidEvent evt)
        => _publishEndpoint.Publish(new
        {
            evt.OrderId,
            evt.UserId,
            evt.Total,
            evt.OccurredAtUtc
        });
}
```

Ý tưởng là:

- Orleans rất mạnh cho coordination nội bộ theo entity
- broker ngoài rất mạnh cho integration boundary liên service
- đừng ép một công cụ làm mọi vai trò

Nhiều hệ production khỏe nhất là sự kết hợp đúng giữa các mô hình.

---

## 24. Một số chiến lược performance tuning và capacity planning

Khi cluster bắt đầu lớn, bạn nên nghĩ đến các câu hỏi sau:

1. Grain nào activation count rất cao?
2. Grain nào có payload state lớn?
3. Storage write nào đang là bottleneck?
4. Có grain hot key nào khiến một node quá tải không?
5. Có logic nào nên chuyển từ synchronous grain-to-grain call sang event/stream không?

Một số chiến lược thực dụng:

- dùng stateless worker cho tác vụ tính toán ngắn, không cần state
- tách state lớn thành nhiều grains nhỏ theo access pattern
- đừng ghi storage mỗi thay đổi nhỏ nếu workload rất dày, có thể buffer hoặc flush theo chiến lược cẩn thận
- thêm cache read model bên ngoài cho query dashboard
- kiểm tra serialization size thường xuyên

Ví dụ buffer ngắn trước khi flush:

```csharp
public class MetricsAccumulatorGrain : Grain, IMetricsAccumulatorGrain
{
    private readonly List<double> _buffer = new();
    private IDisposable? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _timer = RegisterTimer(_ => FlushAsync(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public Task AddSampleAsync(double value)
    {
        _buffer.Add(value);
        return Task.CompletedTask;
    }

    private Task FlushAsync()
    {
        // batch write ra DB/time-series store
        _buffer.Clear();
        return Task.CompletedTask;
    }
}
```

Pattern này không dùng cho dữ liệu cần durability ngay lập tức, nhưng rất hợp với metrics hoặc telemetry tổng hợp.

---

## 25. Kết luận

Orleans đặc biệt đáng giá khi bài toán của bạn xoay quanh **nhiều thực thể có trạng thái, cần xử lý đồng thời, cần scale ngang, nhưng vẫn muốn giữ mô hình lập trình đơn giản**. Virtual Actor giúp bạn suy nghĩ bằng ngôn ngữ business entity thay vì routing table, distributed lock và cron recovery scripts.

Điểm mạnh lớn nhất của Orleans không phải chỉ là performance, mà là **giảm độ phức tạp nhận thức** cho một lớp bài toán distributed rất khó. Bạn vẫn cần kỷ luật về thiết kế aggregate, persistence, idempotency và observability. Nhưng nếu dùng đúng chỗ, Orleans cho phép đội .NET xây các hệ thống như order workflow, game session, device state, notification fan-out, quota control hay tenant orchestration với code rõ ràng hơn hẳn cách làm thủ công.

Nếu triển khai thực chiến, hãy bắt đầu bằng một bounded context nhỏ, ví dụ cart/order hoặc device/session. Đừng cố đưa cả hệ thống lên Orleans trong ngày đầu. Làm một use case stateful rõ ràng, đo latency, failure recovery và độ đơn giản của code. Khi team đã quen mental model, bạn sẽ thấy Orleans là một trong những công cụ mạnh nhất trong hệ .NET distributed application.
