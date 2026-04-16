# gRPC trong .NET

## Mục Lục
1. [Giới thiệu gRPC](#gioi-thieu-grpc)
2. [Proto File Definitions](#proto-file-definitions)
3. [Unary RPC](#unary-rpc)
4. [Server Streaming](#server-streaming)
5. [Client Streaming](#client-streaming)
6. [Bidirectional Streaming](#bidirectional-streaming)
7. [Complete Sample: ProductCatalog Service](#complete-sample)
8. [Interceptors](#interceptors)
9. [Authentication trong gRPC](#authentication)
10. [gRPC vs REST](#grpc-vs-rest)
11. [gRPC-Web](#grpc-web)
12. [Best Practices](#best-practices)

---

## 1. Giới thiệu gRPC

gRPC (Google Remote Procedure Call) là một framework RPC hiệu năng cao, mã nguồn mở, sử dụng HTTP/2 và Protocol Buffers (protobuf) để serialize dữ liệu.

### So sánh gRPC vs REST

```
┌─────────────────────────────────────────────────────────────────┐
│                    gRPC vs REST Comparison                      │
├─────────────────┬───────────────────────┬───────────────────────┤
│ Aspect          │ gRPC                  │ REST                  │
├─────────────────┼───────────────────────┼───────────────────────┤
│ Protocol        │ HTTP/2                │ HTTP/1.1, HTTP/2      │
│ Format          │ Protocol Buffers      │ JSON, XML             │
│ Contract        │ .proto file           │ OpenAPI/Swagger       │
│ Streaming       │ Full bidirectional    │ Limited (SSE, WS)     │
│ Performance     │ ~7-10x faster         │ Baseline              │
│ Browser support │ Via gRPC-Web          │ Native                │
│ Code generation │ Built-in              │ Optional              │
│ Human readable  │ No (binary)           │ Yes (JSON)            │
│ Versioning      │ Field numbers         │ URL versioning        │
└─────────────────┴───────────────────────┴───────────────────────┘

gRPC Communication Flow:
┌──────────────┐    Protobuf (binary)    ┌──────────────────────┐
│   gRPC       │ ──────────────────────► │  gRPC Server         │
│   Client     │ ◄────────────────────── │  (.NET)              │
└──────────────┘    HTTP/2 multiplexed   └──────────────────────┘
```

### Khi nào dùng gRPC:
- Microservice-to-microservice communication
- Low latency, high throughput requirements
- Streaming data (real-time updates)
- Polyglot environments (client/server khác ngôn ngữ)
- Internal APIs (không cần browser support trực tiếp)

---

## 2. Proto File Definitions

### Cài đặt Project

```xml
<!-- ProductCatalog.grpc.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="2.67.0" />
    <PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="2.67.0" />
    <PackageReference Include="Google.Protobuf" Version="3.29.2" />
    <PackageReference Include="Grpc.Tools" Version="2.67.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <!-- Proto files -->
  <ItemGroup>
    <Protobuf Include="Protos\product.proto" GrpcServices="Server" />
    <Protobuf Include="Protos\product_client.proto" GrpcServices="Client" />
  </ItemGroup>
</Project>
```

### Proto Definitions

```protobuf
// Protos/product.proto
syntax = "proto3";

option csharp_namespace = "ProductCatalog.Grpc";

package product;

import "google/protobuf/timestamp.proto";
import "google/protobuf/empty.proto";
import "google/protobuf/wrappers.proto";

// ==================== Messages ====================

message ProductId {
  string value = 1;
}

message Money {
  double amount = 1;
  string currency = 2;
}

message Product {
  string id = 1;
  string name = 2;
  string description = 3;
  Money price = 4;
  int32 stock_quantity = 5;
  string category_id = 6;
  bool is_active = 7;
  google.protobuf.Timestamp created_at = 8;
  repeated string image_urls = 9;
  map<string, string> attributes = 10;
}

message CreateProductRequest {
  string name = 1;
  string description = 2;
  Money price = 3;
  int32 initial_stock = 4;
  string category_id = 5;
  repeated string image_urls = 6;
  map<string, string> attributes = 7;
}

message UpdateProductRequest {
  string id = 1;
  google.protobuf.StringValue name = 2;
  google.protobuf.StringValue description = 3;
  Money price = 4;
}

message GetProductRequest {
  string id = 1;
}

message SearchProductsRequest {
  string query = 1;
  string category_id = 2;
  Money min_price = 3;
  Money max_price = 4;
  int32 page = 5;
  int32 page_size = 6;
  SortField sort_by = 7;
  SortOrder sort_order = 8;
}

message SearchProductsResponse {
  repeated Product products = 1;
  int32 total_count = 2;
  int32 page = 3;
  int32 page_size = 4;
}

message GetProductsRequest {
  repeated string ids = 1;
}

message GetProductsResponse {
  repeated Product products = 1;
}

message UpdateStockRequest {
  string product_id = 1;
  int32 quantity_change = 2;  // positive = add, negative = reserve
  string reason = 3;
}

message StockUpdate {
  string product_id = 1;
  int32 new_quantity = 2;
  google.protobuf.Timestamp updated_at = 3;
}

message WatchProductRequest {
  string product_id = 1;
}

message ProductEvent {
  string product_id = 1;
  ProductEventType event_type = 2;
  Product product = 3;
  google.protobuf.Timestamp occurred_at = 4;
}

message BulkStockRequest {
  repeated StockUpdateItem items = 1;
}

message StockUpdateItem {
  string product_id = 1;
  int32 quantity_change = 2;
}

message BulkStockResult {
  repeated StockUpdateResult results = 1;
}

message StockUpdateResult {
  string product_id = 1;
  bool success = 2;
  string error_message = 3;
  int32 new_quantity = 4;
}

// ==================== Enums ====================

enum SortField {
  SORT_FIELD_UNSPECIFIED = 0;
  SORT_FIELD_NAME = 1;
  SORT_FIELD_PRICE = 2;
  SORT_FIELD_CREATED_AT = 3;
  SORT_FIELD_STOCK = 4;
}

enum SortOrder {
  SORT_ORDER_UNSPECIFIED = 0;
  SORT_ORDER_ASC = 1;
  SORT_ORDER_DESC = 2;
}

enum ProductEventType {
  PRODUCT_EVENT_TYPE_UNSPECIFIED = 0;
  PRODUCT_EVENT_TYPE_CREATED = 1;
  PRODUCT_EVENT_TYPE_UPDATED = 2;
  PRODUCT_EVENT_TYPE_DELETED = 3;
  PRODUCT_EVENT_TYPE_STOCK_CHANGED = 4;
  PRODUCT_EVENT_TYPE_PRICE_CHANGED = 5;
}

// ==================== Services ====================

service ProductCatalogService {
  // Unary RPCs
  rpc GetProduct(GetProductRequest) returns (Product);
  rpc CreateProduct(CreateProductRequest) returns (Product);
  rpc UpdateProduct(UpdateProductRequest) returns (Product);
  rpc DeleteProduct(GetProductRequest) returns (google.protobuf.Empty);
  rpc SearchProducts(SearchProductsRequest) returns (SearchProductsResponse);
  rpc GetProductsBatch(GetProductsRequest) returns (GetProductsResponse);
  
  // Server Streaming - client nhận stream of updates
  rpc WatchProduct(WatchProductRequest) returns (stream ProductEvent);
  
  // Client Streaming - client gửi nhiều updates, server trả 1 response
  rpc BulkUpdateStock(stream StockUpdateItem) returns (BulkStockResult);
  
  // Bidirectional Streaming
  rpc SyncInventory(stream StockUpdateItem) returns (stream StockUpdateResult);
}

// Inventory Service
service InventoryService {
  rpc GetStock(GetProductRequest) returns (StockUpdate);
  rpc UpdateStock(UpdateStockRequest) returns (StockUpdate);
  rpc WatchStock(WatchProductRequest) returns (stream StockUpdate);
}
```

---

## 3. Unary RPC - Server Implementation

```csharp
// Services/ProductCatalogGrpcService.cs
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using ProductCatalog.Application;
using ProductCatalog.Grpc;

namespace ProductCatalog.Services;

public class ProductCatalogGrpcService : ProductCatalogService.ProductCatalogServiceBase
{
    private readonly IProductRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<ProductCatalogGrpcService> _logger;
    private readonly IProductEventStream _eventStream;

    public ProductCatalogGrpcService(
        IProductRepository repository,
        IMapper mapper,
        ILogger<ProductCatalogGrpcService> logger,
        IProductEventStream eventStream)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
        _eventStream = eventStream;
    }

    // Unary RPC - Get single product
    public override async Task<Product> GetProduct(
        GetProductRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("GetProduct called for ID: {ProductId}", request.Id);

        var product = await _repository.GetByIdAsync(
            Guid.Parse(request.Id),
            context.CancellationToken);

        if (product is null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Product {request.Id} not found"));
        }

        return _mapper.Map<Product>(product);
    }

    // Unary RPC - Create product
    public override async Task<Product> CreateProduct(
        CreateProductRequest request,
        ServerCallContext context)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Product name is required"));
        }

        if (request.Price.Amount <= 0)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Product price must be positive"));
        }

        var domainProduct = Domain.Product.Create(
            request.Name,
            request.Description,
            new Domain.Money(request.Price.Amount, request.Price.Currency),
            request.InitialStock,
            Guid.Parse(request.CategoryId));

        await _repository.AddAsync(domainProduct, context.CancellationToken);
        await _repository.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Created product {ProductId}", domainProduct.Id);

        return _mapper.Map<Product>(domainProduct);
    }

    // Unary RPC - Search products
    public override async Task<SearchProductsResponse> SearchProducts(
        SearchProductsRequest request,
        ServerCallContext context)
    {
        var filter = new ProductFilter
        {
            Query = request.Query,
            CategoryId = string.IsNullOrEmpty(request.CategoryId) ? null : request.CategoryId,
            MinPrice = request.MinPrice?.Amount,
            MaxPrice = request.MaxPrice?.Amount,
            Page = request.Page == 0 ? 1 : request.Page,
            PageSize = request.PageSize == 0 ? 20 : Math.Min(request.PageSize, 100)
        };

        var (products, totalCount) = await _repository.SearchAsync(filter, context.CancellationToken);

        var response = new SearchProductsResponse
        {
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        response.Products.AddRange(products.Select(_mapper.Map<Product>));
        return response;
    }

    // Unary RPC - Batch get products
    public override async Task<GetProductsResponse> GetProductsBatch(
        GetProductsRequest request,
        ServerCallContext context)
    {
        var ids = request.Ids.Select(Guid.Parse).ToList();
        var products = await _repository.GetByIdsAsync(ids, context.CancellationToken);

        var response = new GetProductsResponse();
        response.Products.AddRange(products.Select(_mapper.Map<Product>));
        return response;
    }

    // ==================== SERVER STREAMING ====================
    // Server gửi nhiều events về một product cho client
    public override async Task WatchProduct(
        WatchProductRequest request,
        IServerStreamWriter<ProductEvent> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Client watching product {ProductId}", request.ProductId);

        var productId = Guid.Parse(request.ProductId);

        // Verify product exists
        var product = await _repository.GetByIdAsync(productId, context.CancellationToken);
        if (product is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Product {request.ProductId} not found"));
        }

        // Subscribe to events for this product
        await foreach (var @event in _eventStream.WatchAsync(productId, context.CancellationToken))
        {
            try
            {
                var grpcEvent = new ProductEvent
                {
                    ProductId = request.ProductId,
                    EventType = MapEventType(@event.Type),
                    Product = _mapper.Map<Product>(@event.Product),
                    OccurredAt = Timestamp.FromDateTime(@event.OccurredAt)
                };

                await responseStream.WriteAsync(grpcEvent, context.CancellationToken);
                _logger.LogDebug("Sent event {EventType} for product {ProductId}", @event.Type, productId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client disconnected from product watch {ProductId}", productId);
                break;
            }
        }
    }

    // ==================== CLIENT STREAMING ====================
    // Client gửi nhiều stock updates, server aggregate và trả 1 kết quả
    public override async Task<BulkStockResult> BulkUpdateStock(
        IAsyncStreamReader<StockUpdateItem> requestStream,
        ServerCallContext context)
    {
        var results = new List<StockUpdateResult>();
        var updates = new List<StockUpdateItem>();

        // Collect all items from client stream
        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            updates.Add(item);
            _logger.LogDebug("Received stock update: Product {ProductId}, Qty Change {Change}",
                item.ProductId, item.QuantityChange);
        }

        _logger.LogInformation("Processing bulk stock update: {Count} items", updates.Count);

        // Process all updates in a transaction
        foreach (var update in updates)
        {
            try
            {
                var product = await _repository.GetByIdAsync(
                    Guid.Parse(update.ProductId),
                    context.CancellationToken);

                if (product is null)
                {
                    results.Add(new StockUpdateResult
                    {
                        ProductId = update.ProductId,
                        Success = false,
                        ErrorMessage = "Product not found"
                    });
                    continue;
                }

                if (update.QuantityChange < 0)
                    product.ReserveStock(Math.Abs(update.QuantityChange));
                else
                    product.RestoreStock(update.QuantityChange);

                await _repository.SaveChangesAsync(context.CancellationToken);

                results.Add(new StockUpdateResult
                {
                    ProductId = update.ProductId,
                    Success = true,
                    NewQuantity = product.StockQuantity
                });
            }
            catch (Exception ex)
            {
                results.Add(new StockUpdateResult
                {
                    ProductId = update.ProductId,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        var bulkResult = new BulkStockResult();
        bulkResult.Results.AddRange(results);
        return bulkResult;
    }

    // ==================== BIDIRECTIONAL STREAMING ====================
    // Client gửi updates, server xử lý và gửi kết quả ngay lập tức
    public override async Task SyncInventory(
        IAsyncStreamReader<StockUpdateItem> requestStream,
        IServerStreamWriter<StockUpdateResult> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Inventory sync session started");

        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            StockUpdateResult result;

            try
            {
                var product = await _repository.GetByIdAsync(
                    Guid.Parse(item.ProductId),
                    context.CancellationToken);

                if (product is null)
                {
                    result = new StockUpdateResult
                    {
                        ProductId = item.ProductId,
                        Success = false,
                        ErrorMessage = "Product not found"
                    };
                }
                else
                {
                    if (item.QuantityChange < 0)
                        product.ReserveStock(Math.Abs(item.QuantityChange));
                    else
                        product.RestoreStock(item.QuantityChange);

                    await _repository.SaveChangesAsync(context.CancellationToken);

                    result = new StockUpdateResult
                    {
                        ProductId = item.ProductId,
                        Success = true,
                        NewQuantity = product.StockQuantity
                    };
                }
            }
            catch (Exception ex)
            {
                result = new StockUpdateResult
                {
                    ProductId = item.ProductId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }

            // Gửi kết quả ngay lập tức cho client
            await responseStream.WriteAsync(result, context.CancellationToken);
        }

        _logger.LogInformation("Inventory sync session completed");
    }

    private static ProductEventType MapEventType(Domain.ProductEventType type) => type switch
    {
        Domain.ProductEventType.Created => ProductEventType.ProductEventTypeCreated,
        Domain.ProductEventType.Updated => ProductEventType.ProductEventTypeUpdated,
        Domain.ProductEventType.Deleted => ProductEventType.ProductEventTypeDeleted,
        Domain.ProductEventType.StockChanged => ProductEventType.ProductEventTypeStockChanged,
        Domain.ProductEventType.PriceChanged => ProductEventType.ProductEventTypePriceChanged,
        _ => ProductEventType.ProductEventTypeUnspecified
    };
}
```

---

## 4. gRPC Client Implementation

```csharp
// Client/ProductCatalogClient.cs
public class ProductCatalogClient : IProductCatalogClient
{
    private readonly ProductCatalogService.ProductCatalogServiceClient _client;
    private readonly ILogger<ProductCatalogClient> _logger;

    public ProductCatalogClient(
        ProductCatalogService.ProductCatalogServiceClient client,
        ILogger<ProductCatalogClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    // Unary Call
    public async Task<ProductDto?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            var request = new GetProductRequest { Id = productId.ToString() };
            var response = await _client.GetProductAsync(request, cancellationToken: ct);
            return MapToDto(response);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC error getting product {ProductId}: {Status}", productId, ex.Status);
            throw;
        }
    }

    // Unary Call với deadline
    public async Task<SearchResult> SearchProductsAsync(
        SearchRequest search,
        CancellationToken ct = default)
    {
        var request = new SearchProductsRequest
        {
            Query = search.Query ?? "",
            Page = search.Page,
            PageSize = search.PageSize,
            SortBy = SortField.SortFieldPrice,
            SortOrder = SortOrder.SortOrderAsc
        };

        if (search.MinPrice.HasValue)
            request.MinPrice = new Grpc.Money { Amount = search.MinPrice.Value, Currency = "VND" };
        
        if (search.MaxPrice.HasValue)
            request.MaxPrice = new Grpc.Money { Amount = search.MaxPrice.Value, Currency = "VND" };

        // Set deadline cho request
        var callOptions = new CallOptions(
            deadline: DateTime.UtcNow.AddSeconds(10),
            cancellationToken: ct);

        var response = await _client.SearchProductsAsync(request, callOptions);

        return new SearchResult
        {
            Products = response.Products.Select(MapToDto).ToList(),
            TotalCount = response.TotalCount,
            Page = response.Page,
            PageSize = response.PageSize
        };
    }

    // Server Streaming - nhận events từ server
    public async IAsyncEnumerable<ProductEventDto> WatchProductAsync(
        Guid productId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new WatchProductRequest { ProductId = productId.ToString() };
        
        using var call = _client.WatchProduct(request, cancellationToken: ct);
        
        await foreach (var @event in call.ResponseStream.ReadAllAsync(ct))
        {
            yield return new ProductEventDto(
                Guid.Parse(@event.ProductId),
                @event.EventType.ToString(),
                @event.OccurredAt.ToDateTime());
        }
    }

    // Client Streaming - gửi nhiều updates
    public async Task<BulkStockResultDto> BulkUpdateStockAsync(
        IEnumerable<StockUpdateDto> updates,
        CancellationToken ct = default)
    {
        using var call = _client.BulkUpdateStock(cancellationToken: ct);
        
        foreach (var update in updates)
        {
            await call.RequestStream.WriteAsync(new StockUpdateItem
            {
                ProductId = update.ProductId.ToString(),
                QuantityChange = update.QuantityChange
            }, ct);
        }
        
        // Signal end of client stream
        await call.RequestStream.CompleteAsync();
        
        // Wait for server response
        var result = await call.ResponseAsync;
        
        return new BulkStockResultDto
        {
            Results = result.Results.Select(r => new StockResultDto
            {
                ProductId = Guid.Parse(r.ProductId),
                Success = r.Success,
                ErrorMessage = r.ErrorMessage,
                NewQuantity = r.NewQuantity
            }).ToList()
        };
    }

    // Bidirectional Streaming - realtime inventory sync
    public async Task SyncInventoryAsync(
        IAsyncEnumerable<StockUpdateDto> updates,
        Func<StockResultDto, Task> onResult,
        CancellationToken ct = default)
    {
        using var call = _client.SyncInventory(cancellationToken: ct);

        // Start reading responses in background
        var readTask = Task.Run(async () =>
        {
            await foreach (var result in call.ResponseStream.ReadAllAsync(ct))
            {
                await onResult(new StockResultDto
                {
                    ProductId = Guid.Parse(result.ProductId),
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage,
                    NewQuantity = result.NewQuantity
                });
            }
        }, ct);

        // Send updates
        await foreach (var update in updates.WithCancellation(ct))
        {
            await call.RequestStream.WriteAsync(new StockUpdateItem
            {
                ProductId = update.ProductId.ToString(),
                QuantityChange = update.QuantityChange
            }, ct);
        }

        await call.RequestStream.CompleteAsync();
        await readTask;
    }

    private static ProductDto MapToDto(Product product) => new(
        Guid.Parse(product.Id),
        product.Name,
        product.Description,
        product.Price.Amount,
        product.Price.Currency,
        product.StockQuantity,
        product.IsActive);
}

// Registration
builder.Services.AddGrpcClient<ProductCatalogService.ProductCatalogServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["Services:ProductCatalog:GrpcUrl"]!);
})
.ConfigureChannel(channel =>
{
    channel.HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        EnableMultipleHttp2Connections = true
    };
})
.AddCallCredentials((context, metadata) =>
{
    // Add JWT token to each call
    var token = context.ServiceProvider
        .GetRequiredService<ITokenProvider>()
        .GetToken();
    
    if (!string.IsNullOrEmpty(token))
        metadata.Add("Authorization", $"Bearer {token}");
    
    return Task.CompletedTask;
})
.AddRetryPolicy(new GrpcRetryPolicy
{
    MaxAttempts = 3,
    InitialBackoff = TimeSpan.FromMilliseconds(100),
    MaxBackoff = TimeSpan.FromSeconds(1),
    BackoffMultiplier = 1.5,
    RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.DeadlineExceeded }
});
```

---

## 5. Interceptors

### Server-side Interceptors

```csharp
// Logging Interceptor
public class LoggingInterceptor : Interceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;

    public LoggingInterceptor(ILogger<LoggingInterceptor> logger)
    {
        _logger = logger;
    }

    // Intercept Unary calls
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var method = context.Method;
        var peer = context.Peer;

        _logger.LogInformation(
            "gRPC call started: {Method} from {Peer}", method, peer);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            
            _logger.LogInformation(
                "gRPC call completed: {Method} in {ElapsedMs}ms",
                method, sw.ElapsedMilliseconds);
            
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "gRPC call failed: {Method} in {ElapsedMs}ms. Error: {Error}",
                method, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    // Intercept Server Streaming calls
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        _logger.LogInformation("Server streaming started: {Method}", context.Method);
        
        try
        {
            await continuation(request, responseStream, context);
        }
        finally
        {
            _logger.LogInformation("Server streaming ended: {Method}", context.Method);
        }
    }
}

// Exception Handling Interceptor
public class ExceptionHandlingInterceptor : Interceptor
{
    private readonly ILogger<ExceptionHandlingInterceptor> _logger;

    public ExceptionHandlingInterceptor(ILogger<ExceptionHandlingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (DomainException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (UnauthorizedException ex)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, ex.Message));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            _logger.LogError(ex, "Unhandled exception in gRPC handler");
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred"));
        }
    }
}

// Validation Interceptor
public class ValidationInterceptor : Interceptor
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var validator = _serviceProvider.GetService<IValidator<TRequest>>();
        
        if (validator is not null)
        {
            var validationResult = await validator.ValidateAsync(request, context.CancellationToken);
            
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                throw new RpcException(new Status(StatusCode.InvalidArgument, errors));
            }
        }
        
        return await continuation(request, context);
    }
}

// Registration trong Program.cs
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
    options.MaxSendMessageSize = 16 * 1024 * 1024;    // 16MB
    options.Interceptors.Add<LoggingInterceptor>();
    options.Interceptors.Add<ExceptionHandlingInterceptor>();
    options.Interceptors.Add<ValidationInterceptor>();
});
```

---

## 6. Authentication trong gRPC

```csharp
// JWT Authentication cho gRPC
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = false; // Dev only
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
    
    options.AddPolicy("ProductManager", policy =>
        policy.RequireRole("Admin", "ProductManager"));
});

// Secure gRPC Service
[Authorize]
public class SecureProductCatalogGrpcService : ProductCatalogService.ProductCatalogServiceBase
{
    // Admin-only operation
    [Authorize(Policy = "AdminOnly")]
    public override async Task<Product> CreateProduct(
        CreateProductRequest request,
        ServerCallContext context)
    {
        // context.GetHttpContext().User contains ClaimsPrincipal
        var userId = context.GetHttpContext().User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("Product created by user {UserId}", userId);
        
        // ... implementation
        return new Product();
    }

    // Any authenticated user
    public override async Task<Product> GetProduct(
        GetProductRequest request,
        ServerCallContext context)
    {
        // ... implementation
        return new Product();
    }
}

// Authentication Interceptor phía client
public class AuthenticationInterceptor : Interceptor
{
    private readonly ITokenProvider _tokenProvider;

    public AuthenticationInterceptor(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = new Metadata();
        AddAuthHeader(metadata);

        var newContext = context with
        {
            Options = context.Options.WithHeaders(metadata)
        };

        return continuation(request, newContext);
    }

    private void AddAuthHeader(Metadata metadata)
    {
        var token = _tokenProvider.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            metadata.Add("Authorization", $"Bearer {token}");
        }
    }
}
```

---

## 7. gRPC Health Checks và Reflection

```csharp
// Program.cs - Full server setup
var builder = WebApplication.CreateBuilder(args);

// gRPC services
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<LoggingInterceptor>();
    options.Interceptors.Add<ExceptionHandlingInterceptor>();
});

// gRPC Reflection (for grpcurl, Postman)
builder.Services.AddGrpcReflection();

// Health checks
builder.Services.AddGrpcHealthChecks()
    .AddNpgsql(builder.Configuration.GetConnectionString("ProductsDb")!);

var app = builder.Build();

// Route gRPC services
app.MapGrpcService<ProductCatalogGrpcService>();
app.MapGrpcService<InventoryGrpcService>();
app.MapGrpcHealthChecksService();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.Run();

// Testing với grpcurl:
// grpcurl -plaintext localhost:5001 list
// grpcurl -plaintext -d '{"id": "123"}' localhost:5001 product.ProductCatalogService/GetProduct
```

---

## 8. gRPC-Web cho Browser Clients

### Server Setup

```csharp
// Blazor WASM hoặc JavaScript có thể dùng gRPC-Web
builder.Services.AddGrpcWebClient(options =>
{
    // Cấu hình gRPC-Web
});

// Hoặc trong ASP.NET Core server:
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.UseCors(); // Required for gRPC-Web with CORS

app.MapGrpcService<ProductCatalogGrpcService>()
   .EnableGrpcWeb()
   .RequireCors("AllowAll");
```

### Blazor WASM Client

```csharp
// Program.cs trong Blazor WASM
builder.Services.AddGrpcClient<ProductCatalogService.ProductCatalogServiceClient>(options =>
{
    options.Address = new Uri("https://localhost:5001");
})
.ConfigureChannel(channel =>
{
    // Dùng gRPC-Web handler cho browser
    var httpClient = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()));
    channel.HttpClient = httpClient;
});

// Usage trong Blazor component
@inject ProductCatalogService.ProductCatalogServiceClient ProductClient

@code {
    private List<Product> products = new();
    
    protected override async Task OnInitializedAsync()
    {
        var request = new SearchProductsRequest
        {
            Query = "",
            Page = 1,
            PageSize = 20
        };
        
        var response = await ProductClient.SearchProductsAsync(request);
        products = response.Products.ToList();
    }
    
    // Server streaming trong Blazor
    private async Task WatchProduct(string productId)
    {
        var request = new WatchProductRequest { ProductId = productId };
        
        using var call = ProductClient.WatchProduct(request);
        
        await foreach (var @event in call.ResponseStream.ReadAllAsync())
        {
            // Update UI
            Console.WriteLine($"Received event: {@event.EventType} for product {@event.ProductId}");
            StateHasChanged();
        }
    }
}
```

---

## 9. Performance Optimization

```csharp
// Connection pooling và keepalive
builder.Services.AddGrpcClient<ProductCatalogService.ProductCatalogServiceClient>(options =>
{
    options.Address = new Uri("https://product-service:5001");
})
.ConfigureChannel(channel =>
{
    channel.HttpHandler = new SocketsHttpHandler
    {
        // Reuse HTTP/2 connections
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        
        // Keepalive - ngăn connection bị timeout
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        
        // Multiple HTTP/2 connections
        EnableMultipleHttp2Connections = true
    };
    
    // Compression
    channel.CompressionProviders = new List<ICompressionProvider>
    {
        new GzipCompressionProvider(CompressionLevel.Fastest)
    };
});

// Deadline propagation
public async Task<ProductDto?> GetProductWithDeadlineAsync(Guid id, CancellationToken ct)
{
    // Tự động set deadline từ CancellationToken
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(5)); // Max 5 seconds

    try
    {
        var response = await _client.GetProductAsync(
            new GetProductRequest { Id = id.ToString() },
            cancellationToken: cts.Token);
        
        return MapToDto(response);
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        _logger.LogWarning("GetProduct timed out for {ProductId}", id);
        return null;
    }
}
```

---

## 10. Best Practices

### Error Handling

```csharp
// Structured error details
public static class GrpcErrorExtensions
{
    public static RpcException CreateValidationException(IEnumerable<string> errors)
    {
        var badRequest = new BadRequest();
        foreach (var error in errors)
        {
            badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
            {
                Description = error
            });
        }

        var status = new Google.Rpc.Status
        {
            Code = (int)StatusCode.InvalidArgument,
            Message = "Validation failed"
        };
        status.Details.Add(Any.Pack(badRequest));

        var metadata = new Metadata();
        metadata.Add("grpc-status-details-bin", status.ToByteArray());

        return new RpcException(new Status(StatusCode.InvalidArgument, "Validation failed"), metadata);
    }
}

// Proto definition checklist:
// ✅ Dùng field numbers ổn định, không tái sử dụng
// ✅ Thêm reserved cho fields đã xóa
// ✅ Luôn có default values (proto3)
// ✅ Dùng wrapper types cho nullable fields
// ✅ Document với comments trong proto

// Proto example với best practices:
/*
message UpdateProductRequest {
  string id = 1;
  
  // Use wrapper types for optional fields
  google.protobuf.StringValue name = 2;
  google.protobuf.DoubleValue price = 3;
  
  // Reserved removed fields
  reserved 4, 5;
  reserved "old_field_name";
}
*/
```

### Testing gRPC Services

```csharp
// Unit testing gRPC service
public class ProductCatalogGrpcServiceTests
{
    private readonly Mock<IProductRepository> _repoMock;
    private readonly ProductCatalogGrpcService _service;

    public ProductCatalogGrpcServiceTests()
    {
        _repoMock = new Mock<IProductRepository>();
        _service = new ProductCatalogGrpcService(
            _repoMock.Object,
            Mock.Of<IMapper>(),
            Mock.Of<ILogger<ProductCatalogGrpcService>>(),
            Mock.Of<IProductEventStream>());
    }

    [Fact]
    public async Task GetProduct_WhenExists_ReturnsProduct()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Product { /* ... */ });
        
        var request = new GetProductRequest { Id = productId.ToString() };
        var context = TestServerCallContext.Create();
        
        // Act
        var result = await _service.GetProduct(request, context);
        
        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetProduct_WhenNotFound_ThrowsRpcException()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Product?)null);
        
        var context = TestServerCallContext.Create();
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _service.GetProduct(new GetProductRequest { Id = Guid.NewGuid().ToString() }, context));
        
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }
}
```

---

## Tổng Kết

gRPC là lựa chọn tuyệt vời cho microservice communication nhờ:

| Feature | Lợi ích |
|---------|---------|
| Protocol Buffers | Binary serialization, 3-10x nhỏ hơn JSON |
| HTTP/2 | Multiplexing, header compression, server push |
| Code generation | Type-safe clients/servers từ .proto |
| Streaming | Unary, server, client, bidirectional |
| Language agnostic | Client/server có thể dùng bất kỳ ngôn ngữ nào |

**Khi nào dùng gRPC thay REST:**
- ✅ Service-to-service communication (internal)
- ✅ High-performance, low-latency requirements
- ✅ Cần streaming capabilities
- ✅ Polyglot microservices (Go backend, .NET client...)
- ❌ Public APIs (prefer REST + JSON)
- ❌ Browser clients (trừ khi dùng gRPC-Web)
- ❌ Simple CRUD với ít endpoints
