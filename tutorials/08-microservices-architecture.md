# Kiến Trúc Microservices với .NET

## Mục Lục
1. [Monolith vs Microservices](#monolith-vs-microservices)
2. [Domain-Driven Design cho Microservices](#domain-driven-design)
3. [API Contract Design](#api-contract-design)
4. [Inter-service Communication](#inter-service-communication)
5. [Service Registry và Discovery](#service-registry)
6. [Bounded Contexts](#bounded-contexts)
7. [Complete Sample: Order, Product, User Services](#complete-sample)
8. [Resilience với Polly](#resilience-voi-polly)
9. [Best Practices](#best-practices)

---

## 1. Monolith vs Microservices

### Kiến Trúc Monolith

Trong kiến trúc monolith, toàn bộ ứng dụng được đóng gói thành một đơn vị triển khai duy nhất. Tất cả các module (UI, business logic, data access) chạy trong cùng một process.

```
┌─────────────────────────────────────────────────────────┐
│                    MONOLITH APPLICATION                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐  │
│  │  Orders  │  │ Products │  │  Users   │  │Payments│  │
│  │  Module  │  │  Module  │  │  Module  │  │ Module │  │
│  └──────────┘  └──────────┘  └──────────┘  └────────┘  │
│  ┌─────────────────────────────────────────────────────┐ │
│  │              Shared Database (Single DB)            │ │
│  └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Ưu điểm của Monolith:**
- Đơn giản trong phát triển ban đầu
- Dễ debug và test
- Không có network latency giữa các module
- Transaction đơn giản (ACID)
- Deployment đơn giản

**Nhược điểm của Monolith:**
- Khó scale theo từng phần
- Technology lock-in (phải dùng cùng stack)
- Team lớn khó làm việc song song
- Release cycle chậm (phải deploy toàn bộ)
- Lỗi một module có thể ảnh hưởng toàn bộ hệ thống

### Kiến Trúc Microservices

```
┌──────────────────────────────────────────────────────────────────────┐
│                        API GATEWAY                                   │
│                    (YARP / Ocelot)                                   │
└────────────┬─────────────────┬────────────────────┬─────────────────┘
             │                 │                    │
             ▼                 ▼                    ▼
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │  Order Service  │ │ Product Service │ │  User Service   │
    │   :5001         │ │   :5002         │ │   :5003         │
    │                 │ │                 │ │                 │
    │  ┌───────────┐  │ │  ┌───────────┐  │ │  ┌───────────┐  │
    │  │ Orders DB │  │ │  │Products DB│  │ │  │ Users DB  │  │
    │  │(PostgreSQL)│ │ │  │  (MongoDB)│  │ │  │  (MySQL)  │  │
    │  └───────────┘  │ │  └───────────┘  │ │  └───────────┘  │
    └─────────────────┘ └─────────────────┘ └─────────────────┘
             │                 │                    │
             └─────────────────┴────────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │   Message Broker    │
                    │  (RabbitMQ / Kafka) │
                    └─────────────────────┘
```

**Ưu điểm của Microservices:**
- Scale độc lập từng service
- Technology diversity (mỗi service có thể dùng stack khác nhau)
- Team độc lập, deploy độc lập
- Fault isolation (lỗi một service không ảnh hưởng toàn bộ)
- Dễ thay thế từng service

**Nhược điểm của Microservices:**
- Phức tạp về infrastructure
- Distributed system problems (network failures, latency)
- Data consistency khó hơn
- Testing phức tạp hơn
- Cần DevOps mature

---

## 2. Domain-Driven Design cho Microservices

### Ubiquitous Language

Mỗi bounded context có ngôn ngữ riêng. Ví dụ: "Order" trong Sales context khác với "Order" trong Shipping context.

```csharp
// Sales Bounded Context - Order là về việc đặt hàng
namespace ECommerce.Sales.Domain
{
    public class Order
    {
        public Guid Id { get; private set; }
        public CustomerId CustomerId { get; private set; }
        public IReadOnlyList<OrderLine> Lines { get; private set; }
        public Money TotalAmount { get; private set; }
        public OrderStatus Status { get; private set; }
        
        // Sales-specific behaviors
        public void AddLine(ProductId productId, int quantity, Money unitPrice) { }
        public void ApplyDiscount(DiscountCode code) { }
        public void Submit() { }
    }
}

// Shipping Bounded Context - Order là về việc giao hàng
namespace ECommerce.Shipping.Domain
{
    public class ShipmentOrder
    {
        public Guid OrderId { get; private set; }
        public Address DeliveryAddress { get; private set; }
        public IReadOnlyList<ShipmentItem> Items { get; private set; }
        public TrackingNumber TrackingNumber { get; private set; }
        
        // Shipping-specific behaviors
        public void AssignCarrier(CarrierId carrierId) { }
        public void UpdateTrackingNumber(string number) { }
        public void MarkDelivered() { }
    }
}
```

### Value Objects

```csharp
// Value Objects - immutable, equality dựa trên giá trị
public record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0, currency);
    
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add different currencies");
        return new Money(Amount + other.Amount, Currency);
    }
    
    public Money Multiply(int quantity) => new(Amount * quantity, Currency);
    
    public override string ToString() => $"{Amount:N2} {Currency}";
}

public record Address(
    string Street,
    string City,
    string Province,
    string PostalCode,
    string Country)
{
    public bool IsValid() => 
        !string.IsNullOrWhiteSpace(Street) &&
        !string.IsNullOrWhiteSpace(City) &&
        !string.IsNullOrWhiteSpace(Country);
}

public record ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());
    public static ProductId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

### Aggregate Root

```csharp
// Aggregate Root - đơn vị consistency
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();
    
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    protected void AddDomainEvent(IDomainEvent @event)
    {
        _domainEvents.Add(@event);
    }
    
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

public class Order : AggregateRoot
{
    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }

    private Order() { } // For EF Core

    public static Order Create(CustomerId customerId)
    {
        var order = new Order
        {
            Id = OrderId.New(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            TotalAmount = Money.Zero("VND"),
            CreatedAt = DateTime.UtcNow
        };
        
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, customerId));
        return order;
    }

    public void AddLine(ProductId productId, string productName, int quantity, Money unitPrice)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Cannot add lines to non-pending order");
        
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");

        var existingLine = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existingLine != null)
        {
            existingLine.IncreaseQuantity(quantity);
        }
        else
        {
            _lines.Add(OrderLine.Create(productId, productName, quantity, unitPrice));
        }
        
        RecalculateTotal();
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be confirmed");
        
        if (!_lines.Any())
            throw new DomainException("Cannot confirm empty order");
        
        Status = OrderStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
        
        AddDomainEvent(new OrderConfirmedEvent(Id, CustomerId, TotalAmount));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel shipped or delivered order");
        
        Status = OrderStatus.Cancelled;
        AddDomainEvent(new OrderCancelledEvent(Id, CustomerId, reason));
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Aggregate(
            Money.Zero("VND"),
            (total, line) => total.Add(line.SubTotal));
    }
}
```

---

## 3. API Contract Design

### REST API Conventions

```csharp
// Versioning API - quan trọng cho microservices
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    // GET api/v1/orders?page=1&pageSize=10&status=Pending
    [HttpGet]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] OrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetOrdersQuery(page, pageSize, status);
        var result = await _orderService.GetOrdersAsync(query, cancellationToken);
        return Ok(result);
    }

    // GET api/v1/orders/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order is null)
            return NotFound(new ProblemDetails
            {
                Title = "Order not found",
                Detail = $"Order with id {id} does not exist",
                Status = StatusCodes.Status404NotFound
            });
        
        return Ok(order);
    }

    // POST api/v1/orders
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(
            request.CustomerId,
            request.Lines.Select(l => new OrderLineCommand(l.ProductId, l.Quantity)));
        
        var result = await _orderService.CreateOrderAsync(command, cancellationToken);
        
        return CreatedAtAction(
            nameof(GetOrder),
            new { id = result.Id },
            result);
    }

    // PUT api/v1/orders/{id}/confirm
    [HttpPut("{id:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmOrder(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _orderService.ConfirmOrderAsync(id, cancellationToken);
            return NoContent();
        }
        catch (OrderNotFoundException)
        {
            return NotFound();
        }
        catch (DomainException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }
}

// DTOs - Data Transfer Objects cho API contract
public record CreateOrderRequest(
    Guid CustomerId,
    IEnumerable<CreateOrderLineRequest> Lines);

public record CreateOrderLineRequest(
    Guid ProductId,
    int Quantity);

public record OrderDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    IEnumerable<OrderLineDto> Lines,
    decimal TotalAmount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt);

public record OrderLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal SubTotal);

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

---

## 4. Inter-service Communication

### Synchronous Communication với HttpClient

```csharp
// Typed Client cho Product Service
public interface IProductServiceClient
{
    Task<ProductDto?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductDto>> GetProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    Task<bool> ReserveStockAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
    Task ReleaseStockAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
}

public class ProductServiceClient : IProductServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductServiceClient> _logger;

    public ProductServiceClient(HttpClient httpClient, ILogger<ProductServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/v1/products/{productId}", cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<ProductDto>(
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get product {ProductId}", productId);
            throw new ServiceUnavailableException("Product service is unavailable", ex);
        }
    }

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = string.Join(",", productIds);
        var response = await _httpClient.GetAsync($"api/v1/products?ids={ids}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>(
            cancellationToken: cancellationToken) ?? Enumerable.Empty<ProductDto>();
    }

    public async Task<bool> ReserveStockAsync(
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var request = new { ProductId = productId, Quantity = quantity };
        var response = await _httpClient.PostAsJsonAsync(
            "api/v1/inventory/reserve",
            request,
            cancellationToken);
        
        return response.IsSuccessStatusCode;
    }

    public async Task ReleaseStockAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        var request = new { ProductId = productId, Quantity = quantity };
        await _httpClient.PostAsJsonAsync("api/v1/inventory/release", request, cancellationToken);
    }
}

// Registration trong Program.cs
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ProductService:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddTransientHttpErrorPolicy(policy => policy.WaitAndRetryAsync(
    retryCount: 3,
    sleepDurationProvider: retryAttempt => 
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
    onRetry: (outcome, timespan, retryAttempt, context) =>
    {
        var logger = context.GetLogger();
        logger?.LogWarning(
            "Retry {RetryAttempt} for ProductService after {Delay}ms",
            retryAttempt, timespan.TotalMilliseconds);
    }))
.AddCircuitBreakerPolicy(policy => policy.CircuitBreakerAsync(
    handledEventsAllowedBeforeBreaking: 5,
    durationOfBreak: TimeSpan.FromSeconds(30)));
```

### Asynchronous Communication với Message Bus

```csharp
// Event definitions
public record OrderCreatedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    IEnumerable<OrderLineEvent> Lines,
    decimal TotalAmount,
    DateTime CreatedAt);

public record OrderLineEvent(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);

// Publisher - Order Service publishes event
public class OrderEventPublisher : IOrderEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderEventPublisher> _logger;

    public OrderEventPublisher(IPublishEndpoint publishEndpoint, ILogger<OrderEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishOrderCreatedAsync(Order order, CancellationToken cancellationToken)
    {
        var @event = new OrderCreatedIntegrationEvent(
            order.Id.Value,
            order.CustomerId.Value,
            order.Lines.Select(l => new OrderLineEvent(
                l.ProductId.Value,
                l.Quantity,
                l.UnitPrice.Amount)),
            order.TotalAmount.Amount,
            order.CreatedAt);
        
        await _publishEndpoint.Publish(@event, cancellationToken);
        _logger.LogInformation("Published OrderCreated event for order {OrderId}", order.Id);
    }
}

// Consumer - Inventory Service consumes event
public class OrderCreatedConsumer : IConsumer<OrderCreatedIntegrationEvent>
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IInventoryService inventoryService,
        ILogger<OrderCreatedConsumer> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var @event = context.Message;
        _logger.LogInformation("Processing OrderCreated event for order {OrderId}", @event.OrderId);
        
        try
        {
            foreach (var line in @event.Lines)
            {
                await _inventoryService.ReserveStockAsync(
                    line.ProductId,
                    line.Quantity,
                    context.CancellationToken);
            }
            
            _logger.LogInformation("Stock reserved for order {OrderId}", @event.OrderId);
        }
        catch (InsufficientStockException ex)
        {
            _logger.LogWarning(ex, "Insufficient stock for order {OrderId}", @event.OrderId);
            // Publish compensation event
            await context.Publish(new StockReservationFailedEvent(@event.OrderId, ex.Message));
        }
    }
}
```

---

## 5. Service Registry và Discovery

### Consul Service Discovery

```csharp
// Registration với Consul
public static class ConsulExtensions
{
    public static IServiceCollection AddConsul(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IConsulClient>(p => new ConsulClient(config =>
        {
            config.Address = new Uri(configuration["Consul:Address"] ?? "http://localhost:8500");
        }));

        return services;
    }

    public static IApplicationBuilder UseConsul(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime)
    {
        var consulClient = app.ApplicationServices.GetRequiredService<IConsulClient>();
        var logger = app.ApplicationServices.GetRequiredService<ILogger<IApplicationBuilder>>();
        
        var serviceName = configuration["Service:Name"]!;
        var serviceId = $"{serviceName}-{Guid.NewGuid()}";
        var servicePort = int.Parse(configuration["Service:Port"]!);

        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = serviceName,
            Address = configuration["Service:Host"],
            Port = servicePort,
            Check = new AgentServiceCheck
            {
                HTTP = $"http://{configuration["Service:Host"]}:{servicePort}/health",
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1)
            },
            Tags = new[] { "dotnet", "microservice", configuration["Service:Version"] ?? "v1" }
        };

        logger.LogInformation("Registering with Consul as {ServiceId}", serviceId);
        consulClient.Agent.ServiceRegister(registration).Wait();

        lifetime.ApplicationStopping.Register(() =>
        {
            logger.LogInformation("Deregistering from Consul {ServiceId}", serviceId);
            consulClient.Agent.ServiceDeregister(serviceId).Wait();
        });

        return app;
    }
}

// Service Discovery Client
public class ConsulServiceDiscovery : IServiceDiscovery
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulServiceDiscovery> _logger;

    public ConsulServiceDiscovery(
        IConsulClient consulClient,
        ILogger<ConsulServiceDiscovery> logger)
    {
        _consulClient = consulClient;
        _logger = logger;
    }

    public async Task<string?> GetServiceUrlAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var queryResult = await _consulClient.Health.Service(
            serviceName,
            tag: null,
            passingOnly: true,
            cancellationToken: cancellationToken);
        
        var services = queryResult.Response;
        if (!services.Any())
        {
            _logger.LogWarning("No healthy instances found for service {ServiceName}", serviceName);
            return null;
        }
        
        // Round-robin load balancing
        var service = services[Random.Shared.Next(services.Length)];
        return $"http://{service.Service.Address}:{service.Service.Port}";
    }
}
```

---

## 6. Bounded Contexts

```
┌────────────────────────────────────────────────────────────────┐
│                     E-COMMERCE DOMAIN                          │
│                                                                │
│  ┌──────────────────┐        ┌──────────────────────────────┐  │
│  │  SALES CONTEXT   │        │    CATALOG CONTEXT           │  │
│  │                  │        │                              │  │
│  │  Order           │        │  Product                     │  │
│  │  Customer        │◄──────►│  Category                    │  │
│  │  OrderLine       │        │  ProductVariant              │  │
│  │  Discount        │        │  PriceList                   │  │
│  └────────┬─────────┘        └──────────────────────────────┘  │
│           │                                                    │
│           │ events                                             │
│           ▼                                                    │
│  ┌──────────────────┐        ┌──────────────────────────────┐  │
│  │ SHIPPING CONTEXT │        │   PAYMENT CONTEXT            │  │
│  │                  │        │                              │  │
│  │  Shipment        │        │  Invoice                     │  │
│  │  DeliveryRoute   │        │  Transaction                 │  │
│  │  Carrier         │        │  PaymentMethod               │  │
│  │  TrackingEvent   │        │  Refund                      │  │
│  └──────────────────┘        └──────────────────────────────┘  │
└────────────────────────────────────────────────────────────────┘
```

---

## 7. Complete Sample: Order, Product, User Services

### Project Structure

```
ECommerce/
├── src/
│   ├── Services/
│   │   ├── OrderService/
│   │   │   ├── ECommerce.OrderService.API/
│   │   │   ├── ECommerce.OrderService.Application/
│   │   │   ├── ECommerce.OrderService.Domain/
│   │   │   └── ECommerce.OrderService.Infrastructure/
│   │   ├── ProductService/
│   │   │   ├── ECommerce.ProductService.API/
│   │   │   └── ...
│   │   └── UserService/
│   │       ├── ECommerce.UserService.API/
│   │       └── ...
│   └── ApiGateway/
│       └── ECommerce.ApiGateway/
├── docker-compose.yml
└── ECommerce.sln
```

### Product Service Implementation

```csharp
// ProductService Domain
public class Product : AggregateRoot
{
    public ProductId Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public Money Price { get; private set; }
    public int StockQuantity { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Product() { }

    public static Product Create(
        string name,
        string description,
        Money price,
        int initialStock,
        CategoryId categoryId)
    {
        var product = new Product
        {
            Id = ProductId.New(),
            Name = name,
            Description = description,
            Price = price,
            StockQuantity = initialStock,
            CategoryId = categoryId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        
        product.AddDomainEvent(new ProductCreatedEvent(product.Id, name, price));
        return product;
    }

    public void UpdatePrice(Money newPrice)
    {
        if (newPrice.Amount <= 0)
            throw new DomainException("Price must be positive");
        
        var oldPrice = Price;
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, oldPrice, newPrice));
    }

    public void ReserveStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");
        
        if (StockQuantity < quantity)
            throw new InsufficientStockException(Id, quantity, StockQuantity);
        
        StockQuantity -= quantity;
        AddDomainEvent(new StockReservedEvent(Id, quantity, StockQuantity));
    }

    public void RestoreStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");
        
        StockQuantity += quantity;
        AddDomainEvent(new StockRestoredEvent(Id, quantity, StockQuantity));
    }
}

// ProductService API Controller
[ApiController]
[Route("api/v1/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductRepository _repository;
    private readonly IMapper _mapper;

    public ProductsController(IProductRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? category,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var spec = new ProductFilterSpecification(category, minPrice, maxPrice);
        var products = await _repository.GetBySpecificationAsync(spec, page, pageSize, cancellationToken);
        var totalCount = await _repository.CountBySpecificationAsync(spec, cancellationToken);
        
        var dtos = _mapper.Map<IEnumerable<ProductSummaryDto>>(products);
        return Ok(new PagedResult<ProductSummaryDto>(dtos, totalCount, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(ProductId.From(id), cancellationToken);
        if (product is null) return NotFound();
        
        return Ok(_mapper.Map<ProductDto>(product));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = Product.Create(
            request.Name,
            request.Description,
            new Money(request.Price, "VND"),
            request.InitialStock,
            CategoryId.From(request.CategoryId));
        
        await _repository.AddAsync(product, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id.Value },
            _mapper.Map<ProductDto>(product));
    }

    [HttpPost("{id:guid}/reserve")]
    public async Task<IActionResult> ReserveStock(
        Guid id,
        [FromBody] ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(ProductId.From(id), cancellationToken);
        if (product is null) return NotFound();
        
        try
        {
            product.ReserveStock(request.Quantity);
            await _repository.SaveChangesAsync(cancellationToken);
            return Ok(new { Success = true, RemainingStock = product.StockQuantity });
        }
        catch (InsufficientStockException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Insufficient Stock",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }
}
```

### User Service Implementation

```csharp
// User Service Domain
public class User : AggregateRoot
{
    public UserId Id { get; private set; }
    public string Email { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string PasswordHash { get; private set; }
    public IReadOnlyList<Address> Addresses { get; private set; } = new List<Address>();
    public UserRole Role { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    private User() { }

    public static User Register(
        string email,
        string firstName,
        string lastName,
        string passwordHash)
    {
        var user = new User
        {
            Id = UserId.New(),
            Email = email.ToLowerInvariant(),
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = passwordHash,
            Role = UserRole.Customer,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };
        
        user.AddDomainEvent(new UserRegisteredEvent(user.Id, email));
        return user;
    }

    public void VerifyEmail()
    {
        IsEmailVerified = true;
        AddDomainEvent(new EmailVerifiedEvent(Id, Email));
    }

    public void AddAddress(Address address)
    {
        var addresses = (List<Address>)Addresses;
        addresses.Add(address);
    }
}

// Authentication Service
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await _userRepository.GetByEmailAsync(email, ct);
        if (user is null)
            return AuthResult.Failure("Invalid email or password");
        
        if (!_passwordHasher.Verify(password, user.PasswordHash))
            return AuthResult.Failure("Invalid email or password");
        
        if (!user.IsEmailVerified)
            return AuthResult.Failure("Email not verified");
        
        var token = _jwtTokenGenerator.GenerateToken(user);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        
        return AuthResult.Success(token, refreshToken, user);
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var existingUser = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (existingUser is not null)
            return AuthResult.Failure("Email already registered");
        
        var passwordHash = _passwordHasher.Hash(request.Password);
        var user = User.Register(request.Email, request.FirstName, request.LastName, passwordHash);
        
        await _userRepository.AddAsync(user, ct);
        await _userRepository.SaveChangesAsync(ct);
        
        return AuthResult.Success(null, null, user);
    }
}

// JWT Token Generator
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(IOptions<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public string GenerateToken(User user)
    {
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new Claim(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
```

---

## 8. Resilience với Polly

```csharp
// Polly Resilience Strategies
public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddResiliencePolicies(
        this IHttpClientBuilder builder,
        string serviceName)
    {
        // Retry Policy với Exponential Backoff
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)), // Jitter
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogWarning(
                        "[{ServiceName}] Retry {RetryAttempt} after {Delay}ms. Error: {Error}",
                        serviceName, retryAttempt, timespan.TotalMilliseconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });

        // Circuit Breaker Policy
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, timespan) =>
                {
                    Console.WriteLine($"[{serviceName}] Circuit OPEN for {timespan.TotalSeconds}s");
                },
                onReset: () =>
                {
                    Console.WriteLine($"[{serviceName}] Circuit CLOSED - service recovered");
                },
                onHalfOpen: () =>
                {
                    Console.WriteLine($"[{serviceName}] Circuit HALF-OPEN - testing service");
                });

        // Timeout Policy
        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            seconds: 10,
            timeoutStrategy: TimeoutStrategy.Optimistic);

        // Bulkhead Policy - giới hạn concurrent requests
        var bulkheadPolicy = Policy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization: 10,
            maxQueuingActions: 25,
            onBulkheadRejectedAsync: context =>
            {
                Console.WriteLine($"[{serviceName}] Bulkhead rejected request - too many concurrent calls");
                return Task.CompletedTask;
            });

        return builder
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy)
            .AddPolicyHandler(timeoutPolicy)
            .AddPolicyHandler(bulkheadPolicy);
    }
}

// Fallback Pattern
public class ProductServiceClientWithFallback : IProductServiceClient
{
    private readonly IProductServiceClient _inner;
    private readonly IProductCache _cache;
    private readonly ILogger<ProductServiceClientWithFallback> _logger;

    public ProductServiceClientWithFallback(
        IProductServiceClient inner,
        IProductCache cache,
        ILogger<ProductServiceClientWithFallback> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            var product = await _inner.GetProductAsync(productId, ct);
            if (product is not null)
            {
                await _cache.SetAsync(productId, product, TimeSpan.FromMinutes(5));
            }
            return product;
        }
        catch (ServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "ProductService unavailable, using cache fallback for {ProductId}", productId);
            return await _cache.GetAsync(productId);
        }
    }

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        try
        {
            return await _inner.GetProductsAsync(productIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch products, attempting cache fallback");
            var cachedProducts = new List<ProductDto>();
            foreach (var id in productIds)
            {
                var cached = await _cache.GetAsync(id);
                if (cached is not null) cachedProducts.Add(cached);
            }
            return cachedProducts;
        }
    }

    public Task<bool> ReserveStockAsync(Guid productId, int quantity, CancellationToken ct = default)
        => _inner.ReserveStockAsync(productId, quantity, ct);

    public Task ReleaseStockAsync(Guid productId, int quantity, CancellationToken ct = default)
        => _inner.ReleaseStockAsync(productId, quantity, ct);
}
```

---

## 9. Infrastructure Setup

```csharp
// Program.cs cho Order Service
var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

// Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

// MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// AutoMapper
builder.Services.AddAutoMapper(typeof(OrderProfile));

// HTTP Clients with Resilience
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ProductService"]!);
})
.AddResiliencePolicies("ProductService");

builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:UserService"]!);
})
.AddResiliencePolicies("UserService");

// Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    });

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgsql(builder.Configuration.GetConnectionString("OrdersDb")!)
    .AddRabbitMQ(builder.Configuration["RabbitMQ:ConnectionString"]!);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Service API",
        Version = "v1",
        Description = "Microservice để quản lý đơn hàng"
    });
});

var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Database Migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// appsettings.json
/*
{
  "ConnectionStrings": {
    "OrdersDb": "Host=localhost;Database=orders_db;Username=postgres;Password=secret"
  },
  "Services": {
    "ProductService": "http://product-service:5002",
    "UserService": "http://user-service:5003"
  },
  "Auth": {
    "Authority": "http://identity-service:5004",
    "Audience": "order-service"
  },
  "RabbitMQ": {
    "ConnectionString": "amqp://guest:guest@rabbitmq:5672"
  },
  "Consul": {
    "Address": "http://consul:8500"
  }
}
*/
```

---

## 10. Best Practices

### 1. Service Boundaries rõ ràng

```
✅ Đúng: Mỗi service có database riêng, không share schema
❌ Sai: Nhiều services cùng truy cập một database

✅ Đúng: Communicate qua API hoặc Events
❌ Sai: Direct database queries sang service khác
```

### 2. API Versioning

```csharp
// Luôn version APIs để backward compatibility
[ApiVersion("1.0")]
[ApiVersion("2.0", Deprecated = true)]
[Route("api/v{version:apiVersion}/[controller]")]
```

### 3. Idempotency Keys

```csharp
// Prevent duplicate operations với Idempotency Keys
[HttpPost]
public async Task<IActionResult> CreateOrder(
    [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
    [FromBody] CreateOrderRequest request)
{
    if (idempotencyKey is not null)
    {
        var cached = await _idempotencyCache.GetAsync(idempotencyKey);
        if (cached is not null) return Ok(cached);
    }
    
    var result = await _orderService.CreateOrderAsync(request);
    
    if (idempotencyKey is not null)
    {
        await _idempotencyCache.SetAsync(idempotencyKey, result, TimeSpan.FromHours(24));
    }
    
    return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
}
```

### 4. Health Checks đầy đủ

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddNpgsql(connectionString, name: "database")
    .AddRabbitMQ(rabbitConnectionString, name: "message-broker")
    .AddUrlGroup(new Uri("http://product-service/health"), name: "product-service")
    .AddUrlGroup(new Uri("http://user-service/health"), name: "user-service");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Chỉ check service is alive, không check dependencies
});
```

### 5. Distributed Tracing

```csharp
// OpenTelemetry cho distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("OrderService")
        .AddJaegerExporter(options =>
        {
            options.AgentHost = builder.Configuration["Jaeger:Host"]!;
            options.AgentPort = 6831;
        }));

// Sử dụng trong code
public class OrderService
{
    private static readonly ActivitySource ActivitySource = new("OrderService");
    
    public async Task<OrderDto> CreateOrderAsync(CreateOrderCommand command, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("CreateOrder");
        activity?.SetTag("customer.id", command.CustomerId.ToString());
        activity?.SetTag("order.lines.count", command.Lines.Count().ToString());
        
        // Business logic...
        
        activity?.SetTag("order.id", order.Id.Value.ToString());
        return result;
    }
}
```

### 6. Structured Logging

```csharp
// Serilog với structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "OrderService")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://seq:5341")
    .CreateLogger();

// Sử dụng log properties
_logger.LogInformation(
    "Order {OrderId} created for customer {CustomerId} with {LineCount} lines, total {TotalAmount}",
    order.Id, order.CustomerId, order.Lines.Count, order.TotalAmount);
```

---

## Tổng Kết

Kiến trúc Microservices mang lại nhiều lợi ích nhưng cũng đi kèm với độ phức tạp đáng kể:

| Khía cạnh | Monolith | Microservices |
|-----------|----------|---------------|
| Development Speed | Fast initially | Slower initially, faster long-term |
| Scalability | Scale toàn bộ | Scale từng service |
| Deployment | Simple | Complex (K8s, CI/CD) |
| Data consistency | ACID transactions | Eventual consistency |
| Team structure | Shared codebase | Autonomous teams |
| Fault isolation | Low | High |
| Tech diversity | Limited | Full freedom |

**Khi nào nên dùng Microservices:**
- Team lớn (>20 developers)
- Domain phức tạp với nhiều bounded contexts
- Yêu cầu scale độc lập từng phần
- Yêu cầu deploy frequency cao
- Đã có kinh nghiệm với distributed systems

**Bắt đầu với Monolith nếu:**
- Team nhỏ hoặc startup
- Domain chưa rõ ràng
- Cần iterate nhanh
- Chưa có kinh nghiệm DevOps tốt
