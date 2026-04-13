# Hot Chocolate chuyên sâu cho .NET: GraphQL server thực chiến, schema design, resolver, DataLoader, filtering, pagination, federation và subscription

## Hot Chocolate là gì và vì sao nó quan trọng?

Hot Chocolate là thư viện GraphQL hàng đầu trong hệ .NET, được phát triển bởi ChilliCream. Nếu bạn làm ASP.NET Core và muốn xây GraphQL server nghiêm túc, có typing mạnh, schema-first lẫn code-first linh hoạt, middleware phong phú, DataLoader, federation, filtering, sorting, projection, authorization, persisted operation, subscription và tooling tốt, Hot Chocolate gần như luôn nằm trong top lựa chọn.

Điểm hấp dẫn của Hot Chocolate là nó không chỉ “cho bạn một endpoint GraphQL”. Nó cho bạn cả một platform để thiết kế API read-centric phức tạp, tổng hợp dữ liệu từ nhiều nguồn, tối ưu N+1 query, kiểm soát schema evolution, enforce authorization theo field, và kết nối rất tốt với ASP.NET Core cùng Entity Framework Core.

Nhiều người mới nhìn GraphQL thường nghĩ đơn giản: client tự chọn field, server trả đúng field đó, thế là xong. Nhưng khi hệ thống lớn hơn, bạn sẽ gặp ngay các bài toán thật:

- làm sao tránh N+1 query khi query danh sách rồi lồng nhiều quan hệ
- làm sao schema dễ evolve mà không phá client cũ
- làm sao phân tách query, mutation, subscription hợp lý
- làm sao tối ưu truy vấn với EF Core mà không load thừa dữ liệu
- làm sao map authorization rule theo field hoặc type
- làm sao tổng hợp dữ liệu từ nhiều microservice nhưng vẫn có API thống nhất

Hot Chocolate sinh ra đúng để giải nhóm vấn đề đó trong thế giới .NET.

---

## GraphQL và Hot Chocolate nên được nhìn như thế nào?

GraphQL không phải là “REST nhưng gộp lại thành một endpoint”. Nó là một query language cho API, kèm runtime để execute query theo schema kiểu strongly typed.

Client gửi một query như sau:

```graphql
query {
  productById(id: "p-100") {
    id
    name
    price
    category {
      id
      name
    }
    reviews {
      rating
      comment
      author {
        displayName
      }
    }
  }
}
```

Server trả đúng cấu trúc client yêu cầu:

```json
{
  "data": {
    "productById": {
      "id": "p-100",
      "name": "Mechanical Keyboard",
      "price": 120,
      "category": {
        "id": "c-1",
        "name": "Accessories"
      },
      "reviews": [
        {
          "rating": 5,
          "comment": "Great keyboard",
          "author": {
            "displayName": "Viet"
          }
        }
      ]
    }
  }
}
```

Với Hot Chocolate, bạn định nghĩa schema và resolver trong .NET, thường theo code-first, rồi framework lo parse query, validate, execute, compose result và apply middleware pipeline.

---

## Khi nào nên dùng Hot Chocolate?

### Nên dùng khi

1. Bạn cần read API giàu khả năng query, có nhiều màn hình khác nhau với nhu cầu dữ liệu khác nhau.
2. Frontend cần tự chọn field để tránh over-fetching và under-fetching.
3. Bạn phải tổng hợp dữ liệu từ nhiều nguồn: DB, microservice, cache, search index.
4. Bạn muốn schema typed rõ ràng, dễ introspect, tài liệu hóa tốt.
5. Bạn cần subscription real-time cho một số luồng dữ liệu.
6. Bạn muốn một GraphQL stack trưởng thành trong .NET, không phải tự ráp từng phần.

### Không nên dùng khi

1. API đơn giản CRUD ít biến thể, REST là đủ và rẻ hơn.
2. Team chưa có discipline về schema design và performance, dễ biến GraphQL thành “DB over HTTP”.
3. Hệ thống write-heavy nhưng read model nghèo nàn, GraphQL không mang thêm nhiều giá trị.
4. Bạn cần tận dụng sâu semantics HTTP cache/status code/resource-oriented contract của REST.

GraphQL không phải luôn tốt hơn REST. Nó mạnh khi bài toán query composition thật sự tồn tại.

---

## Kiến trúc demo trong bài

Ta sẽ xây một GraphQL server cho mini-commerce gồm:

- `Product`
- `Category`
- `Review`
- `User`
- `Order`

Dữ liệu đến từ nhiều nguồn:

- EF Core + PostgreSQL cho product, category, order
- service giả lập cho review
- service giả lập cho user profile
- subscription khi order được tạo

Ta sẽ đi qua các phần quan trọng nhất của Hot Chocolate:

1. setup server
2. schema bằng code-first
3. query và mutation
4. resolver cho relation
5. DataLoader chống N+1
6. filtering, sorting, projection, paging
7. input type, validation, error handling
8. authorization
9. subscription
10. stitching/federation và pattern BFF
11. performance, persisted query, best practices

---

## Cài đặt project Hot Chocolate cơ bản

Tạo project:

```bash
dotnet new web -n CommerceGraphQL
cd CommerceGraphQL
```

Cài package:

```bash
dotnet add package HotChocolate.AspNetCore
dotnet add package HotChocolate.Data.EntityFramework
dotnet add package HotChocolate.Subscriptions
dotnet add package HotChocolate.Subscriptions.InMemory
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

Nếu bạn dùng PostgreSQL thực, thay bằng provider phù hợp. Để tutorial dễ chạy, ta dùng EF Core InMemory trước.

`Program.cs` ban đầu:

```csharp
using CommerceGraphQL.Data;
using CommerceGraphQL.GraphQL;
using CommerceGraphQL.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("commerce-db"));

builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<UserProfileService>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddSubscriptionType<Subscription>()
    .AddType<ProductType>()
    .AddType<OrderType>()
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .AddInMemorySubscriptions();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    SeedData.Initialize(db);
}

app.UseWebSockets();
app.MapGraphQL();
app.Run();
```

Chỉ vài dòng đã có một GraphQL endpoint mặc định là `/graphql`.

---

## Domain model và DbContext

### Entity models

```csharp
namespace CommerceGraphQL.Data;

public class Product
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public decimal Price { get; set; }
    public string CategoryId { get; set; } = default!;
    public Category Category { get; set; } = default!;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class Category
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public List<Product> Products { get; set; } = new();
}

public class Order
{
    public string Id { get; set; } = default!;
    public string CustomerEmail { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public string OrderId { get; set; } = default!;
    public Order Order { get; set; } = default!;
    public string ProductId { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Review
{
    public string Id { get; set; } = default!;
    public string ProductId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public int Rating { get; set; }
    public string Comment { get; set; } = default!;
}

public class UserProfile
{
    public string Id { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
}
```

### DbContext

```csharp
using Microsoft.EntityFrameworkCore;

namespace CommerceGraphQL.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasKey(x => x.Id);
        modelBuilder.Entity<Category>().HasKey(x => x.Id);
        modelBuilder.Entity<Order>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderItem>().HasKey(x => x.Id);

        modelBuilder.Entity<Product>()
            .HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId);

        modelBuilder.Entity<OrderItem>()
            .HasOne(x => x.Order)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId);
    }
}
```

### Seed data

```csharp
namespace CommerceGraphQL.Data;

public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        if (db.Products.Any()) return;

        var accessories = new Category { Id = "c-1", Name = "Accessories" };
        var monitors = new Category { Id = "c-2", Name = "Monitors" };

        db.Categories.AddRange(accessories, monitors);

        db.Products.AddRange(
            new Product
            {
                Id = "p-100",
                Name = "Mechanical Keyboard",
                Slug = "mechanical-keyboard",
                Price = 120m,
                CategoryId = accessories.Id,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
            },
            new Product
            {
                Id = "p-200",
                Name = "4K Monitor",
                Slug = "4k-monitor",
                Price = 480m,
                CategoryId = monitors.Id,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-7)
            },
            new Product
            {
                Id = "p-300",
                Name = "USB-C Dock",
                Slug = "usb-c-dock",
                Price = 90m,
                CategoryId = accessories.Id,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
            });

        db.SaveChanges();
    }
}
```

---

## Query type cơ bản

Hot Chocolate code-first rất tự nhiên. Bạn tạo một class `Query`, mỗi method hoặc property là một field GraphQL.

```csharp
using CommerceGraphQL.Data;
using Microsoft.EntityFrameworkCore;

namespace CommerceGraphQL.GraphQL;

public class Query
{
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Product> GetProducts(AppDbContext db) =>
        db.Products.AsNoTracking();

    public Task<Product?> GetProductByIdAsync(string id, AppDbContext db, CancellationToken ct) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    [UseProjection]
    public IQueryable<Category> GetCategories(AppDbContext db) =>
        db.Categories.AsNoTracking();

    [UseProjection]
    public IQueryable<Order> GetOrders(AppDbContext db) =>
        db.Orders.AsNoTracking();
}
```

Chạy app và mở Banana Cake Pop hoặc bất kỳ GraphQL IDE nào, bạn có thể query:

```graphql
query {
  products {
    id
    name
    price
  }
}
```

Kết quả:

```json
{
  "data": {
    "products": [
      { "id": "p-100", "name": "Mechanical Keyboard", "price": 120 },
      { "id": "p-200", "name": "4K Monitor", "price": 480 },
      { "id": "p-300", "name": "USB-C Dock", "price": 90 }
    ]
  }
}
```

### Query có argument

```graphql
query {
  productById(id: "p-100") {
    id
    name
    slug
    price
  }
}
```

Hot Chocolate tự map method parameter thành GraphQL argument. Đó là một lý do code-first ở nó rất mượt.

---

## Mutation: ghi dữ liệu có chủ đích

Mutation trong GraphQL không chỉ là “POST thay vì GET”. Nó là entry point cho thay đổi state có semantic rõ ràng.

### Input model

```csharp
namespace CommerceGraphQL.GraphQL;

public record CreateOrderItemInput(string ProductId, int Quantity);
public record CreateOrderInput(string CustomerEmail, IReadOnlyList<CreateOrderItemInput> Items);

public record CreateOrderPayload(string OrderId, string Status, decimal TotalAmount);
```

### Mutation implementation

```csharp
using CommerceGraphQL.Data;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace CommerceGraphQL.GraphQL;

public class Mutation
{
    public async Task<CreateOrderPayload> CreateOrderAsync(
        CreateOrderInput input,
        AppDbContext db,
        ITopicEventSender eventSender,
        CancellationToken ct)
    {
        var productIds = input.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (products.Count != productIds.Count)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("One or more products were not found.")
                .SetCode("PRODUCT_NOT_FOUND")
                .Build());
        }

        var order = new Order
        {
            Id = Guid.NewGuid().ToString("N"),
            CustomerEmail = input.CustomerEmail,
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var item in input.Items)
        {
            var product = products[item.ProductId];
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price
            });
        }

        order.TotalAmount = order.Items.Sum(x => x.UnitPrice * x.Quantity);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var payload = new CreateOrderPayload(order.Id, order.Status, order.TotalAmount);
        await eventSender.SendAsync(nameof(Subscription.OnOrderCreated), payload, ct);

        return payload;
    }
}
```

Query thử mutation:

```graphql
mutation {
  createOrder(
    input: {
      customerEmail: "alice@example.com"
      items: [
        { productId: "p-100", quantity: 2 }
        { productId: "p-300", quantity: 1 }
      ]
    }
  ) {
    orderId
    status
    totalAmount
  }
}
```

### Tư duy thiết kế mutation tốt

- đặt tên theo hành động nghiệp vụ, không phải CRUD mù quáng
- input tách riêng khỏi entity DB
- payload trả về thứ client thật sự cần
- error code rõ ràng để frontend xử lý

Ví dụ tốt:

- `createOrder`
- `cancelOrder`
- `markOrderAsPaid`
- `assignCategoryToProduct`

Thường tốt hơn kiểu generic “updateEverything”.

---

## Type configuration: khi cần schema rõ ràng hơn code mặc định

Hot Chocolate có thể infer nhiều thứ từ model C#, nhưng khi schema quan trọng, bạn nên chủ động config type.

### ProductType

```csharp
using CommerceGraphQL.Data;

namespace CommerceGraphQL.GraphQL;

public class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        descriptor.Description("A sellable product in the commerce catalog.");

        descriptor.Field(x => x.Id).Type<NonNullType<IdType>>();
        descriptor.Field(x => x.Name).Type<NonNullType<StringType>>();
        descriptor.Field(x => x.Price).Type<NonNullType<DecimalType>>();

        descriptor.Field("isExpensive")
            .Type<NonNullType<BooleanType>>()
            .Resolve(ctx => ctx.Parent<Product>().Price >= 300m);

        descriptor.Field("reviews")
            .ResolveWith<ProductResolvers>(x => x.GetReviewsAsync(default!, default!, default))
            .Type<NonNullType<ListType<NonNullType<ReviewType>>>>();
    }
}
```

Resolver cho field reviews:

```csharp
using CommerceGraphQL.Data;
using CommerceGraphQL.Services;

namespace CommerceGraphQL.GraphQL;

public class ProductResolvers
{
    public Task<IReadOnlyList<Review>> GetReviewsAsync(
        [Parent] Product product,
        ReviewService reviewService,
        CancellationToken ct) =>
        reviewService.GetReviewsByProductIdAsync(product.Id, ct);
}
```

### ReviewType và nested author

```csharp
using CommerceGraphQL.Data;

namespace CommerceGraphQL.GraphQL;

public class ReviewType : ObjectType<Review>
{
    protected override void Configure(IObjectTypeDescriptor<Review> descriptor)
    {
        descriptor.Field(x => x.Id).Type<NonNullType<IdType>>();
        descriptor.Field(x => x.Comment).Type<NonNullType<StringType>>();
        descriptor.Field(x => x.Rating).Type<NonNullType<IntType>>();

        descriptor.Field("author")
            .ResolveWith<ReviewResolvers>(x => x.GetAuthorAsync(default!, default!, default))
            .Type<NonNullType<UserProfileType>>();
    }
}

public class UserProfileType : ObjectType<UserProfile>
{
    protected override void Configure(IObjectTypeDescriptor<UserProfile> descriptor)
    {
        descriptor.Field(x => x.Id).Type<NonNullType<IdType>>();
        descriptor.Field(x => x.DisplayName).Type<NonNullType<StringType>>();
    }
}
```

Resolver tác giả:

```csharp
using CommerceGraphQL.Data;
using CommerceGraphQL.Services;

namespace CommerceGraphQL.GraphQL;

public class ReviewResolvers
{
    public Task<UserProfile> GetAuthorAsync(
        [Parent] Review review,
        UserProfileService userProfileService,
        CancellationToken ct) =>
        userProfileService.GetUserByIdAsync(review.UserId, ct);
}
```

Type configuration rõ ràng giúp schema ổn định hơn và team lớn dễ đọc hơn.

---

## N+1 problem và DataLoader

Đây là phần bắt buộc phải hiểu nếu muốn dùng GraphQL nghiêm túc.

### N+1 là gì?

Giả sử query:

```graphql
query {
  products {
    id
    name
    reviews {
      id
      rating
      author {
        id
        displayName
      }
    }
  }
}
```

Nếu resolver `reviews` tự gọi database/service một lần cho mỗi product, và resolver `author` lại gọi thêm một lần cho mỗi review, số lượng query/network call sẽ bùng nổ. Đó là N+1.

Hot Chocolate giải quyết rất tốt bằng DataLoader.

### ReviewService giả lập

```csharp
using CommerceGraphQL.Data;

namespace CommerceGraphQL.Services;

public class ReviewService
{
    private static readonly List<Review> Reviews = new()
    {
        new() { Id = "r-1", ProductId = "p-100", UserId = "u-1", Rating = 5, Comment = "Excellent typing feel" },
        new() { Id = "r-2", ProductId = "p-100", UserId = "u-2", Rating = 4, Comment = "Solid build quality" },
        new() { Id = "r-3", ProductId = "p-200", UserId = "u-1", Rating = 5, Comment = "Great panel" }
    };

    public Task<IReadOnlyList<Review>> GetReviewsByProductIdAsync(string productId, CancellationToken ct)
    {
        var result = Reviews.Where(x => x.ProductId == productId).ToList();
        return Task.FromResult<IReadOnlyList<Review>>(result);
    }

    public Task<ILookup<string, Review>> GetReviewsByProductIdsAsync(IReadOnlyList<string> productIds, CancellationToken ct)
    {
        var result = Reviews.Where(x => productIds.Contains(x.ProductId)).ToLookup(x => x.ProductId);
        return Task.FromResult(result);
    }
}
```

### DataLoader cho reviews theo product

```csharp
using CommerceGraphQL.Data;
using CommerceGraphQL.Services;
using GreenDonut;

namespace CommerceGraphQL.GraphQL;

public class ReviewsByProductIdDataLoader : BatchDataLoader<string, IReadOnlyList<Review>>
{
    private readonly ReviewService _reviewService;

    public ReviewsByProductIdDataLoader(
        IBatchScheduler batchScheduler,
        ReviewService reviewService,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options)
    {
        _reviewService = reviewService;
    }

    protected override async Task<IReadOnlyDictionary<string, IReadOnlyList<Review>>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var lookup = await _reviewService.GetReviewsByProductIdsAsync(keys, cancellationToken);
        return keys.ToDictionary(k => k, k => (IReadOnlyList<Review>)lookup[k].ToList());
    }
}
```

Resolver dùng DataLoader:

```csharp
public class ProductResolvers
{
    public async Task<IReadOnlyList<Review>> GetReviewsAsync(
        [Parent] Product product,
        ReviewsByProductIdDataLoader dataLoader,
        CancellationToken ct)
    {
        return await dataLoader.LoadAsync(product.Id, ct);
    }
}
```

### DataLoader cho user profile

```csharp
using CommerceGraphQL.Data;

namespace CommerceGraphQL.Services;

public class UserProfileService
{
    private static readonly List<UserProfile> Users = new()
    {
        new() { Id = "u-1", DisplayName = "Viet" },
        new() { Id = "u-2", DisplayName = "Anh" },
        new() { Id = "u-3", DisplayName = "Linh" }
    };

    public Task<UserProfile> GetUserByIdAsync(string id, CancellationToken ct)
    {
        return Task.FromResult(Users.First(x => x.Id == id));
    }

    public Task<IReadOnlyDictionary<string, UserProfile>> GetUsersByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var result = Users.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id, x => x);
        return Task.FromResult<IReadOnlyDictionary<string, UserProfile>>(result);
    }
}
```

```csharp
using CommerceGraphQL.Data;
using CommerceGraphQL.Services;
using GreenDonut;

namespace CommerceGraphQL.GraphQL;

public class UserByIdDataLoader : BatchDataLoader<string, UserProfile>
{
    private readonly UserProfileService _userProfileService;

    public UserByIdDataLoader(
        IBatchScheduler batchScheduler,
        UserProfileService userProfileService,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options)
    {
        _userProfileService = userProfileService;
    }

    protected override Task<IReadOnlyDictionary<string, UserProfile>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken) =>
        _userProfileService.GetUsersByIdsAsync(keys, cancellationToken);
}
```

Resolver author:

```csharp
public class ReviewResolvers
{
    public Task<UserProfile> GetAuthorAsync(
        [Parent] Review review,
        UserByIdDataLoader userByIdDataLoader,
        CancellationToken ct) =>
        userByIdDataLoader.LoadAsync(review.UserId, ct);
}
```

Đây là khác biệt giữa demo GraphQL cho vui và GraphQL production-ready. Không hiểu DataLoader thì rất dễ tự bắn vào chân.

---

## Filtering, sorting, projection, paging

Hot Chocolate có bộ middleware data rất mạnh. Với EF Core, chỉ vài attribute là bạn có query linh hoạt.

### Query có filtering và sorting

```csharp
public class Query
{
    [UsePaging(IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Product> GetProducts(AppDbContext db) =>
        db.Products.AsNoTracking();
}
```

Query ví dụ:

```graphql
query {
  products(
    where: { price: { gte: 100 }, isActive: { eq: true } }
    order: { price: DESC }
    first: 2
  ) {
    totalCount
    nodes {
      id
      name
      price
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
```

### Projection giúp gì?

Nếu client chỉ yêu cầu `id` và `name`, middleware projection có thể translate sang SQL chỉ chọn cột cần thiết, thay vì load cả entity. Điều này rất quan trọng cho performance.

### Paging kiểu cursor

GraphQL cộng đồng chuộng cursor-based paging hơn offset paging vì ổn định hơn trong dữ liệu thay đổi liên tục.

Ví dụ response:

```json
{
  "data": {
    "products": {
      "totalCount": 3,
      "nodes": [
        { "id": "p-200", "name": "4K Monitor", "price": 480 },
        { "id": "p-100", "name": "Mechanical Keyboard", "price": 120 }
      ],
      "pageInfo": {
        "hasNextPage": true,
        "endCursor": "Mg=="
      }
    }
  }
}
```

### Lưu ý thực chiến

- không nên mở filter/sort trên mọi field vô điều kiện
- với field nhạy cảm hoặc query nặng, custom filter cẩn thận
- kiểm tra SQL generated khi dùng EF Core projection
- paging là bắt buộc cho collection lớn, đừng trả list vô hạn

---

## Resolver composition và pattern query aggregation

Một trong những lý do mạnh nhất để dùng GraphQL là tổng hợp dữ liệu từ nhiều nơi thành một schema thống nhất.

Ví dụ `Product` lấy từ DB, `reviews` lấy từ service A, `inventory` lấy từ service B.

```csharp
public class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        descriptor.Field("inventory")
            .Type<NonNullType<IntType>>()
            .ResolveWith<ProductResolvers>(x => x.GetInventoryAsync(default!, default!, default));
    }
}

public class InventoryService
{
    public Task<int> GetAvailableStockAsync(string productId, CancellationToken ct)
    {
        var inventory = productId switch
        {
            "p-100" => 15,
            "p-200" => 4,
            _ => 30
        };

        return Task.FromResult(inventory);
    }
}

public partial class ProductResolvers
{
    public Task<int> GetInventoryAsync(
        [Parent] Product product,
        InventoryService inventoryService,
        CancellationToken ct) =>
        inventoryService.GetAvailableStockAsync(product.Id, ct);
}
```

Query:

```graphql
query {
  products {
    id
    name
    price
    inventory
    reviews {
      rating
      comment
    }
  }
}
```

Hot Chocolate cho bạn BFF pattern rất tự nhiên, đặc biệt khi frontend cần nhiều shape dữ liệu khác nhau mà không muốn gọi 5 API REST.

---

## Input types, validation và business rule

GraphQL schema đẹp không có nghĩa business rule tự động đúng. Bạn cần validate input cẩn thận.

### Validate thủ công trong mutation

```csharp
public async Task<CreateOrderPayload> CreateOrderAsync(
    CreateOrderInput input,
    AppDbContext db,
    ITopicEventSender eventSender,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(input.CustomerEmail))
    {
        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage("Customer email is required.")
            .SetCode("EMAIL_REQUIRED")
            .Build());
    }

    if (input.Items.Count == 0)
    {
        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage("At least one order item is required.")
            .SetCode("ORDER_ITEMS_REQUIRED")
            .Build());
    }

    if (input.Items.Any(x => x.Quantity <= 0))
    {
        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage("Quantity must be greater than zero.")
            .SetCode("INVALID_QUANTITY")
            .Build());
    }

    // continue actual logic
}
```

### Error shape

GraphQL error chuẩn thường như sau:

```json
{
  "errors": [
    {
      "message": "Quantity must be greater than zero.",
      "extensions": {
        "code": "INVALID_QUANTITY"
      }
    }
  ]
}
```

Điểm hay là frontend có thể bám vào `extensions.code` để xử lý ổn định hơn là parse message.

### Một kinh nghiệm quan trọng

Đừng nhét mọi validation vào GraphQL layer. Validation liên quan schema và input shape có thể nằm ở mutation/resolver, nhưng invariant nghiệp vụ quan trọng vẫn nên ở application/domain layer để tránh bị bypass khi app có nhiều entry point khác.

---

## Authorization

GraphQL thường có nhiều field nhạy cảm hơn bạn tưởng. Ví dụ `customerEmail`, `internalCost`, `supplierContract`. Hot Chocolate hỗ trợ authorization rất tốt.

### Cấu hình auth

```csharp
builder.Services
    .AddAuthorization()
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();
```

Giả sử app có JWT bearer như ASP.NET Core bình thường, bạn chỉ cần dùng policy.

### Bảo vệ field hoặc resolver

```csharp
using HotChocolate.Authorization;

public class Query
{
    [Authorize]
    [UseProjection]
    public IQueryable<Order> GetOrders(AppDbContext db) => db.Orders.AsNoTracking();

    [Authorize(Policy = "AdminOnly")]
    public Task<decimal> GetRevenueAsync(AppDbContext db, CancellationToken ct) =>
        db.Orders.SumAsync(x => x.TotalAmount, ct);
}
```

Trong type config:

```csharp
public class OrderType : ObjectType<Order>
{
    protected override void Configure(IObjectTypeDescriptor<Order> descriptor)
    {
        descriptor.Field(x => x.CustomerEmail)
            .Authorize("AdminOnly");
    }
}
```

### Điều cần nhớ

Trong GraphQL, không chỉ query root mới cần auth. Từng field nested cũng có thể rò dữ liệu. Một schema đẹp nhưng auth sơ sài là tai nạn chờ sẵn.

---

## Subscription và realtime

Hot Chocolate hỗ trợ subscription qua WebSocket rất tốt. Đây là nơi GraphQL trở nên hấp dẫn cho dashboard, order status, chat, notification stream.

### Subscription type

```csharp
using HotChocolate.Subscriptions;

namespace CommerceGraphQL.GraphQL;

public class Subscription
{
    [Subscribe]
    [Topic]
    public CreateOrderPayload OnOrderCreated([EventMessage] CreateOrderPayload payload) => payload;
}
```

Do mutation `CreateOrderAsync` đã gọi:

```csharp
await eventSender.SendAsync(nameof(Subscription.OnOrderCreated), payload, ct);
```

Client subscribe:

```graphql
subscription {
  onOrderCreated {
    orderId
    status
    totalAmount
  }
}
```

Khi có order mới, client nhận event ngay.

### Use case thực tế

- dashboard admin theo dõi order mới
- client theo dõi trạng thái xử lý thanh toán
- system monitoring feed
- collaborative UI

### Lưu ý production

- quản lý connection lifecycle
- auth cho subscription
- chọn backend subscription phù hợp, không phải lúc nào in-memory cũng đủ
- cân nhắc scale-out WebSocket hạ tầng

---

## Schema design: cách làm API dễ sống lâu

Hot Chocolate mạnh, nhưng schema design mới là phần quyết định API có sống nổi qua thời gian không.

### 1. Thiết kế theo domain, không theo bảng DB

Đừng expose thẳng table structure nếu điều đó làm schema cứng hoặc lộ chi tiết nội bộ.

Tệ:

- `product_table_row`
- `order_db_item`

Tốt hơn:

- `Product`
- `Order`
- `CheckoutSummary`

### 2. Dùng field có ngữ nghĩa

Thay vì một field `statusCode`, có thể bạn cần enum `OrderStatus`.

```csharp
public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Cancelled
}
```

### 3. Tránh mutation generic kiểu patch object vô tội vạ

GraphQL cho phép input rất linh hoạt, nhưng không đồng nghĩa bạn nên expose `updateProduct(input: ProductInput)` rồi client muốn sửa gì cũng được. Mutation nên bám use case nghiệp vụ.

### 4. Deprecate thay vì phá ngang

Hot Chocolate hỗ trợ deprecation:

```csharp
descriptor.Field("legacySku")
    .Deprecated("Use sku instead.");
```

### 5. Giữ contract ổn định

Một schema tốt cần version hoá mềm mại. GraphQL thường tránh version endpoint kiểu `/v2`, mà evolve bằng additive change, deprecation, capability discovery.

---

## Federation, stitching và gateway pattern

Khi hệ thống có nhiều GraphQL service hoặc nhiều domain team, câu hỏi là: làm sao có một unified schema?

Hot Chocolate hỗ trợ nhiều pattern như schema stitching và federation. Ý tưởng chung là không để client phải tự gọi 5 GraphQL endpoint khác nhau.

### Trường hợp dùng gateway/BFF

- Catalog team có service GraphQL riêng
- Order team có service GraphQL riêng
- User team có service GraphQL riêng
- Frontend cần một graph thống nhất

Gateway hoặc stitched schema có thể hợp nhất các nguồn đó.

Ví dụ khái niệm cấu hình remote schema:

```csharp
builder.Services
    .AddGraphQLServer()
    .AddRemoteSchema("catalog")
    .AddRemoteSchema("orders");
```

Sau đó gateway có thể extend type hoặc expose unified entry point. Phần này trong production cần quan tâm lớn tới auth propagation, latency, caching và ownership của schema.

### Khi nào nên cẩn thận?

- nếu team chưa mạnh về GraphQL governance, federation dễ thành spaghetti graph
- latency cộng dồn giữa nhiều subgraph là có thật
- auth và error mapping qua gateway cần rất rõ ràng

GraphQL gateway không tự động sửa các vấn đề tổ chức. Nó chỉ phóng đại cả điểm mạnh lẫn điểm yếu của tổ chức đó.

---

## EF Core integration và tối ưu query

Hot Chocolate và EF Core là cặp ghép rất phổ biến. Nhưng cần vài nguyên tắc để tránh pain.

### 1. Trả `IQueryable` khi hợp lý

Điều này cho phép middleware projection/filtering/sorting can thiệp.

```csharp
[UseProjection]
[UseFiltering]
[UseSorting]
public IQueryable<Product> GetProducts(AppDbContext db) => db.Products.AsNoTracking();
```

### 2. Dùng `AsNoTracking` cho read query

GraphQL read-heavy, đừng giữ change tracking nếu không cần.

### 3. Không trộn business logic nặng vào expression pipeline

Một số field nên resolve riêng bằng service, thay vì cố nén tất cả vào EF query.

### 4. Kiểm tra generated SQL

Projection rất hay, nhưng bạn nên log SQL để chắc query không vô tình phình to hoặc join vô nghĩa.

### 5. Tách write path và read path khi cần

Nếu query GraphQL phức tạp, đôi khi read model riêng hoặc CQRS hợp lý hơn là ép EF model gốc gánh hết.

---

## Persisted operations, complexity và security

GraphQL linh hoạt nhưng nếu mở toang cho client gửi query tuỳ ý, bạn sẽ đối mặt risk về performance và abuse.

### Persisted operations

Ý tưởng là client chỉ gửi operation id/hash, server tra query đã đăng ký trước. Lợi ích:

- giảm payload request
- kiểm soát query được phép chạy
- cache dễ hơn
- giảm tấn công query tùy tiện

### Complexity analysis

Một query tưởng nhỏ có thể rất nặng vì nested collection sâu. Bạn nên đặt limit độ phức tạp và depth.

Ví dụ tư duy:

- depth max = 8
- collection page size max = 100
- block introspection ở production công khai nếu cần
- dùng allow-list cho operation quan trọng

### Authorization và throttling vẫn cần

GraphQL không miễn nhiễm với brute force, enumeration, scraping. Nếu API public, hãy kết hợp auth, rate limit, complexity limit, persisted queries.

---

## Error handling và observability

Một GraphQL server production-ready không chỉ trả `data`. Nó cần quan sát được hành vi query.

### Logging ví dụ

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
```

### Điều nên log

- operation name
- duration
- error code
- principal/user id
- query hash hoặc persisted query id

### Điều không nên log bừa bãi

- full query chứa thông tin nhạy cảm
- token, email, card data trong variable
- response lớn gây tốn log và rò dữ liệu

### Error mapping

Bạn có thể tạo error filter để chuẩn hoá exception thành GraphQL error shape.

```csharp
public class GraphQLErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is InvalidOperationException)
        {
            return error.WithMessage("Invalid operation.")
                .WithCode("INVALID_OPERATION");
        }

        return error;
    }
}
```

Đăng ký:

```csharp
builder.Services.AddErrorFilter<GraphQLErrorFilter>();
```

Nhờ đó client có trải nghiệm lỗi ổn định hơn.

---

## Một ví dụ query thực chiến hơn

Hãy tưởng tượng frontend trang product detail cần:

- product core info
- category
- inventory
- reviews
- author của từng review
- cờ `isExpensive`

Chỉ một request:

```graphql
query ProductDetail($id: String!) {
  productById(id: $id) {
    id
    name
    slug
    price
    isExpensive
    category {
      id
      name
    }
    inventory
    reviews {
      id
      rating
      comment
      author {
        id
        displayName
      }
    }
  }
}
```

Với REST, frontend có thể phải gọi:

1. `/products/{id}`
2. `/categories/{categoryId}`
3. `/inventory/{productId}`
4. `/reviews?productId=...`
5. `/users/{id}` cho từng review author nếu không batch tốt

GraphQL không phải luôn ít call hơn trong backend, nhưng nó cho client một contract đọc hợp nhất và expressive hơn rất nhiều.

---

## Một ví dụ mutation có business semantics tốt hơn

Thay vì update order raw, ta định nghĩa mutation nghiệp vụ:

```csharp
public record MarkOrderPaidInput(string OrderId, string PaymentReference);
public record MarkOrderPaidPayload(string OrderId, string Status);

public partial class Mutation
{
    public async Task<MarkOrderPaidPayload> MarkOrderPaidAsync(
        MarkOrderPaidInput input,
        AppDbContext db,
        CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(x => x.Id == input.OrderId, ct);
        if (order is null)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Order not found.")
                .SetCode("ORDER_NOT_FOUND")
                .Build());
        }

        if (order.Status == "Paid")
        {
            return new MarkOrderPaidPayload(order.Id, order.Status);
        }

        order.Status = "Paid";
        await db.SaveChangesAsync(ct);

        return new MarkOrderPaidPayload(order.Id, order.Status);
    }
}
```

Client gọi rõ ý nghĩa:

```graphql
mutation {
  markOrderPaid(input: { orderId: "abc123", paymentReference: "PAY-999" }) {
    orderId
    status
  }
}
```

Đây là cách GraphQL đẹp nhất, không phải biến nó thành RPC hỗn loạn hay CRUD generator vô hồn.

---

## Testing GraphQL với Hot Chocolate

Một lợi thế lớn của Hot Chocolate là test schema và execution khá thuận tiện.

### Test integration ý tưởng

- spin up test server bằng `WebApplicationFactory`
- execute query/mutation với HTTP POST `/graphql`
- assert `data` và `errors`

Ví dụ payload:

```json
{
  "query": "query { products { id name } }"
}
```

Bạn cũng có thể test executor trực tiếp nếu muốn đi nhanh ở mức schema.

### Nên test gì?

1. query đúng shape dữ liệu
2. authorization chặn đúng field
3. mutation trả error code đúng khi invalid input
4. DataLoader không gây duplicate call bất thường
5. paging/filter/sort hoạt động đúng
6. deprecation và schema evolution không phá client cũ

GraphQL rất dễ bị hỏng ở contract layer mà unit test business không phát hiện. Vì vậy schema-level test rất quan trọng.

---

## Best practices thực chiến với Hot Chocolate

### 1. Tách query model và domain model khi cần

Nếu domain object quá nặng hoặc chứa field nội bộ, đừng expose trực tiếp. Dùng DTO/read model phù hợp với API.

### 2. DataLoader là mặc định cho relation ngoài DB chính

Đừng đợi production chậm rồi mới thêm. Cứ giả định relation cross-service cần batch.

### 3. Giới hạn collection và complexity

Không cho query vô hạn, không cho nested depth vô kiểm soát.

### 4. Thiết kế mutation theo use case

Một mutation tốt kể được câu chuyện nghiệp vụ.

### 5. Cẩn thận với breaking change

- đổi tên field là breaking
- đổi nullability có thể breaking
- xóa enum value có thể breaking
- thay type của field thường breaking

### 6. Quan tâm DX của frontend

Tên field rõ, error code nhất quán, schema docs tốt, deprecation message tử tế sẽ giúp team frontend cực nhiều.

### 7. Đừng cố nhét mọi thứ vào GraphQL

REST vẫn có chỗ đứng. Ví dụ file upload lớn, webhook, streaming data đặc thù, hoặc endpoint rất đơn giản và ổn định. GraphQL không phải lời giải cho mọi interface.

---

## Những sai lầm phổ biến

### 1. Expose entity DB thẳng hết mọi thứ

Kết quả là schema rò chi tiết nội bộ và rất khó evolve.

### 2. Không dùng DataLoader

Đây là lỗi phổ biến nhất. App vẫn chạy, demo vẫn đẹp, nhưng production thì query nổ tung.

### 3. Cho filter/sort quá thoải mái

Client có thể tạo query cực nặng ngoài dự đoán.

### 4. Không phân tách auth theo field

Root query được bảo vệ nhưng nested field nhạy cảm lại mở. Rất nguy hiểm.

### 5. Coi GraphQL như SQL over HTTP

Nếu client muốn gì trả nấy mà không có guardrail, bạn sẽ có một API khó bảo trì và khó tối ưu.

### 6. Không quản lý schema change

GraphQL làm client thích nghi tốt hơn REST ở vài điểm, nhưng breaking change vẫn là breaking change.

---

## Hot Chocolate trong kiến trúc microservices

Hot Chocolate rất hợp với các vai trò sau:

### 1. API Gateway / Backend For Frontend

Một service GraphQL đứng trước nhiều microservice REST/gRPC để hợp nhất data shape cho web/mobile. Đây là case cực phổ biến.

### 2. Domain Graph cho internal platform

Nhiều team nội bộ cần truy vấn dữ liệu phân tán trong tổ chức. GraphQL là lớp query thống nhất tốt nếu governance đủ mạnh.

### 3. Read model cho CQRS

Nếu write side dùng REST hoặc messaging, read side có thể dùng GraphQL để phục vụ dashboard và UI phong phú.

### 4. Admin portal và dashboard

Admin thường cần query linh hoạt, filter, search, nested relation, pagination. GraphQL hợp tự nhiên.

Tuy nhiên, nếu mỗi microservice đều tự expose GraphQL vô tổ chức mà không có schema ownership rõ ràng, hệ thống rất dễ loạn.

---

## Một skeleton hoàn chỉnh hơn

### Query

```csharp
public class Query
{
    [UsePaging(IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Product> GetProducts(AppDbContext db) => db.Products.AsNoTracking();

    public Task<Product?> GetProductByIdAsync(string id, AppDbContext db, CancellationToken ct) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    [Authorize]
    [UseProjection]
    public IQueryable<Order> GetOrders(AppDbContext db) => db.Orders.AsNoTracking();
}
```

### Product type

```csharp
public class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        descriptor.Field(x => x.Id).Type<NonNullType<IdType>>();
        descriptor.Field(x => x.Name).Type<NonNullType<StringType>>();
        descriptor.Field(x => x.Price).Type<NonNullType<DecimalType>>();
        descriptor.Field(x => x.Category).Type<NonNullType<CategoryType>>();

        descriptor.Field("inventory")
            .Type<NonNullType<IntType>>()
            .ResolveWith<ProductResolvers>(x => x.GetInventoryAsync(default!, default!, default));

        descriptor.Field("reviews")
            .Type<NonNullType<ListType<NonNullType<ReviewType>>>>()
            .ResolveWith<ProductResolvers>(x => x.GetReviewsAsync(default!, default!, default));
    }
}
```

### Mutation

```csharp
public class Mutation
{
    public async Task<CreateOrderPayload> CreateOrderAsync(
        CreateOrderInput input,
        AppDbContext db,
        ITopicEventSender eventSender,
        CancellationToken ct)
    {
        if (input.Items.Count == 0)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("At least one item is required.")
                .SetCode("ORDER_ITEMS_REQUIRED")
                .Build());
        }

        var productIds = input.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products.Where(x => productIds.Contains(x.Id)).ToListAsync(ct);

        var order = new Order
        {
            Id = Guid.NewGuid().ToString("N"),
            CustomerEmail = input.CustomerEmail,
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow,
            Items = input.Items.Select(item =>
            {
                var product = products.Single(x => x.Id == item.ProductId);
                return new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price
                };
            }).ToList()
        };

        order.TotalAmount = order.Items.Sum(x => x.UnitPrice * x.Quantity);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var payload = new CreateOrderPayload(order.Id, order.Status, order.TotalAmount);
        await eventSender.SendAsync(nameof(Subscription.OnOrderCreated), payload, ct);

        return payload;
    }
}
```

### Subscription

```csharp
public class Subscription
{
    [Subscribe]
    [Topic]
    public CreateOrderPayload OnOrderCreated([EventMessage] CreateOrderPayload payload) => payload;
}
```

Đây đã là một GraphQL backend rất có hồn cho một ứng dụng vừa phải.

---

## So sánh Hot Chocolate với REST và các lựa chọn .NET khác

### Hot Chocolate vs REST API controller truyền thống

REST vẫn tuyệt vời cho:

- CRUD rõ resource
- caching HTTP chuẩn
- webhook
- file upload/download
- API công khai đơn giản

Hot Chocolate mạnh hơn khi:

- dữ liệu cần ghép từ nhiều nguồn
- frontend có nhiều shape query khác nhau
- cần nested graph hợp lý
- muốn giảm over-fetching / under-fetching

### Hot Chocolate vs OData

OData giải bài toán queryable API nhưng thường lộ data model và semantics kém tự nhiên hơn cho frontend hiện đại. Hot Chocolate thường mang lại schema expressive hơn, contract đẹp hơn, DX tốt hơn, dù OData có chỗ đứng riêng trong một số enterprise app.

### Hot Chocolate vs tự viết GraphQL stack nhẹ hơn

Bạn có thể tự ráp nhiều thứ, nhưng rất nhanh sẽ nhớ DataLoader, auth, subscriptions, filtering, schema tooling, projection, performance guardrails. Hot Chocolate đáng giá vì đã trưởng thành ở các phần khó đó.

---

## Kết luận

Hot Chocolate là một trong những thư viện đáng học nhất nếu bạn làm .NET backend hiện đại và cần GraphQL nghiêm túc. Giá trị thật sự của nó không nằm ở việc “trả dữ liệu đúng field client yêu cầu”, mà ở chỗ nó cho bạn một runtime GraphQL đầy đủ để xây API giàu năng lực: schema typed rõ ràng, query/mutation/subscription nhất quán, DataLoader chống N+1, middleware cho filtering/sorting/paging/projection, authorization đến cấp field, và khả năng hợp nhất dữ liệu từ nhiều nguồn thành một graph có ý nghĩa.

Nếu dùng đúng, Hot Chocolate giúp bạn tạo một API rất phù hợp cho frontend phức tạp, dashboard, BFF, read model và gateway aggregation. Nếu dùng sai, đặc biệt khi bỏ qua DataLoader, schema governance và performance guardrails, bạn sẽ tạo ra một hệ thống rất khó kiểm soát.

Cách học tốt nhất là bắt đầu từ một schema nhỏ nhưng thực tế: products, categories, reviews, orders. Viết query trước, rồi mutation có ngữ nghĩa nghiệp vụ, sau đó thêm DataLoader và paging. Khi đã nắm được các khái niệm đó, bạn sẽ thấy Hot Chocolate không chỉ là một thư viện GraphQL, mà là một bộ công cụ rất mạnh để thiết kế API đọc và tương tác hiện đại trong hệ .NET.

Nếu phải nhớ một ý duy nhất sau bài này, hãy nhớ điều này: Hot Chocolate mạnh nhất khi bạn coi schema là product contract sống lâu, chứ không phải lớp bọc mỏng quanh database. Khi đó mọi tính năng của nó, từ type system đến DataLoader và authorization, mới thật sự phát huy hết giá trị.
