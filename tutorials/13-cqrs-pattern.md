# CQRS Pattern trong .NET

## Mục Lục
1. [CQRS là gì?](#cqrs-la-gi)
2. [Commands vs Queries](#commands-vs-queries)
3. [MediatR cho CQRS](#mediatr)
4. [Write Model vs Read Model](#write-vs-read)
5. [Validation với FluentValidation](#validation)
6. [Pipeline Behaviors](#pipeline-behaviors)
7. [Complete Sample: Product Management](#complete-sample)
8. [EF Core Write Side](#ef-core-write)
9. [Dapper Read Side](#dapper-read)
10. [Best Practices](#best-practices)

---

## 1. CQRS là gì?

CQRS (Command Query Responsibility Segregation) là pattern tách biệt hoàn toàn operations **thay đổi dữ liệu** (Commands) khỏi operations **đọc dữ liệu** (Queries).

```
CQS Principle (Bertrand Meyer):
"A method should either change state of an object (command) 
 or return a result (query), but not both."

CQRS Architecture:
                    ┌────────────────────────────────────────┐
                    │              Application               │
                    │                                        │
   ┌──────────┐     │   ┌──────────────────────────────┐    │
   │          │─────────►  COMMAND SIDE                 │    │
   │  Client  │     │   │  ┌────────────┐ ┌──────────┐ │    │
   │          │     │   │  │  Command   │ │ Handler  │ │    │
   │          │     │   │  └────────────┘ └────┬─────┘ │    │
   │          │     │   └─────────────────────┼────────┘    │
   │          │     │                         │              │
   │          │     │              ┌──────────▼──────────┐   │
   │          │     │              │   Write Database     │   │
   │          │     │              │   (Normalized)       │   │
   │          │     │              └──────────┬──────────┘   │
   │          │     │                         │              │
   │          │     │              ┌──────────▼──────────┐   │
   │          │     │              │   Read Database      │   │
   │          │     │              │   (Denormalized)     │   │
   │          │     │   ┌──────────┴──────────────────┐  │   │
   │          │─────────►  QUERY SIDE                  │  │   │
   │          │     │   │  ┌────────────┐ ┌──────────┐ │  │   │
   └──────────┘     │   │  │  Query     │ │ Handler  │ │  │   │
                    │   │  └────────────┘ └──────────┘ │  │   │
                    │   └────────────────────────────────┘  │   │
                    └────────────────────────────────────────┘

Lợi ích:
✅ Scale read và write độc lập
✅ Optimize từng side riêng (EF Core writes, Dapper reads)
✅ Đơn giản hóa complex queries
✅ Better separation of concerns
✅ Event sourcing integration tự nhiên

Thách thức:
❌ Eventual consistency (nếu dùng separate databases)
❌ Complexity tăng (nhiều classes hơn)
❌ Over-engineering cho simple CRUD
```

---

## 2. Project Structure

```
ProductManagement/
├── src/
│   └── ProductManagement.API/
│       ├── Program.cs
│       ├── Controllers/
│       │   └── ProductsController.cs
│       ├── Application/
│       │   ├── Commands/
│       │   │   ├── CreateProduct/
│       │   │   │   ├── CreateProductCommand.cs
│       │   │   │   ├── CreateProductCommandHandler.cs
│       │   │   │   └── CreateProductCommandValidator.cs
│       │   │   ├── UpdateProduct/
│       │   │   │   ├── UpdateProductCommand.cs
│       │   │   │   ├── UpdateProductCommandHandler.cs
│       │   │   │   └── UpdateProductCommandValidator.cs
│       │   │   └── DeleteProduct/
│       │   │       ├── DeleteProductCommand.cs
│       │   │       └── DeleteProductCommandHandler.cs
│       │   ├── Queries/
│       │   │   ├── GetProduct/
│       │   │   │   ├── GetProductQuery.cs
│       │   │   │   └── GetProductQueryHandler.cs
│       │   │   └── SearchProducts/
│       │   │       ├── SearchProductsQuery.cs
│       │   │       └── SearchProductsQueryHandler.cs
│       │   ├── Behaviors/
│       │   │   ├── LoggingBehavior.cs
│       │   │   ├── ValidationBehavior.cs
│       │   │   ├── CachingBehavior.cs
│       │   │   └── TransactionBehavior.cs
│       │   └── DTOs/
│       │       └── ProductDto.cs
│       ├── Domain/
│       │   ├── Product.cs
│       │   ├── Category.cs
│       │   └── Events/
│       └── Infrastructure/
│           ├── Write/
│           │   ├── ProductDbContext.cs
│           │   └── Repositories/
│           └── Read/
│               └── ProductReadRepository.cs
```

---

## 3. Packages Setup

```xml
<!-- ProductManagement.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- CQRS -->
    <PackageReference Include="MediatR" Version="12.4.1" />
    <PackageReference Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="11.1.0" />
    
    <!-- Validation -->
    <PackageReference Include="FluentValidation" Version="11.9.2" />
    <PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.9.2" />
    
    <!-- Write Side -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.0" />
    
    <!-- Read Side -->
    <PackageReference Include="Dapper" Version="2.1.35" />
    <PackageReference Include="Npgsql" Version="9.0.2" />
    
    <!-- Caching -->
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="9.0.0" />
    
    <!-- Mapping -->
    <PackageReference Include="AutoMapper" Version="13.0.1" />
    <PackageReference Include="AutoMapper.Extensions.Microsoft.DependencyInjection" Version="12.0.1" />
  </ItemGroup>
</Project>
```

---

## 4. Domain Model

```csharp
// Domain/Product.cs
public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "VND";
    public int StockQuantity { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public bool IsActive { get; private set; }
    public string? ImageUrl { get; private set; }
    public ICollection<ProductTag> Tags { get; private set; } = new List<ProductTag>();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Product() { } // EF Core constructor

    public static Product Create(
        string name,
        string description,
        string sku,
        decimal price,
        int initialStock,
        Guid categoryId,
        Guid createdBy,
        string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required");
        if (price <= 0)
            throw new DomainException("Price must be positive");
        if (initialStock < 0)
            throw new DomainException("Initial stock cannot be negative");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            Sku = sku.ToUpperInvariant(),
            Price = price,
            StockQuantity = initialStock,
            CategoryId = categoryId,
            IsActive = true,
            ImageUrl = imageUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        product._domainEvents.Add(new ProductCreatedEvent(product.Id, name, price, createdBy));
        return product;
    }

    public void Update(
        string name,
        string description,
        decimal price,
        Guid categoryId,
        Guid updatedBy)
    {
        var oldPrice = Price;
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        CategoryId = categoryId;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;

        _domainEvents.Add(new ProductUpdatedEvent(Id, name, price, updatedBy));

        if (oldPrice != price)
        {
            _domainEvents.Add(new ProductPriceChangedEvent(Id, oldPrice, price, updatedBy));
        }
    }

    public void UpdateStock(int newQuantity, Guid updatedBy)
    {
        var oldQuantity = StockQuantity;
        StockQuantity = newQuantity;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;

        _domainEvents.Add(new StockUpdatedEvent(Id, oldQuantity, newQuantity, updatedBy));
    }

    public void Deactivate(Guid deactivatedBy)
    {
        if (!IsActive)
            throw new DomainException("Product is already inactive");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = deactivatedBy;

        _domainEvents.Add(new ProductDeactivatedEvent(Id, deactivatedBy));
    }

    public void AddTag(string tag)
    {
        if (Tags.Any(t => t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            return;
        
        Tags.Add(new ProductTag { ProductId = Id, Name = tag.ToLowerInvariant() });
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class ProductTag
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
}
```

---

## 5. Commands

```csharp
// Commands/CreateProduct/CreateProductCommand.cs
public sealed record CreateProductCommand(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    int InitialStock,
    Guid CategoryId,
    string? ImageUrl,
    IEnumerable<string>? Tags,
    Guid CreatedBy) : IRequest<CreateProductResult>;

public sealed record CreateProductResult(
    Guid Id,
    string Name,
    decimal Price,
    string Status);

// Commands/CreateProduct/CreateProductCommandValidator.cs
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    private readonly IProductReadRepository _readRepository;

    public CreateProductCommandValidator(IProductReadRepository readRepository)
    {
        _readRepository = readRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống")
            .MinimumLength(3).WithMessage("Tên sản phẩm phải có ít nhất 3 ký tự")
            .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU không được để trống")
            .Matches(@"^[A-Z0-9\-]+$").WithMessage("SKU chỉ được chứa chữ cái in hoa, số và dấu gạch ngang")
            .MaximumLength(50)
            .MustAsync(BeUniqueSku).WithMessage("SKU này đã tồn tại trong hệ thống");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Giá sản phẩm phải lớn hơn 0")
            .LessThanOrEqualTo(1_000_000_000).WithMessage("Giá sản phẩm không được vượt quá 1 tỷ VND");

        RuleFor(x => x.InitialStock)
            .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho không được âm")
            .LessThanOrEqualTo(100_000).WithMessage("Số lượng tồn kho không được vượt quá 100,000");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Danh mục sản phẩm là bắt buộc")
            .MustAsync(CategoryExists).WithMessage("Danh mục sản phẩm không tồn tại");

        RuleFor(x => x.Description)
            .MaximumLength(5000).WithMessage("Mô tả không được vượt quá 5000 ký tự");

        RuleFor(x => x.ImageUrl)
            .Must(url => url is null || Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("URL ảnh không hợp lệ")
            .When(x => x.ImageUrl is not null);

        RuleForEach(x => x.Tags)
            .MaximumLength(50).WithMessage("Tag không được vượt quá 50 ký tự")
            .When(x => x.Tags is not null);
    }

    private async Task<bool> BeUniqueSku(string sku, CancellationToken ct)
    {
        return !await _readRepository.SkuExistsAsync(sku, ct);
    }

    private async Task<bool> CategoryExists(Guid categoryId, CancellationToken ct)
    {
        return await _readRepository.CategoryExistsAsync(categoryId, ct);
    }
}

// Commands/CreateProduct/CreateProductCommandHandler.cs
public sealed class CreateProductCommandHandler
    : IRequestHandler<CreateProductCommand, CreateProductResult>
{
    private readonly IProductWriteRepository _writeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(
        IProductWriteRepository writeRepository,
        IUnitOfWork unitOfWork,
        IEventPublisher eventPublisher,
        ILogger<CreateProductCommandHandler> logger)
    {
        _writeRepository = writeRepository;
        _unitOfWork = unitOfWork;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<CreateProductResult> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating product '{Name}' (SKU: {Sku}) for category {CategoryId}",
            command.Name, command.Sku, command.CategoryId);

        var product = Product.Create(
            command.Name,
            command.Description,
            command.Sku,
            command.Price,
            command.InitialStock,
            command.CategoryId,
            command.CreatedBy,
            command.ImageUrl);

        if (command.Tags is not null)
        {
            foreach (var tag in command.Tags)
                product.AddTag(tag);
        }

        await _writeRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Publish domain events
        foreach (var domainEvent in product.DomainEvents)
        {
            await _eventPublisher.PublishAsync(domainEvent, cancellationToken);
        }
        product.ClearDomainEvents();

        _logger.LogInformation("Product {ProductId} created successfully", product.Id);

        return new CreateProductResult(
            product.Id,
            product.Name,
            product.Price,
            "Created");
    }
}

// Commands/UpdateProduct/UpdateProductCommand.cs
public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    int? StockQuantity,
    string? ImageUrl,
    bool? IsActive,
    Guid UpdatedBy) : IRequest<UpdateProductResult>;

public sealed record UpdateProductResult(
    Guid Id,
    string Name,
    decimal Price,
    bool IsActive,
    DateTime UpdatedAt);

// Commands/UpdateProduct/UpdateProductCommandHandler.cs
public sealed class UpdateProductCommandHandler
    : IRequestHandler<UpdateProductCommand, UpdateProductResult>
{
    private readonly IProductWriteRepository _writeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(
        IProductWriteRepository writeRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateProductCommandHandler> logger)
    {
        _writeRepository = writeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<UpdateProductResult> Handle(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await _writeRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new ProductNotFoundException(command.Id);

        product.Update(
            command.Name,
            command.Description,
            command.Price,
            command.CategoryId,
            command.UpdatedBy);

        if (command.StockQuantity.HasValue)
        {
            product.UpdateStock(command.StockQuantity.Value, command.UpdatedBy);
        }

        if (command.IsActive == false && product.IsActive)
        {
            product.Deactivate(command.UpdatedBy);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateProductResult(
            product.Id,
            product.Name,
            product.Price,
            product.IsActive,
            product.UpdatedAt);
    }
}

// Commands/DeleteProduct/DeleteProductCommand.cs
public sealed record DeleteProductCommand(Guid Id, Guid DeletedBy) : IRequest<bool>;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IProductWriteRepository _writeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProductCommandHandler(
        IProductWriteRepository writeRepository,
        IUnitOfWork unitOfWork)
    {
        _writeRepository = writeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteProductCommand command, CancellationToken ct)
    {
        var product = await _writeRepository.GetByIdAsync(command.Id, ct);
        if (product is null) return false;

        // Soft delete - deactivate thay vì xóa
        product.Deactivate(command.DeletedBy);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
```

---

## 6. Queries

```csharp
// Queries/GetProduct/GetProductQuery.cs
public sealed record GetProductQuery(Guid Id) : IRequest<ProductDetailDto?>;

// DTOs/ProductDto.cs
public record ProductDetailDto(
    Guid Id,
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool InStock,
    Guid CategoryId,
    string CategoryName,
    bool IsActive,
    string? ImageUrl,
    IEnumerable<string> Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record ProductSummaryDto(
    Guid Id,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    string CategoryName,
    bool IsActive,
    string? ImageUrl);

// Queries/GetProduct/GetProductQueryHandler.cs
public sealed class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductDetailDto?>
{
    private readonly IProductReadRepository _readRepository;
    private readonly ILogger<GetProductQueryHandler> _logger;

    public GetProductQueryHandler(
        IProductReadRepository readRepository,
        ILogger<GetProductQueryHandler> logger)
    {
        _readRepository = readRepository;
        _logger = logger;
    }

    public async Task<ProductDetailDto?> Handle(GetProductQuery query, CancellationToken ct)
    {
        _logger.LogDebug("Getting product {ProductId}", query.Id);
        return await _readRepository.GetProductDetailAsync(query.Id, ct);
    }
}

// Queries/SearchProducts/SearchProductsQuery.cs
public sealed record SearchProductsQuery(
    string? SearchTerm,
    Guid? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStockOnly,
    IEnumerable<string>? Tags,
    string SortBy = "name",
    string SortOrder = "asc",
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProductSummaryDto>>;

// Queries/SearchProducts/SearchProductsQueryHandler.cs
public sealed class SearchProductsQueryHandler
    : IRequestHandler<SearchProductsQuery, PagedResult<ProductSummaryDto>>
{
    private readonly IProductReadRepository _readRepository;

    public SearchProductsQueryHandler(IProductReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    public async Task<PagedResult<ProductSummaryDto>> Handle(
        SearchProductsQuery query,
        CancellationToken ct)
    {
        var filter = new ProductSearchFilter(
            query.SearchTerm,
            query.CategoryId,
            query.MinPrice,
            query.MaxPrice,
            query.InStockOnly ?? false,
            query.Tags?.ToList(),
            query.SortBy,
            query.SortOrder,
            query.Page,
            Math.Min(query.PageSize, 100));

        return await _readRepository.SearchProductsAsync(filter, ct);
    }
}

public record ProductSearchFilter(
    string? SearchTerm,
    Guid? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool InStockOnly,
    List<string>? Tags,
    string SortBy,
    string SortOrder,
    int Page,
    int PageSize);
```

---

## 7. Pipeline Behaviors

```csharp
// Behaviors/ValidationBehavior.cs
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            _logger.LogWarning(
                "Validation failed for {RequestType}: {Errors}",
                typeof(TRequest).Name,
                string.Join(", ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

            throw new ValidationException(failures);
        }

        return await next();
    }
}

// Behaviors/LoggingBehavior.cs
public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestId = Guid.NewGuid();

        _logger.LogInformation(
            "[{RequestId}] Handling {RequestName}: {@Request}",
            requestId, requestName, request);

        var sw = Stopwatch.StartNew();

        try
        {
            var response = await next();
            sw.Stop();

            _logger.LogInformation(
                "[{RequestId}] {RequestName} handled successfully in {ElapsedMs}ms",
                requestId, requestName, sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[{RequestId}] {RequestName} failed after {ElapsedMs}ms",
                requestId, requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

// Behaviors/CachingBehavior.cs - Cache cho Queries
public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan? CacheDuration { get; }
}

public sealed class CachingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheableQuery
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var cacheKey = request.CacheKey;

        // Try get from cache
        var cachedValue = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cachedValue is not null)
        {
            _logger.LogDebug("Cache HIT for key: {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<TResponse>(cachedValue)!;
        }

        _logger.LogDebug("Cache MISS for key: {CacheKey}", cacheKey);

        // Execute handler
        var response = await next();

        // Store in cache
        var serialized = JsonSerializer.SerializeToUtf8Bytes(response);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = request.CacheDuration ?? TimeSpan.FromMinutes(5)
        };

        await _cache.SetAsync(cacheKey, serialized, options, cancellationToken);

        return response;
    }
}

// Behaviors/TransactionBehavior.cs - Transaction cho Commands
public sealed class TransactionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ITransactionalCommand
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IUnitOfWork unitOfWork, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Beginning transaction for {RequestType}", typeof(TRequest).Name);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Transaction committed for {RequestType}", typeof(TRequest).Name);
            return response;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Transaction rolled back for {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }
}

public interface ITransactionalCommand { }

// Cacheable Query với cache key
public sealed record GetProductQuery(Guid Id) : IRequest<ProductDetailDto?>, ICacheableQuery
{
    public string CacheKey => $"product:{Id}";
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(15);
}
```

---

## 8. EF Core Write Side

```csharp
// Infrastructure/Write/ProductDbContext.cs
public class ProductDbContext : DbContext, IUnitOfWork
{
    private IDbContextTransaction? _currentTransaction;

    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct)
    {
        _currentTransaction = await Database.BeginTransactionAsync(ct);
        return _currentTransaction;
    }

    public bool HasActiveTransaction => _currentTransaction is not null;

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Automatically set timestamps
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditableEntity &&
                        (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                    auditable.CreatedAt = DateTime.UtcNow;
                
                auditable.UpdatedAt = DateTime.UtcNow;
            }
        }

        return await base.SaveChangesAsync(ct);
    }
}

// Entity Configuration
public class ProductEntityConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Sku)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasDatabaseName("ix_products_sku");

        builder.Property(p => p.Price)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Description)
            .HasMaxLength(5000);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Tags)
            .WithOne()
            .HasForeignKey(t => t.ProductId);

        // Concurrency token
        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Ignore domain events (không persist vào DB)
        builder.Ignore(p => p.DomainEvents);
    }
}
```

---

## 9. Dapper Read Side

```csharp
// Infrastructure/Read/ProductReadRepository.cs
public class ProductReadRepository : IProductReadRepository
{
    private readonly IDbConnection _connection;
    private readonly ILogger<ProductReadRepository> _logger;

    public ProductReadRepository(IDbConnection connection, ILogger<ProductReadRepository> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<ProductDetailDto?> GetProductDetailAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT
                p.id,
                p.name,
                p.description,
                p.sku,
                p.price,
                p.currency,
                p.stock_quantity,
                p.stock_quantity > 0 AS in_stock,
                p.category_id,
                c.name AS category_name,
                p.is_active,
                p.image_url,
                p.created_at,
                p.updated_at,
                STRING_AGG(pt.name, ',') AS tags
            FROM products p
            INNER JOIN categories c ON c.id = p.category_id
            LEFT JOIN product_tags pt ON pt.product_id = p.id
            WHERE p.id = @Id
            GROUP BY
                p.id, p.name, p.description, p.sku, p.price, p.currency,
                p.stock_quantity, p.category_id, c.name, p.is_active,
                p.image_url, p.created_at, p.updated_at
            """;

        var result = await _connection.QueryFirstOrDefaultAsync<dynamic>(
            sql,
            new { Id = id });

        if (result is null) return null;

        return new ProductDetailDto(
            result.id,
            result.name,
            result.description,
            result.sku,
            result.price,
            result.currency,
            result.stock_quantity,
            result.in_stock,
            result.category_id,
            result.category_name,
            result.is_active,
            result.image_url,
            result.tags?.Split(',') ?? Array.Empty<string>(),
            result.created_at,
            result.updated_at);
    }

    public async Task<PagedResult<ProductSummaryDto>> SearchProductsAsync(
        ProductSearchFilter filter,
        CancellationToken ct)
    {
        var conditions = new List<string> { "p.is_active = TRUE" };
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            conditions.Add("(p.name ILIKE @SearchTerm OR p.sku ILIKE @SearchTerm OR p.description ILIKE @SearchTerm)");
            parameters.Add("SearchTerm", $"%{filter.SearchTerm}%");
        }

        if (filter.CategoryId.HasValue)
        {
            conditions.Add("p.category_id = @CategoryId");
            parameters.Add("CategoryId", filter.CategoryId);
        }

        if (filter.MinPrice.HasValue)
        {
            conditions.Add("p.price >= @MinPrice");
            parameters.Add("MinPrice", filter.MinPrice);
        }

        if (filter.MaxPrice.HasValue)
        {
            conditions.Add("p.price <= @MaxPrice");
            parameters.Add("MaxPrice", filter.MaxPrice);
        }

        if (filter.InStockOnly)
        {
            conditions.Add("p.stock_quantity > 0");
        }

        if (filter.Tags?.Any() == true)
        {
            conditions.Add("EXISTS (SELECT 1 FROM product_tags pt WHERE pt.product_id = p.id AND pt.name = ANY(@Tags))");
            parameters.Add("Tags", filter.Tags.ToArray());
        }

        var whereClause = string.Join(" AND ", conditions);
        
        var sortColumn = filter.SortBy.ToLower() switch
        {
            "price" => "p.price",
            "name" => "p.name",
            "stock" => "p.stock_quantity",
            "createdat" => "p.created_at",
            _ => "p.name"
        };

        var sortDirection = filter.SortOrder.ToLower() == "desc" ? "DESC" : "ASC";
        var offset = (filter.Page - 1) * filter.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("Limit", filter.PageSize);

        var countSql = $"""
            SELECT COUNT(*)
            FROM products p
            WHERE {whereClause}
            """;

        var dataSql = $"""
            SELECT
                p.id,
                p.name,
                p.sku,
                p.price,
                p.stock_quantity,
                c.name AS category_name,
                p.is_active,
                p.image_url
            FROM products p
            INNER JOIN categories c ON c.id = p.category_id
            WHERE {whereClause}
            ORDER BY {sortColumn} {sortDirection}
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
            """;

        // Execute cả hai queries song song
        var totalCountTask = _connection.QuerySingleAsync<int>(countSql, parameters);
        var productsTask = _connection.QueryAsync<ProductSummaryDto>(dataSql, parameters);

        await Task.WhenAll(totalCountTask, productsTask);

        var totalCount = await totalCountTask;
        var products = await productsTask;

        return new PagedResult<ProductSummaryDto>(
            products.ToList(),
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM products WHERE sku = @Sku)";
        return await _connection.QuerySingleAsync<bool>(sql, new { Sku = sku.ToUpperInvariant() });
    }

    public async Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken ct)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM categories WHERE id = @Id)";
        return await _connection.QuerySingleAsync<bool>(sql, new { Id = categoryId });
    }
}
```

---

## 10. API Controller và Program.cs

```csharp
// Controllers/ProductsController.cs
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly ISender _mediator;

    public ProductsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProductQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<ProductSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] string? q,
        [FromQuery] Guid? categoryId,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] string? tags = null,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tagList = tags?.Split(',').Select(t => t.Trim()).ToList();
        
        var query = new SearchProductsQuery(
            q, categoryId, minPrice, maxPrice, inStockOnly,
            tagList, sortBy, sortOrder, page, Math.Min(pageSize, 100));
        
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ProductManager")]
    [ProducesResponseType(typeof(CreateProductResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var command = new CreateProductCommand(
            request.Name,
            request.Description,
            request.Sku,
            request.Price,
            request.InitialStock,
            request.CategoryId,
            request.ImageUrl,
            request.Tags,
            userId);

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetProduct), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ProductManager")]
    [ProducesResponseType(typeof(UpdateProductResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProduct(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken ct)
    {
        var command = new UpdateProductCommand(
            id,
            request.Name,
            request.Description,
            request.Price,
            request.CategoryId,
            request.StockQuantity,
            request.ImageUrl,
            request.IsActive,
            GetCurrentUserId());

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var deleted = await _mediator.Send(new DeleteProductCommand(id, GetCurrentUserId()), ct);
        return deleted ? NoContent() : NotFound();
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(CreateProductCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
});

// FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(CreateProductCommandValidator).Assembly);

// Write Side - EF Core
builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriteDb")));

builder.Services.AddScoped<IProductWriteRepository, EfCoreProductRepository>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProductDbContext>());

// Read Side - Dapper
builder.Services.AddScoped<IDbConnection>(sp =>
    new NpgsqlConnection(builder.Configuration.GetConnectionString("ReadDb")));

builder.Services.AddScoped<IProductReadRepository, ProductReadRepository>();

// Caching
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis"));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Exception handling
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Best Practices

### 1. Marker Interfaces cho Commands/Queries

```csharp
// Phân biệt Commands và Queries rõ ràng
public interface ICommand<TResult> : IRequest<TResult> { }
public interface IQuery<TResult> : IRequest<TResult> { }

// Commands có thể modify state
public sealed record CreateProductCommand(...) : ICommand<CreateProductResult>;

// Queries chỉ đọc data
public sealed record GetProductQuery(...) : IQuery<ProductDetailDto?>;
```

### 2. Result Pattern thay vì Exceptions

```csharp
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Errors { get; }

    private Result(T value) { IsSuccess = true; Value = value; Errors = Array.Empty<string>(); }
    private Result(string error) { IsSuccess = false; Error = error; Errors = new[] { error }; }
    private Result(IReadOnlyList<string> errors) { IsSuccess = false; Errors = errors; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);
    public static Result<T> Failure(IReadOnlyList<string> errors) => new(errors);
    
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<string?, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error);
}
```

### 3. Consistent Error Handling

```csharp
// Global exception handler cho ValidationException
public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        if (exception is not ValidationException validationEx)
            return false;

        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        
        var errors = validationEx.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        await context.Response.WriteAsJsonAsync(new ValidationProblemDetails(errors), ct);
        return true;
    }
}
```

---

## Tổng Kết

CQRS với MediatR, EF Core và Dapper là combination mạnh mẽ:

```
CQRS Benefits:
Write Side (EF Core)      │  Read Side (Dapper)
─────────────────────────────────────────────────
✅ Rich domain model      │  ✅ Optimized SQL queries
✅ Business rule enforce  │  ✅ Direct mapping to DTOs
✅ Concurrency control    │  ✅ Complex JOIN, aggregations
✅ Audit trail            │  ✅ Performance optimized
✅ Event publishing       │  ✅ Multiple DB support

MediatR Pipeline:
Request → Logging → Validation → Caching → Handler → Response
```

**Khi nào dùng CQRS:**
- ✅ Complex business logic cho write operations
- ✅ Complex queries không fit vào domain model
- ✅ Read/write throughput khác nhau đáng kể
- ✅ Multiple read models từ cùng data
- ❌ Simple CRUD (over-engineering)
- ❌ Team nhỏ, timeline ngắn
