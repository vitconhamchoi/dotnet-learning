# gRPC trong .NET: High-performance Service-to-Service Communication

## 1. Tại sao gRPC cho Service-to-Service Communication

Khi microservices cần giao tiếp synchronously, bạn có hai lựa chọn chính: REST/HTTP và gRPC. REST quen thuộc nhưng có overhead đáng kể ở quy mô lớn.

**So sánh REST vs gRPC**:

| Tiêu chí | REST/JSON | gRPC/Protobuf |
|----------|-----------|---------------|
| Serialization | JSON (text) | Protobuf (binary) |
| Payload size | Lớn hơn 3-10x | Nhỏ hơn |
| Latency | Cao hơn | Thấp hơn ~30-40% |
| Type safety | Không (phải generate) | Có (contract-first) |
| Streaming | Không | Có (bi-directional) |
| Browser support | Tốt | Cần gRPC-Web |
| Debugging | Dễ | Cần tooling |
| Schema evolution | Không có schema | Protobuf versioning |

gRPC đặc biệt hiệu quả cho:
- Service-to-service internal communication
- Real-time streaming (price feeds, live feeds)
- Mobile clients cần tiết kiệm bandwidth
- High-volume low-latency internal APIs

---

## 2. Định nghĩa Service với Protocol Buffers

```protobuf
// Protos/order_service.proto
syntax = "proto3";

option csharp_namespace = "OrderService.Grpc";

package orders;

import "google/protobuf/timestamp.proto";
import "google/protobuf/empty.proto";

// Service definition
service OrderService {
  // Unary: một request, một response
  rpc PlaceOrder(PlaceOrderRequest) returns (PlaceOrderResponse);
  rpc GetOrder(GetOrderRequest) returns (OrderDto);
  rpc ApproveOrder(ApproveOrderRequest) returns (google.protobuf.Empty);
  rpc CancelOrder(CancelOrderRequest) returns (google.protobuf.Empty);
  
  // Server streaming: một request, nhiều response
  rpc GetOrderStatusStream(GetOrderRequest) returns (stream OrderStatusUpdate);
  
  // Client streaming: nhiều request, một response
  rpc BulkImportOrders(stream PlaceOrderRequest) returns (BulkImportResult);
  
  // Bi-directional streaming
  rpc ProcessOrderBatch(stream PlaceOrderRequest) returns (stream OrderProcessResult);
}

message PlaceOrderRequest {
  string customer_id = 1;
  string shipping_address = 2;
  repeated OrderLine lines = 3;
  string payment_method = 4;
}

message OrderLine {
  string sku = 1;
  int32 quantity = 2;
  double unit_price = 3;
}

message PlaceOrderResponse {
  string order_id = 1;
  string status = 2;
  double total_amount = 3;
  google.protobuf.Timestamp placed_at = 4;
}

message GetOrderRequest {
  string order_id = 1;
}

message OrderDto {
  string id = 1;
  string customer_id = 2;
  string status = 3;
  double total_amount = 4;
  repeated OrderLine lines = 5;
  google.protobuf.Timestamp placed_at = 6;
  google.protobuf.Timestamp updated_at = 7;
}

message OrderStatusUpdate {
  string order_id = 1;
  string status = 2;
  string message = 3;
  google.protobuf.Timestamp timestamp = 4;
}

message ApproveOrderRequest {
  string order_id = 1;
  string approved_by = 2;
}

message CancelOrderRequest {
  string order_id = 1;
  string reason = 2;
}

message BulkImportResult {
  int32 success_count = 1;
  int32 failure_count = 1;
  repeated string failed_order_ids = 3;
}

message OrderProcessResult {
  string order_id = 1;
  bool success = 2;
  string error_message = 3;
}
```

---

## 3. Implement gRPC Server

```bash
dotnet add package Grpc.AspNetCore
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

```csharp
// OrderGrpcService.cs
public class OrderGrpcService : OrderService.OrderServiceBase
{
    private readonly IOrderRepository _repo;
    private readonly IOrderCommandBus _commandBus;
    private readonly ILogger<OrderGrpcService> _logger;

    public OrderGrpcService(
        IOrderRepository repo,
        IOrderCommandBus commandBus,
        ILogger<OrderGrpcService> logger)
    {
        _repo = repo;
        _commandBus = commandBus;
        _logger = logger;
    }

    // Unary RPC
    public override async Task<PlaceOrderResponse> PlaceOrder(
        PlaceOrderRequest request,
        ServerCallContext context)
    {
        var orderId = Guid.NewGuid();
        
        var cmd = new PlaceOrderCommand(
            orderId,
            Guid.Parse(request.CustomerId),
            request.Lines.Select(l => new OrderLine(l.Sku, l.Quantity, (decimal)l.UnitPrice)).ToList(),
            request.ShippingAddress,
            request.PaymentMethod);

        try
        {
            await _commandBus.SendAsync(cmd, context.CancellationToken);
            
            return new PlaceOrderResponse
            {
                OrderId = orderId.ToString(),
                Status = "Pending",
                TotalAmount = (double)request.Lines.Sum(l => l.UnitPrice * l.Quantity),
                PlacedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            };
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order");
            throw new RpcException(new Status(StatusCode.Internal, "Internal error"));
        }
    }

    // Server streaming RPC
    public override async Task GetOrderStatusStream(
        GetOrderRequest request,
        IServerStreamWriter<OrderStatusUpdate> responseStream,
        ServerCallContext context)
    {
        var orderId = Guid.Parse(request.OrderId);
        
        // Stream status updates until completed or cancelled
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var order = await _repo.FindByIdAsync(orderId, context.CancellationToken);
            if (order is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Order {orderId} not found"));
            }

            await responseStream.WriteAsync(new OrderStatusUpdate
            {
                OrderId = orderId.ToString(),
                Status = order.Status.ToString(),
                Message = $"Order is {order.Status}",
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            }, context.CancellationToken);

            if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                break;

            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
        }
    }

    // Bi-directional streaming RPC
    public override async Task ProcessOrderBatch(
        IAsyncStreamReader<PlaceOrderRequest> requestStream,
        IServerStreamWriter<OrderProcessResult> responseStream,
        ServerCallContext context)
    {
        var processingTasks = new List<Task>();

        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            // Process mỗi order song song
            processingTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var orderId = Guid.NewGuid();
                    await _commandBus.SendAsync(
                        new PlaceOrderCommand(orderId, Guid.Parse(request.CustomerId), 
                            [], request.ShippingAddress, request.PaymentMethod),
                        context.CancellationToken);
                    
                    await responseStream.WriteAsync(new OrderProcessResult
                    {
                        OrderId = orderId.ToString(),
                        Success = true
                    }, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    await responseStream.WriteAsync(new OrderProcessResult
                    {
                        OrderId = Guid.NewGuid().ToString(),
                        Success = false,
                        ErrorMessage = ex.Message
                    }, context.CancellationToken);
                }
            }, context.CancellationToken));
        }

        await Task.WhenAll(processingTasks);
    }
}
```

### 3.1 Đăng ký gRPC service

```csharp
// Program.cs
builder.Services.AddGrpc(opts =>
{
    opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
    opts.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16 MB
    opts.MaxSendMessageSize = 16 * 1024 * 1024;
    
    // Compression
    opts.ResponseCompressionLevel = CompressionLevel.Optimal;
    opts.ResponseCompressionAlgorithm = "gzip";
});

builder.Services.AddGrpcReflection(); // Cho phép grpcurl, Postman khám phá service

// Authentication cho gRPC
builder.Services.AddGrpc()
    .AddServiceOptions<OrderGrpcService>(opts =>
    {
        opts.Interceptors.Add<AuthInterceptor>();
        opts.Interceptors.Add<LoggingInterceptor>();
    });

var app = builder.Build();

app.MapGrpcService<OrderGrpcService>();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}
```

---

## 4. gRPC Client

```csharp
// Đăng ký typed gRPC client
builder.Services.AddGrpcClient<OrderService.OrderServiceClient>(opts =>
{
    opts.Address = new Uri(builder.Configuration["Services:OrderService"]!);
})
.ConfigureChannel(opts =>
{
    opts.Credentials = ChannelCredentials.SecureSsl; // TLS
    opts.MaxRetryAttempts = 3;
})
.AddCallCredentials((ctx, metadata, sp) =>
{
    var tokenService = sp.GetRequiredService<ITokenService>();
    var token = tokenService.GetServiceToken();
    metadata.Add("Authorization", $"Bearer {token}");
    return Task.CompletedTask;
})
.AddStandardResilienceHandler(); // Polly resilience

// Sử dụng
public class OrderApiController : ControllerBase
{
    private readonly OrderService.OrderServiceClient _grpcClient;

    public OrderApiController(OrderService.OrderServiceClient grpcClient)
    {
        _grpcClient = grpcClient;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest model)
    {
        try
        {
            var response = await _grpcClient.PlaceOrderAsync(new GrpcPlaceOrderRequest
            {
                CustomerId = model.CustomerId.ToString(),
                ShippingAddress = model.ShippingAddress,
                Lines = { model.Lines.Select(l => new GrpcOrderLine
                {
                    Sku = l.Sku,
                    Quantity = l.Quantity,
                    UnitPrice = (double)l.Price
                }) }
            }, deadline: DateTime.UtcNow.AddSeconds(10));
            
            return Accepted(new { orderId = response.OrderId });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return BadRequest(ex.Status.Detail);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            return StatusCode(504, "Order service timeout");
        }
    }

    // Server-sent events từ gRPC streaming
    [HttpGet("orders/{id}/status-stream")]
    public async IAsyncEnumerable<OrderStatusUpdate> GetStatusStream(
        string id,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = _grpcClient.GetOrderStatusStream(
            new GetOrderRequest { OrderId = id },
            cancellationToken: ct);

        await foreach (var update in stream.ResponseStream.ReadAllAsync(ct))
        {
            yield return new OrderStatusUpdate(update.Status, update.Message, update.Timestamp.ToDateTimeOffset());
        }
    }
}
```

---

## 5. gRPC Interceptors: cross-cutting concerns

```csharp
// Server-side interceptor cho logging và auth
public class LoggingInterceptor : Interceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        
        _logger.LogInformation("gRPC call: {Method} started", context.Method);

        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            
            _logger.LogInformation("gRPC call: {Method} completed in {Elapsed}ms",
                context.Method, sw.ElapsedMilliseconds);
            
            return response;
        }
        catch (RpcException)
        {
            sw.Stop();
            _logger.LogWarning("gRPC call: {Method} failed in {Elapsed}ms", 
                context.Method, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "gRPC call: {Method} unhandled exception", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal error"));
        }
    }
}
```

---

## 6. gRPC-Web cho Browser Clients

```csharp
// Kích hoạt gRPC-Web (cho browser)
builder.Services.AddGrpcWeb(opts => opts.GrpcWebEnabled = true);

app.UseGrpcWeb();
app.MapGrpcService<OrderGrpcService>().EnableGrpcWeb();
```

---

## 7. Checklist production cho gRPC

- [ ] TLS cho tất cả gRPC connections trong production
- [ ] Deadlines (timeout) cho tất cả gRPC calls - client phải set deadline
- [ ] Interceptors cho logging, auth, metrics
- [ ] Proto schema versioning: thêm field không phải xóa (backward compatible)
- [ ] gRPC health checking protocol cho Kubernetes
- [ ] Reflection service chỉ bật ở development, không production
- [ ] Load balancing: cần client-side hoặc L7 load balancer (không phải L4)
- [ ] Max message size phù hợp với payload thực tế
- [ ] Error handling: map domain exceptions thành gRPC status codes
- [ ] Test với grpcurl và BloomRPC
