# Bài 18: Background Jobs & Scheduling với Hangfire và Quartz.NET

## Mục lục
1. [Giới thiệu Background Jobs](#1-giới-thiệu-background-jobs)
2. [Các loại Background Job](#2-các-loại-background-job)
3. [Hangfire - Cài đặt và Cấu hình](#3-hangfire---cài-đặt-và-cấu-hình)
4. [Hangfire Dashboard và Authorization](#4-hangfire-dashboard-và-authorization)
5. [Fire-and-Forget Jobs](#5-fire-and-forget-jobs)
6. [Delayed Jobs](#6-delayed-jobs)
7. [Recurring Jobs](#7-recurring-jobs)
8. [Job Continuations](#8-job-continuations)
9. [Custom Job Filters](#9-custom-job-filters)
10. [Quartz.NET - Cài đặt và Cấu hình](#10-quartznet---cài-đặt-và-cấu-hình)
11. [Cron Expressions](#11-cron-expressions)
12. [Job Listeners và Trigger Listeners](#12-job-listeners-và-trigger-listeners)
13. [Calendars - Loại trừ ngày nghỉ](#13-calendars---loại-trừ-ngày-nghỉ)
14. [AdoJobStore - Lưu trữ bền vững](#14-adojobstore---lưu-trữ-bền-vững)
15. [Distributed Job Processing](#15-distributed-job-processing)
16. [Dự án thực tế: E-commerce Background Jobs](#16-dự-án-thực-tế-e-commerce-background-jobs)
17. [Job Monitoring và Retry Policies](#17-job-monitoring-và-retry-policies)
18. [Outbox Pattern với Background Jobs](#18-outbox-pattern-với-background-jobs)
19. [Best Practices](#19-best-practices)
20. [Tổng kết](#20-tổng-kết)

---

## 1. Giới thiệu Background Jobs

### Background Jobs là gì?

Trong phát triển ứng dụng web, không phải tác vụ nào cũng cần được thực thi ngay lập tức và trả về kết quả cho người dùng. **Background Jobs** (công việc nền) là những tác vụ được thực thi **bất đồng bộ**, tách biệt khỏi request/response cycle, cho phép ứng dụng phản hồi người dùng nhanh hơn trong khi các tác vụ nặng vẫn đang chạy ở phía sau.

### Tại sao cần Background Jobs?

- **Cải thiện trải nghiệm người dùng**: Người dùng không phải chờ đợi các tác vụ dài (gửi email, tạo báo cáo, xử lý ảnh)
- **Độ tin cậy cao hơn**: Nếu tác vụ thất bại, có thể retry tự động mà không ảnh hưởng đến người dùng
- **Phân tải hệ thống**: Các tác vụ nặng được xử lý vào thời điểm ít tải hơn (ví dụ: nửa đêm)
- **Tích hợp với hệ thống bên ngoài**: Gọi API bên ngoài, gửi message queue mà không block luồng chính

### Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────────────────────┐
│                        ASP.NET Core App                         │
│                                                                 │
│  ┌─────────────┐    ┌──────────────────┐    ┌───────────────┐  │
│  │   HTTP      │    │   Job Scheduler  │    │   Job Queue   │  │
│  │  Request    │───▶│  (Hangfire/      │───▶│  (Storage)    │  │
│  │  Handler    │    │   Quartz.NET)    │    │               │  │
│  └─────────────┘    └──────────────────┘    └───────┬───────┘  │
│         │                                           │           │
│         │ Response ngay                             │           │
│         ▼                                           ▼           │
│  ┌─────────────┐                         ┌───────────────────┐ │
│  │   Client    │                         │   Background      │ │
│  │  (Browser)  │                         │   Worker          │ │
│  └─────────────┘                         │   (Processing)    │ │
│                                          └───────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Các loại Background Job

### 2.1 Fire-and-Forget (Bắn và Quên)

**Khái niệm**: Job được thêm vào queue và thực thi một lần duy nhất càng sớm càng tốt. Caller không cần chờ kết quả.

**Ví dụ thực tế**: Gửi email xác nhận đơn hàng, ghi log analytics, cập nhật cache.

```
Client ──► API Handler ──► Enqueue Job ──► Response (200 OK)
                                  │
                                  ▼
                           [Background Worker]
                           Thực thi job sau đó
```

### 2.2 Delayed Jobs (Job Trì hoãn)

**Khái niệm**: Job được lên lịch thực thi sau một khoảng thời gian nhất định.

**Ví dụ thực tế**: Gửi email nhắc nhở sau 24h, tự động hủy đơn hàng chưa thanh toán sau 30 phút.

```
Client ──► API Handler ──► Schedule Job (sau 30 phút) ──► Response
                                  │
                           [Chờ 30 phút]
                                  │
                                  ▼
                           [Background Worker]
                           Thực thi job đúng giờ
```

### 2.3 Recurring Jobs (Job Định kỳ)

**Khái niệm**: Job được thực thi lặp đi lặp lại theo lịch định sẵn (cron expression).

**Ví dụ thực tế**: Tạo báo cáo doanh thu hàng ngày, dọn dẹp session hết hạn, sync dữ liệu từ external API.

```
┌─────────────────────────────────────────────┐
│              Scheduler                       │
│                                             │
│  [Cron: "0 0 * * *"] ──► Execute at 00:00  │
│  [Cron: "0 * * * *"] ──► Execute every hour│
│  [Cron: "*/5 * * * *"] ► Execute every 5m  │
└─────────────────────────────────────────────┘
```

### 2.4 Job Continuations (Job Tiếp nối)

**Khái niệm**: Job B chỉ được thực thi sau khi Job A hoàn thành thành công.

**Ví dụ thực tế**: Sau khi xử lý đơn hàng → gửi email xác nhận → cập nhật loyalty points.

```
[Job A: Xử lý thanh toán]
         │
         ▼ (khi hoàn thành)
[Job B: Gửi email xác nhận]
         │
         ▼ (khi hoàn thành)
[Job C: Cập nhật điểm thưởng]
```

---

## 3. Hangfire - Cài đặt và Cấu hình

### 3.1 Cài đặt NuGet Packages

```bash
# Package chính
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.SqlServer

# Dashboard
dotnet add package Hangfire.Dashboard.Authorization  # optional, cho auth

# Nếu dùng PostgreSQL
dotnet add package Hangfire.PostgreSql

# Nếu dùng Redis
dotnet add package Hangfire.Pro.Redis  # requires license
```

### 3.2 Cấu hình SQL Server Storage

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "HangfireConnection": "Server=localhost;Database=EcommerceHangfire;Integrated Security=true;TrustServerCertificate=true;"
  },
  "Hangfire": {
    "WorkerCount": 5,
    "Queues": ["critical", "default", "low"]
  }
}
```

### 3.3 Program.cs - Cấu hình đầy đủ

```csharp
// Program.cs
using Hangfire;
using Hangfire.SqlServer;
using EcommerceApp.Jobs;
using EcommerceApp.Services;
using EcommerceApp.Filters;

var builder = WebApplication.CreateBuilder(args);

// ─── Đăng ký Services ───────────────────────────────────────────
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// ─── Cấu hình Hangfire ──────────────────────────────────────────
builder.Services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(
            builder.Configuration.GetConnectionString("HangfireConnection"),
            new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout     = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval          = TimeSpan.FromSeconds(15),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks           = true,
                SchemaName                   = "hangfire"
            }
        )
        // Thêm custom job filter toàn cục
        .UseFilter(new AutomaticRetryAttribute { Attempts = 3 })
        .UseFilter(new JobLoggingFilter())
        .UseFilter(new CorrelationIdFilter());
});

// ─── Cấu hình Hangfire Server (Worker) ──────────────────────────
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2; // Số worker tự động
    options.Queues = new[] { "critical", "default", "low" }; // Ưu tiên queue
    options.ServerName = $"{Environment.MachineName}:{Guid.NewGuid()}";
    options.HeartbeatInterval     = TimeSpan.FromSeconds(30);
    options.ServerCheckInterval   = TimeSpan.FromSeconds(30);
    options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// ─── Cấu hình Hangfire Dashboard ────────────────────────────────
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Ecommerce Job Dashboard",
    Authorization  = new[] { new HangfireAuthorizationFilter() },
    StatsPollingInterval = 5000 // Refresh mỗi 5 giây
});

// ─── Đăng ký Recurring Jobs ─────────────────────────────────────
app.UseHangfireRecurringJobs();

app.Run();
```

### 3.4 Extension Method cho Recurring Jobs

```csharp
// Extensions/HangfireExtensions.cs
using Hangfire;
using EcommerceApp.Jobs;

namespace EcommerceApp.Extensions;

public static class HangfireExtensions
{
    public static WebApplication UseHangfireRecurringJobs(this WebApplication app)
    {
        // Dọn dẹp session hết hạn - mỗi giờ
        RecurringJob.AddOrUpdate<CleanupJob>(
            recurringJobId: "cleanup-expired-sessions",
            methodCall: job => job.CleanupExpiredSessions(CancellationToken.None),
            cronExpression: Cron.Hourly(),
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"),
                QueueName = "low"
            }
        );

        // Tạo báo cáo hàng ngày - 23:55 mỗi ngày
        RecurringJob.AddOrUpdate<ReportingJob>(
            recurringJobId: "daily-revenue-report",
            methodCall: job => job.GenerateDailyRevenueReport(CancellationToken.None),
            cronExpression: "55 23 * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"),
                QueueName = "default"
            }
        );

        return app;
    }
}
```

---

## 4. Hangfire Dashboard và Authorization

### 4.1 Kiến trúc Hangfire

```
┌───────────────────────────────────────────────────────────────────┐
│                      Hangfire Architecture                        │
│                                                                   │
│  ┌──────────────┐     ┌─────────────────────────────────────┐    │
│  │   Your App   │     │         SQL Server Storage          │    │
│  │              │────▶│  ┌─────────┐  ┌──────────────────┐ │    │
│  │  .Enqueue()  │     │  │  Jobs   │  │  RecurringJobs   │ │    │
│  │  .Schedule() │     │  ├─────────┤  ├──────────────────┤ │    │
│  │  .AddOrUpdate│     │  │  States │  │  JobQueues       │ │    │
│  └──────────────┘     │  ├─────────┤  ├──────────────────┤ │    │
│                       │  │ Counters│  │  Servers         │ │    │
│  ┌──────────────┐     │  └─────────┘  └──────────────────┘ │    │
│  │  Dashboard   │────▶│                                     │    │
│  │  /hangfire   │     └─────────────────────────────────────┘    │
│  └──────────────┘                      ▲                         │
│                                        │                         │
│  ┌──────────────────────────────────────┤                        │
│  │         Hangfire Server (Workers)    │                        │
│  │  ┌──────────┐  ┌──────────┐         │                        │
│  │  │ Worker 1 │  │ Worker 2 │  ...    │                        │
│  │  │ (critical│  │ (default)│         │                        │
│  │  │  queue)  │  │  queue)  │         │                        │
│  │  └──────────┘  └──────────┘         │                        │
│  └──────────────────────────────────────┘                        │
└───────────────────────────────────────────────────────────────────┘
```

### 4.2 Custom Authorization Filter

```csharp
// Filters/HangfireAuthorizationFilter.cs
using Hangfire.Dashboard;

namespace EcommerceApp.Filters;

/// <summary>
/// Chỉ cho phép người dùng đã xác thực và có role Admin truy cập dashboard
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string[] _allowedRoles;

    public HangfireAuthorizationFilter(params string[] allowedRoles)
    {
        _allowedRoles = allowedRoles.Length > 0 ? allowedRoles : new[] { "Admin" };
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Chỉ cho phép khi đã đăng nhập
        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
        {
            return false;
        }

        // Kiểm tra role
        return _allowedRoles.Any(role => httpContext.User.IsInRole(role));
    }
}
```

---

## 5. Fire-and-Forget Jobs

### 5.1 Cách sử dụng cơ bản

```csharp
// Controllers/OrderController.cs
using Hangfire;
using EcommerceApp.Services;
using EcommerceApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IOrderService      _orderService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        IOrderService orderService,
        IBackgroundJobClient backgroundJobs,
        ILogger<OrderController> logger)
    {
        _orderService   = orderService;
        _backgroundJobs = backgroundJobs;
        _logger         = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        // 1. Tạo đơn hàng (đồng bộ)
        var order = await _orderService.CreateOrderAsync(request);

        // 2. Enqueue job gửi email (bất đồng bộ - fire and forget)
        //    Người dùng nhận response ngay, email được gửi sau
        var jobId = _backgroundJobs.Enqueue<IEmailService>(
            emailService => emailService.SendOrderConfirmationAsync(order.Id, CancellationToken.None)
        );

        _logger.LogInformation(
            "Order {OrderId} created. Email job {JobId} enqueued",
            order.Id, jobId);

        // 3. Enqueue job cập nhật inventory (queue ưu tiên cao)
        _backgroundJobs.Enqueue<IInventoryService>(
            inv => inv.DeductStockAsync(order.Items, CancellationToken.None),
            new EnqueuedState("critical") // Chạy trong queue critical
        );

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }
}
```

### 5.2 Enqueue với Lambda Expression

```csharp
// Cách 1: Sử dụng interface (recommended - type-safe)
BackgroundJob.Enqueue<IEmailService>(
    email => email.SendWelcomeEmailAsync("user@example.com", CancellationToken.None)
);

// Cách 2: Sử dụng static method (legacy)
BackgroundJob.Enqueue(() => Console.WriteLine("Hello from background!"));

// Cách 3: Chỉ định queue cụ thể
BackgroundJob.Enqueue<IReportService>(
    report => report.GenerateAsync(DateTime.Today, CancellationToken.None),
    new EnqueuedState("low")
);
```

---

## 6. Delayed Jobs

### 6.1 Lên lịch job sau một khoảng thời gian

```csharp
// Hủy đơn hàng nếu chưa thanh toán sau 30 phút
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
{
    var order = await _orderService.CreateOrderAsync(request);

    // Lên lịch job kiểm tra thanh toán sau 30 phút
    var cancelJobId = _backgroundJobs.Schedule<IOrderService>(
        orderSvc => orderSvc.CancelIfUnpaidAsync(order.Id, CancellationToken.None),
        TimeSpan.FromMinutes(30)
    );

    // Lưu jobId để có thể cancel nếu cần
    await _orderService.SaveCancelJobIdAsync(order.Id, cancelJobId);

    return Created($"/api/orders/{order.Id}", order);
}

// Nếu user thanh toán trước 30 phút, xóa job hủy đơn
public async Task<IActionResult> ProcessPayment(Guid orderId, [FromBody] PaymentRequest request)
{
    var order = await _orderService.GetOrderByIdAsync(orderId);
    if (order?.CancelJobId is not null)
    {
        // Xóa job hủy đơn vì đã thanh toán
        BackgroundJob.Delete(order.CancelJobId);
    }

    var payment = await _paymentService.ProcessAsync(orderId, request);
    return Ok(payment);
}
```

### 6.2 Schedule với DateTimeOffset cụ thể

```csharp
// Gửi email marketing vào 9:00 sáng hôm sau
var tomorrow9AM = DateTimeOffset.Now.Date.AddDays(1).AddHours(9);

BackgroundJob.Schedule<IEmailService>(
    email => email.SendMarketingEmailAsync(userId, campaignId, CancellationToken.None),
    tomorrow9AM
);
```

---

## 7. Recurring Jobs

### 7.1 Định nghĩa Recurring Job

```csharp
// Jobs/CleanupJob.cs
using Hangfire;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs;

/// <summary>
/// Job dọn dẹp session hết hạn - chạy mỗi giờ
/// </summary>
public class CleanupJob
{
    private readonly ISessionService           _sessionService;
    private readonly ILogger<CleanupJob>       _logger;

    public CleanupJob(
        ISessionService sessionService,
        ILogger<CleanupJob> logger)
    {
        _sessionService = sessionService;
        _logger         = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    [Queue("low")]
    public async Task CleanupExpiredSessions(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting expired session cleanup at {Time}", DateTime.UtcNow);

        var deleted = await _sessionService.DeleteExpiredSessionsAsync(
            olderThan: TimeSpan.FromHours(24),
            cancellationToken: cancellationToken
        );

        _logger.LogInformation(
            "Cleanup completed. Deleted {Count} expired sessions",
            deleted);
    }
}
```

### 7.2 Cron Expression phổ biến

```csharp
// Hangfire cung cấp Cron helper
RecurringJob.AddOrUpdate("job-1", () => DoWork(), Cron.Minutely());    // Mỗi phút
RecurringJob.AddOrUpdate("job-2", () => DoWork(), Cron.Hourly());      // Mỗi giờ
RecurringJob.AddOrUpdate("job-3", () => DoWork(), Cron.Daily());       // Mỗi ngày lúc 00:00
RecurringJob.AddOrUpdate("job-4", () => DoWork(), Cron.Weekly());      // Mỗi tuần (Thứ 2)
RecurringJob.AddOrUpdate("job-5", () => DoWork(), Cron.Monthly());     // Ngày 1 mỗi tháng

// Hoặc dùng cron expression tùy chỉnh
RecurringJob.AddOrUpdate("job-6", () => DoWork(), "0 9 * * 1-5");  // 9:00 sáng các ngày làm việc
RecurringJob.AddOrUpdate("job-7", () => DoWork(), "0 0 1 * *");    // 00:00 ngày 1 hàng tháng
RecurringJob.AddOrUpdate("job-8", () => DoWork(), "*/15 * * * *"); // Mỗi 15 phút
```

---

## 8. Job Continuations

### 8.1 Xây dựng chuỗi job

```csharp
// Controllers/OrderController.cs - Xử lý đơn hàng phức tạp
public async Task<IActionResult> FulfillOrder(Guid orderId)
{
    // Job 1: Xử lý thanh toán
    var paymentJobId = BackgroundJob.Enqueue<IPaymentService>(
        payment => payment.ChargeCustomerAsync(orderId, CancellationToken.None),
        new EnqueuedState("critical")
    );

    // Job 2: Chỉ chạy sau khi Job 1 thành công
    var emailJobId = BackgroundJob.ContinueJobWith<IEmailService>(
        parentId: paymentJobId,
        methodCall: email => email.SendOrderConfirmationAsync(orderId, CancellationToken.None)
    );

    // Job 3: Chỉ chạy sau khi Job 2 thành công
    var loyaltyJobId = BackgroundJob.ContinueJobWith<ILoyaltyService>(
        parentId: emailJobId,
        methodCall: loyalty => loyalty.AwardPointsAsync(orderId, CancellationToken.None)
    );

    // Job 4: Thông báo cho warehouse (song song với Job 2, sau Job 1)
    var warehouseJobId = BackgroundJob.ContinueJobWith<IWarehouseService>(
        parentId: paymentJobId,
        methodCall: wh => wh.NotifyFulfillmentAsync(orderId, CancellationToken.None)
    );

    return Accepted(new { 
        Message = "Order fulfillment started",
        PaymentJobId  = paymentJobId,
        EmailJobId    = emailJobId,
        LoyaltyJobId  = loyaltyJobId,
        WarehouseJobId = warehouseJobId
    });
}
```

### 8.2 Luồng thực thi Job Continuations

```
[Job 1: ChargeCustomer] (critical queue)
          │
          ▼ SUCCESS
    ┌─────┴──────┐
    │            │
    ▼            ▼
[Job 2:       [Job 4:
 SendEmail]   NotifyWarehouse]
    │
    ▼ SUCCESS
[Job 3: AwardPoints]
```

---

## 9. Custom Job Filters

### 9.1 Logging Filter

```csharp
// Filters/JobLoggingFilter.cs
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace EcommerceApp.Filters;

/// <summary>
/// Filter ghi log khi job bắt đầu và kết thúc
/// </summary>
public class JobLoggingFilter : JobFilterAttribute,
    IApplyStateFilter, IElectStateFilter
{
    private static readonly ILogger Logger =
        LoggerFactory.Create(b => b.AddConsole())
                     .CreateLogger<JobLoggingFilter>();

    // Gọi khi trạng thái job thay đổi
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is FailedState failedState)
        {
            Logger.LogError(
                failedState.Exception,
                "Job {JobId} [{JobName}] failed after {Retries} retries",
                context.BackgroundJob.Id,
                context.BackgroundJob.Job.Method.Name,
                context.BackgroundJob.Job.Args);
        }
    }

    // Gọi khi trạng thái được áp dụng
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        var jobName = context.BackgroundJob.Job.Method.Name;
        var jobId   = context.BackgroundJob.Id;

        switch (context.NewState)
        {
            case ProcessingState:
                Logger.LogInformation("Job {JobId} [{JobName}] started processing", jobId, jobName);
                break;

            case SucceededState succeeded:
                Logger.LogInformation(
                    "Job {JobId} [{JobName}] succeeded. Duration: {Duration}ms",
                    jobId, jobName, succeeded.PerformanceDuration);
                break;

            case FailedState failed:
                Logger.LogError(
                    failed.Exception,
                    "Job {JobId} [{JobName}] failed",
                    jobId, jobName);
                break;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }
}
```

### 9.2 CorrelationId Filter

```csharp
// Filters/CorrelationIdFilter.cs
using Hangfire.Common;
using Hangfire.Server;

namespace EcommerceApp.Filters;

/// <summary>
/// Truyền CorrelationId qua các job để trace request
/// </summary>
public class CorrelationIdFilter : JobFilterAttribute, IServerFilter
{
    private const string CorrelationIdKey = "CorrelationId";

    public void OnPerforming(PerformingContext context)
    {
        // Lấy CorrelationId từ job parameters (được set khi enqueue)
        if (context.BackgroundJob.Job.Args.Count > 0)
        {
            var correlationId = context.GetJobParameter<string>(CorrelationIdKey)
                               ?? Guid.NewGuid().ToString();

            // Set vào ambient context để logger có thể pick up
            using var scope = context.Connection.AcquireDistributedLock(
                $"job:{context.BackgroundJob.Id}", TimeSpan.FromSeconds(30));
        }
    }

    public void OnPerformed(PerformedContext context) { }
}
```

### 9.3 Custom Retry Policy

```csharp
// Filters/SmartRetryAttribute.cs
using Hangfire.Common;
using Hangfire.States;

namespace EcommerceApp.Filters;

/// <summary>
/// Retry thông minh với exponential backoff, không retry khi lỗi không thể khắc phục
/// </summary>
public class SmartRetryAttribute : JobFilterAttribute, IElectStateFilter
{
    public int MaxAttempts { get; set; } = 5;

    // Danh sách exception không được retry
    private static readonly Type[] NonRetryableExceptions =
    {
        typeof(ArgumentNullException),
        typeof(ArgumentException),
        typeof(InvalidOperationException)
    };

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failedState)
            return;

        var exception = failedState.Exception;

        // Không retry nếu là lỗi không thể khắc phục
        if (NonRetryableExceptions.Any(t => t.IsInstanceOfType(exception)))
        {
            // Chuyển thẳng sang Dead Letter queue
            context.CandidateState = new DeletedState
            {
                Reason = $"Non-retryable exception: {exception.GetType().Name}"
            };
            return;
        }

        var retryCount = context.GetJobParameter<int>("RetryCount");

        if (retryCount < MaxAttempts)
        {
            // Exponential backoff: 1m, 2m, 4m, 8m, 16m
            var delay = TimeSpan.FromMinutes(Math.Pow(2, retryCount));

            context.CandidateState = new ScheduledState(delay)
            {
                Reason = $"Retry attempt {retryCount + 1}/{MaxAttempts} after {delay.TotalMinutes}m"
            };

            context.SetJobParameter("RetryCount", retryCount + 1);
        }
    }
}
```

---

## 10. Quartz.NET - Cài đặt và Cấu hình

### 10.1 Kiến trúc Quartz.NET

```
┌────────────────────────────────────────────────────────────────────┐
│                     Quartz.NET Architecture                        │
│                                                                    │
│  ┌────────────────────────────────────────────────────────────┐   │
│  │                     IScheduler                             │   │
│  │                                                            │   │
│  │  ┌──────────────┐    ┌──────────────┐    ┌────────────┐  │   │
│  │  │  IJobDetail  │    │   ITrigger   │    │  Calendar  │  │   │
│  │  │              │    │              │    │ (Exclusions│  │   │
│  │  │ - JobType    │    │ - CronTrigger│    │  holidays) │  │   │
│  │  │ - JobDataMap │    │ - SimpleTrigger    └────────────┘  │   │
│  │  │ - Durable    │    │ - CalendarInterval                 │   │
│  │  └──────────────┘    └──────────────┘                    │   │
│  │           │                   │                           │   │
│  │           └─────────┬─────────┘                          │   │
│  │                     │                                     │   │
│  │                     ▼                                     │   │
│  │           ┌──────────────────┐                           │   │
│  │           │   Thread Pool    │                           │   │
│  │           │ ┌──┐ ┌──┐ ┌──┐  │                           │   │
│  │           │ │W1│ │W2│ │W3│  │                           │   │
│  │           │ └──┘ └──┘ └──┘  │                           │   │
│  │           └──────────────────┘                           │   │
│  └────────────────────────────────────────────────────────────┘  │
│                          │                                         │
│                          ▼                                         │
│  ┌────────────────────────────────────────────────────────────┐   │
│  │                   IJobStore                                │   │
│  │       RAMJobStore (Dev) | AdoJobStore (Production)        │   │
│  └────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

### 10.2 Cài đặt NuGet Packages

```bash
dotnet add package Quartz
dotnet add package Quartz.AspNetCore
dotnet add package Quartz.Extensions.Hosting
dotnet add package Quartz.Serialization.Json

# Cho persistence với SQL Server
dotnet add package Quartz.Extensions.DependencyInjection
```

### 10.3 Cấu hình Quartz.NET

```csharp
// Program.cs - Thêm cấu hình Quartz
using Quartz;
using EcommerceApp.Jobs.Quartz;

builder.Services.AddQuartz(q =>
{
    // Sử dụng Microsoft DI container
    q.UseMicrosoftDependencyInjectionJobFactory();

    // ─── Cấu hình Job Store ─────────────────────────────────────
    // Development: RAM store (không persistent)
    q.UseSimpleTypeLoader();
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(maxConcurrency: 10);

    // ─── Đăng ký GenerateDailyRevenueReport Job ─────────────────
    var revenueReportJobKey = new JobKey("GenerateDailyRevenueReport", "Reports");

    q.AddJob<GenerateDailyRevenueReportJob>(opts => opts
        .WithIdentity(revenueReportJobKey)
        .WithDescription("Generates daily revenue report for all stores")
        .StoreDurably()
    );

    q.AddTrigger(opts => opts
        .ForJob(revenueReportJobKey)
        .WithIdentity("DailyRevenueReport-Trigger", "Reports")
        .WithCronSchedule(
            "0 0 0 * * ?",  // Chạy vào lúc 00:00:00 mỗi ngày
            x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"))
                  .WithMisfireHandlingInstructionFireAndProceed()
        )
        .WithDescription("Fires every day at midnight (Vietnam time)")
    );

    // ─── Đăng ký ProcessPendingPayments Job ─────────────────────
    var pendingPaymentsJobKey = new JobKey("ProcessPendingPayments", "Payments");

    q.AddJob<ProcessPendingPaymentsJob>(opts => opts
        .WithIdentity(pendingPaymentsJobKey)
        .WithDescription("Processes pending payment batches")
        .StoreDurably()
    );

    q.AddTrigger(opts => opts
        .ForJob(pendingPaymentsJobKey)
        .WithIdentity("ProcessPendingPayments-Trigger", "Payments")
        .WithSimpleSchedule(x => x
            .WithIntervalInMinutes(5)
            .RepeatForever()
            .WithMisfireHandlingInstructionNextWithRemainingCount()
        )
    );
});

// Tích hợp Quartz với ASP.NET Core hosted service
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true; // Chờ job hoàn thành khi shutdown
    options.StartDelay = TimeSpan.FromSeconds(10); // Delay trước khi start
});
```

---

## 11. Cron Expressions

### 11.1 Cấu trúc Cron Expression trong Quartz.NET

Quartz.NET sử dụng cron expression **6 fields** (khác với Unix cron có 5 fields):

```
┌─────────────── Seconds (0-59)
│  ┌──────────── Minutes (0-59)
│  │  ┌─────────── Hours (0-23)
│  │  │  ┌──────────── Day of Month (1-31)
│  │  │  │  ┌─────────── Month (1-12 hoặc JAN-DEC)
│  │  │  │  │  ┌──────────── Day of Week (1-7 hoặc SUN-SAT)
│  │  │  │  │  │  ┌─────────── Year (optional)
│  │  │  │  │  │  │
S  M  H  D  M  W  Y

Ví dụ:
0  0  0  *  *  ?     → Mỗi ngày lúc 00:00:00
0  30 9  ?  *  MON-FRI → 09:30 mỗi ngày làm việc
0  0  */6 *  *  ?    → Mỗi 6 tiếng
0  0  12 1  *  ?     → 12:00 ngày 1 hàng tháng
0  0  0  ?  *  SUN   → 00:00 mỗi Chủ nhật
```

### 11.2 Ví dụ Cron phổ biến

```csharp
// Các cron expression thường gặp trong e-commerce
public static class CronSchedules
{
    // Báo cáo hàng ngày lúc nửa đêm
    public const string DailyMidnight = "0 0 0 * * ?";

    // Báo cáo hàng tuần - Thứ 2 lúc 01:00
    public const string WeeklyMonday  = "0 0 1 ? * MON";

    // Báo cáo tháng - Ngày 1 lúc 02:00
    public const string MonthlyFirst  = "0 0 2 1 * ?";

    // Xử lý thanh toán mỗi 5 phút trong giờ làm việc
    public const string WorkingHours  = "0 */5 8-18 ? * MON-FRI";

    // Sync giá từ nhà cung cấp - 6:00 và 18:00
    public const string TwiceDaily    = "0 0 6,18 * * ?";

    // Cleanup mỗi giờ
    public const string Hourly        = "0 0 * * * ?";

    // Flash sale check mỗi phút
    public const string EveryMinute   = "0 * * * * ?";
}
```

---

## 12. Job Listeners và Trigger Listeners

### 12.1 Job Listener

```csharp
// Listeners/JobAuditListener.cs
using Quartz;

namespace EcommerceApp.Listeners;

/// <summary>
/// Ghi audit log cho tất cả jobs
/// </summary>
public class JobAuditListener : IJobListener
{
    private readonly ILogger<JobAuditListener>  _logger;
    private readonly IAuditService              _auditService;

    public string Name => "JobAuditListener";

    public JobAuditListener(ILogger<JobAuditListener> logger, IAuditService auditService)
    {
        _logger       = logger;
        _auditService = auditService;
    }

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Job {JobName} in group {GroupName} is about to be executed. FireTime: {FireTime}",
            context.JobDetail.Key.Name,
            context.JobDetail.Key.Group,
            context.FireTimeUtc);

        await _auditService.LogJobStartAsync(
            jobName:   context.JobDetail.Key.Name,
            groupName: context.JobDetail.Key.Group,
            fireTime:  context.FireTimeUtc.DateTime);
    }

    public async Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Job {JobName} execution was vetoed by a trigger listener",
            context.JobDetail.Key.Name);

        await _auditService.LogJobVetoedAsync(context.JobDetail.Key.Name);
    }

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        if (jobException is null)
        {
            _logger.LogInformation(
                "Job {JobName} completed successfully. Duration: {Duration}ms",
                context.JobDetail.Key.Name,
                context.JobRunTime.TotalMilliseconds);

            await _auditService.LogJobSuccessAsync(
                context.JobDetail.Key.Name,
                context.JobRunTime);
        }
        else
        {
            _logger.LogError(
                jobException.InnerException,
                "Job {JobName} failed",
                context.JobDetail.Key.Name);

            await _auditService.LogJobFailureAsync(
                context.JobDetail.Key.Name,
                jobException.InnerException?.Message ?? jobException.Message);
        }
    }
}
```

### 12.2 Trigger Listener

```csharp
// Listeners/BusinessHoursTriggerListener.cs
using Quartz;

namespace EcommerceApp.Listeners;

/// <summary>
/// Ngăn một số job chạy ngoài giờ làm việc
/// </summary>
public class BusinessHoursTriggerListener : ITriggerListener
{
    private readonly ILogger<BusinessHoursTriggerListener> _logger;

    public string Name => "BusinessHoursTriggerListener";

    public BusinessHoursTriggerListener(ILogger<BusinessHoursTriggerListener> logger)
        => _logger = logger;

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Kiểm tra xem job có yêu cầu chỉ chạy trong giờ làm việc không
        var requiresBusinessHours = context.JobDetail.JobDataMap
            .GetBoolean("RequiresBusinessHours");

        if (!requiresBusinessHours)
            return Task.FromResult(false); // Không veto

        var vnTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
        );

        var isWeekend      = vnTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isOutsideHours = vnTime.Hour < 8 || vnTime.Hour >= 18;

        if (isWeekend || isOutsideHours)
        {
            _logger.LogInformation(
                "Vetoing job {JobName} - outside business hours ({Time} VN)",
                context.JobDetail.Key.Name, vnTime);
            return Task.FromResult(true); // Veto - không chạy
        }

        return Task.FromResult(false);
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Trigger {TriggerName} misfired. Last fire: {LastFire}",
            trigger.Key.Name, trigger.GetPreviousFireTimeUtc());
        return Task.CompletedTask;
    }

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

---

## 13. Calendars - Loại trừ ngày nghỉ

### 13.1 Tạo Holiday Calendar

```csharp
// Calendars/VietnamHolidayCalendar.cs
using Quartz.Impl.Calendar;

namespace EcommerceApp.Calendars;

/// <summary>
/// Loại trừ các ngày lễ Việt Nam khỏi lịch chạy job
/// </summary>
public static class VietnamHolidayCalendar
{
    public static HolidayCalendar Create(int year)
    {
        var calendar = new HolidayCalendar();

        // Tết Dương lịch
        calendar.AddExcludedDate(new DateTime(year, 1, 1));

        // Tết Nguyên Đán (ví dụ năm 2025: 29/1 - 2/2)
        // Lưu ý: Tết âm lịch thay đổi mỗi năm
        for (var d = new DateTime(year, 1, 29); d <= new DateTime(year, 2, 2); d = d.AddDays(1))
            calendar.AddExcludedDate(d);

        // Giỗ Tổ Hùng Vương (10/3 âm lịch - thường 18/4 dương)
        calendar.AddExcludedDate(new DateTime(year, 4, 18));

        // Giải phóng miền Nam (30/4)
        calendar.AddExcludedDate(new DateTime(year, 4, 30));

        // Quốc tế Lao động (1/5)
        calendar.AddExcludedDate(new DateTime(year, 5, 1));

        // Quốc khánh (2/9)
        calendar.AddExcludedDate(new DateTime(year, 9, 2));

        return calendar;
    }
}
```

### 13.2 Đăng ký Calendar với Scheduler

```csharp
// Trong startup hoặc hosted service
public class QuartzStartupService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;

    public QuartzStartupService(ISchedulerFactory schedulerFactory)
        => _schedulerFactory = schedulerFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        // Đăng ký calendar loại trừ ngày nghỉ
        var holidayCalendar = VietnamHolidayCalendar.Create(DateTime.UtcNow.Year);
        await scheduler.AddCalendar("VietnamHolidays", holidayCalendar,
            replace: true, updateTriggers: true, cancellationToken);

        // Đăng ký weekend calendar
        var weekendCalendar = new WeeklyCalendar();
        weekendCalendar.SetDayExcluded(DayOfWeek.Saturday, true);
        weekendCalendar.SetDayExcluded(DayOfWeek.Sunday, true);
        await scheduler.AddCalendar("Weekends", weekendCalendar,
            replace: true, updateTriggers: true, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

---

## 14. AdoJobStore - Lưu trữ bền vững

### 14.1 Cấu hình AdoJobStore với SQL Server

```csharp
// appsettings.Production.json
{
  "Quartz": {
    "quartz.scheduler.instanceName": "EcommerceScheduler",
    "quartz.scheduler.instanceId": "AUTO",
    "quartz.threadPool.maxConcurrency": "20",
    "quartz.jobStore.type": "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
    "quartz.jobStore.driverDelegateType": "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz",
    "quartz.jobStore.dataSource": "myDS",
    "quartz.jobStore.tablePrefix": "QRTZ_",
    "quartz.jobStore.clustered": "true",
    "quartz.jobStore.clusterCheckinInterval": "20000",
    "quartz.dataSource.myDS.provider": "SqlServer",
    "quartz.dataSource.myDS.connectionString": "Server=localhost;Database=QuartzJobs;Integrated Security=true;TrustServerCertificate=true;",
    "quartz.serializer.type": "json"
  }
}
```

```csharp
// Program.cs - Cấu hình AdoJobStore
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Đọc cấu hình từ appsettings
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

    if (builder.Environment.IsProduction())
    {
        // Production: SQL Server persistence
        q.UsePersistentStore(store =>
        {
            store.UseProperties      = true;
            store.RetryInterval      = TimeSpan.FromSeconds(15);
            store.UseSqlServer(
                builder.Configuration.GetConnectionString("QuartzConnection")!
            );
            store.UseJsonSerializer();
            store.UseClustering(c =>
            {
                c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                c.CheckinInterval         = TimeSpan.FromSeconds(10);
            });
        });
    }
    else
    {
        // Development: In-memory store
        q.UseInMemoryStore();
    }
});
```

---

## 15. Distributed Job Processing

### 15.1 Kiến trúc multi-server

```
┌───────────────────────────────────────────────────────────────────┐
│                   Distributed Job Processing                      │
│                                                                   │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐        │
│  │   App Server │    │   App Server │    │   App Server │        │
│  │    Node 1    │    │    Node 2    │    │    Node 3    │        │
│  │              │    │              │    │              │        │
│  │ ┌──────────┐ │    │ ┌──────────┐ │    │ ┌──────────┐ │        │
│  │ │ Hangfire │ │    │ │ Hangfire │ │    │ │ Hangfire │ │        │
│  │ │ Server   │ │    │ │ Server   │ │    │ │ Server   │ │        │
│  │ │ (5 workers│ │    │ │ (5 workers│ │    │ │ (5 workers│ │       │
│  │ └────┬─────┘ │    │ └────┬─────┘ │    │ └────┬─────┘ │        │
│  └──────┼───────┘    └──────┼───────┘    └──────┼───────┘        │
│         │                   │                    │                │
│         └───────────────────┼────────────────────┘                │
│                             │                                     │
│                             ▼                                     │
│              ┌──────────────────────────────┐                    │
│              │    SQL Server / Redis         │                    │
│              │    (Shared Job Storage)       │                    │
│              │                              │                    │
│              │  ┌────────────────────────┐  │                    │
│              │  │  critical queue: []    │  │                    │
│              │  │  default queue:  []    │  │                    │
│              │  │  low queue:      []    │  │                    │
│              │  └────────────────────────┘  │                    │
│              └──────────────────────────────┘                    │
└───────────────────────────────────────────────────────────────────┘
```

### 15.2 Cấu hình Queue Priority

```csharp
// Hangfire: Cấu hình queue ưu tiên cho mỗi server
builder.Services.AddHangfireServer(options =>
{
    // Server này xử lý critical và default queue
    options.Queues    = new[] { "critical", "default", "low" };
    options.WorkerCount = 10;
    options.ServerName  = $"primary-{Environment.MachineName}";
});

// Optionally: dedicated server cho critical jobs
builder.Services.AddHangfireServer(options =>
{
    options.Queues      = new[] { "critical" };
    options.WorkerCount = 5;
    options.ServerName  = $"critical-{Environment.MachineName}";
});
```

---

## 16. Dự án thực tế: E-commerce Background Jobs

### 16.1 Cấu trúc dự án

```
EcommerceApp/
├── Controllers/
│   └── OrderController.cs
├── Jobs/
│   ├── Hangfire/
│   │   ├── EmailJob.cs
│   │   └── CleanupJob.cs
│   └── Quartz/
│       ├── GenerateDailyRevenueReportJob.cs
│       └── ProcessPendingPaymentsJob.cs
├── Services/
│   ├── IEmailService.cs / EmailService.cs
│   ├── IReportingService.cs / ReportingService.cs
│   ├── IOrderService.cs / OrderService.cs
│   ├── ISessionService.cs / SessionService.cs
│   └── IPaymentService.cs / PaymentService.cs
├── Models/
│   ├── Order.cs
│   ├── Payment.cs
│   └── RevenueReport.cs
└── Filters/
    ├── HangfireAuthorizationFilter.cs
    ├── JobLoggingFilter.cs
    └── SmartRetryAttribute.cs
```

### 16.2 Models

```csharp
// Models/Order.cs
namespace EcommerceApp.Models;

public class Order
{
    public Guid     Id          { get; set; } = Guid.NewGuid();
    public string   CustomerId  { get; set; } = string.Empty;
    public string   CustomerEmail { get; set; } = string.Empty;
    public string   CustomerName  { get; set; } = string.Empty;
    public List<OrderItem> Items  { get; set; } = new();
    public decimal  TotalAmount   { get; set; }
    public OrderStatus Status     { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public string?  CancelJobId   { get; set; }
}

public class OrderItem
{
    public Guid    ProductId { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public int     Quantity   { get; set; }
    public decimal UnitPrice  { get; set; }
}

public enum OrderStatus { Pending, Paid, Processing, Shipped, Delivered, Cancelled }

// Models/Payment.cs
public class Payment
{
    public Guid     Id          { get; set; } = Guid.NewGuid();
    public Guid     OrderId     { get; set; }
    public decimal  Amount      { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string   Gateway     { get; set; } = string.Empty;
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public enum PaymentStatus { Pending, Processing, Completed, Failed, Refunded }

// Models/RevenueReport.cs
public class RevenueReport
{
    public Guid        Id          { get; set; } = Guid.NewGuid();
    public DateTime    ReportDate  { get; set; }
    public decimal     TotalRevenue { get; set; }
    public int         TotalOrders  { get; set; }
    public int         TotalItems   { get; set; }
    public decimal     AverageOrderValue { get; set; }
    public List<ProductRevenue> TopProducts { get; set; } = new();
    public DateTime    GeneratedAt  { get; set; } = DateTime.UtcNow;
}

public class ProductRevenue
{
    public Guid    ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public decimal Revenue     { get; set; }
    public int     UnitsSold   { get; set; }
}
```

### 16.3 Service Interfaces và Implementations

```csharp
// Services/IEmailService.cs
namespace EcommerceApp.Services;

public interface IEmailService
{
    Task SendOrderConfirmationAsync(Guid orderId, CancellationToken cancellationToken);
    Task SendPaymentReceiptAsync(Guid paymentId, CancellationToken cancellationToken);
    Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken);
    Task SendMarketingEmailAsync(string userId, Guid campaignId, CancellationToken cancellationToken);
}

// Services/EmailService.cs
using EcommerceApp.Models;

namespace EcommerceApp.Services;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly IOrderService         _orderService;
    // Trong thực tế sẽ inject ISmtpClient hoặc SendGrid client

    public EmailService(ILogger<EmailService> logger, IOrderService orderService)
    {
        _logger       = logger;
        _orderService = orderService;
    }

    public async Task SendOrderConfirmationAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        _logger.LogInformation(
            "Sending order confirmation email to {Email} for order {OrderId}",
            order.CustomerEmail, orderId);

        // TODO: Integrate với email provider (SendGrid, MailKit, etc.)
        // Stub implementation:
        await Task.Delay(500, cancellationToken); // Simulate async email sending

        var emailBody = BuildOrderConfirmationEmail(order);

        _logger.LogInformation(
            "Order confirmation email sent successfully to {Email}",
            order.CustomerEmail);
    }

    public async Task SendPaymentReceiptAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending payment receipt for payment {PaymentId}", paymentId);
        await Task.Delay(300, cancellationToken);
        _logger.LogInformation("Payment receipt sent for {PaymentId}", paymentId);
    }

    public async Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending welcome email to {Email}", email);
        await Task.Delay(300, cancellationToken);
    }

    public async Task SendMarketingEmailAsync(string userId, Guid campaignId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Sending marketing email for campaign {CampaignId} to user {UserId}",
            campaignId, userId);
        await Task.Delay(200, cancellationToken);
    }

    private static string BuildOrderConfirmationEmail(Order order)
    {
        var items = string.Join("\n", order.Items.Select(i =>
            $"  - {i.ProductName} x{i.Quantity}: {i.UnitPrice * i.Quantity:C}"));

        return $"""
            Xin chào {order.CustomerName},

            Cảm ơn bạn đã đặt hàng! Đơn hàng #{order.Id} đã được xác nhận.

            Chi tiết đơn hàng:
            {items}

            Tổng cộng: {order.TotalAmount:C}

            Trân trọng,
            Ecommerce Team
            """;
    }
}
```

```csharp
// Services/IReportingService.cs
namespace EcommerceApp.Services;

public interface IReportingService
{
    Task<RevenueReport> GenerateDailyRevenueReportAsync(
        DateTime date, CancellationToken cancellationToken);

    Task SaveReportAsync(RevenueReport report, CancellationToken cancellationToken);
    Task SendReportToStakeholdersAsync(Guid reportId, CancellationToken cancellationToken);
}

// Services/ReportingService.cs
using EcommerceApp.Models;

namespace EcommerceApp.Services;

public class ReportingService : IReportingService
{
    private readonly ILogger<ReportingService>  _logger;
    private readonly IOrderRepository           _orderRepository;
    // Inject DB context, blob storage, etc. in real implementation

    public ReportingService(
        ILogger<ReportingService> logger,
        IOrderRepository orderRepository)
    {
        _logger          = logger;
        _orderRepository = orderRepository;
    }

    public async Task<RevenueReport> GenerateDailyRevenueReportAsync(
        DateTime date, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating daily revenue report for {Date}", date.ToShortDateString());

        var startOfDay = date.Date;
        var endOfDay   = startOfDay.AddDays(1).AddTicks(-1);

        var orders = await _orderRepository.GetOrdersByDateRangeAsync(
            startOfDay, endOfDay, cancellationToken);

        var paidOrders = orders.Where(o => o.Status == OrderStatus.Paid).ToList();

        var report = new RevenueReport
        {
            ReportDate       = date.Date,
            TotalOrders      = paidOrders.Count,
            TotalRevenue     = paidOrders.Sum(o => o.TotalAmount),
            TotalItems       = paidOrders.Sum(o => o.Items.Sum(i => i.Quantity)),
            AverageOrderValue = paidOrders.Count > 0
                ? paidOrders.Average(o => o.TotalAmount) : 0,
            TopProducts = paidOrders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.ProductId)
                .Select(g => new ProductRevenue
                {
                    ProductId   = g.Key,
                    ProductName = g.First().ProductName,
                    UnitsSold   = g.Sum(i => i.Quantity),
                    Revenue     = g.Sum(i => i.Quantity * i.UnitPrice)
                })
                .OrderByDescending(p => p.Revenue)
                .Take(10)
                .ToList()
        };

        _logger.LogInformation(
            "Daily report generated: {Orders} orders, {Revenue:C} total revenue",
            report.TotalOrders, report.TotalRevenue);

        return report;
    }

    public async Task SaveReportAsync(RevenueReport report, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving revenue report {ReportId}", report.Id);
        // Save to database, blob storage, etc.
        await Task.Delay(100, cancellationToken);
        _logger.LogInformation("Report {ReportId} saved successfully", report.Id);
    }

    public async Task SendReportToStakeholdersAsync(Guid reportId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending report {ReportId} to stakeholders", reportId);
        // Send email with report attachment
        await Task.Delay(500, cancellationToken);
        _logger.LogInformation("Report {ReportId} sent to stakeholders", reportId);
    }
}
```

```csharp
// Services/ISessionService.cs
namespace EcommerceApp.Services;

public interface ISessionService
{
    Task<int> DeleteExpiredSessionsAsync(
        TimeSpan olderThan, CancellationToken cancellationToken);
}

// Services/SessionService.cs
namespace EcommerceApp.Services;

public class SessionService : ISessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly IDbContext              _db;

    public SessionService(ILogger<SessionService> logger, IDbContext db)
    {
        _logger = logger;
        _db     = db;
    }

    public async Task<int> DeleteExpiredSessionsAsync(
        TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        _logger.LogInformation(
            "Deleting sessions older than {Cutoff}", cutoff);

        // Stub: trong thực tế gọi DB
        var deletedCount = Random.Shared.Next(0, 50);
        await Task.Delay(100, cancellationToken);

        _logger.LogInformation("Deleted {Count} expired sessions", deletedCount);
        return deletedCount;
    }
}
```

```csharp
// Services/IPaymentService.cs
namespace EcommerceApp.Services;

public interface IPaymentService
{
    Task<IEnumerable<Payment>> GetPendingPaymentsAsync(
        int batchSize, CancellationToken cancellationToken);

    Task<PaymentResult> ProcessPaymentAsync(
        Payment payment, CancellationToken cancellationToken);
}

public record PaymentResult(bool Success, string? ErrorMessage);

// Services/PaymentService.cs
using EcommerceApp.Models;

namespace EcommerceApp.Services;

public class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(ILogger<PaymentService> logger) => _logger = logger;

    public async Task<IEnumerable<Payment>> GetPendingPaymentsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching up to {BatchSize} pending payments", batchSize);
        await Task.Delay(100, cancellationToken);
        // Stub: return empty list
        return Array.Empty<Payment>();
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        Payment payment, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing payment {PaymentId}", payment.Id);
        await Task.Delay(500, cancellationToken);

        // Simulate 95% success rate
        var success = Random.Shared.NextDouble() > 0.05;
        return success
            ? new PaymentResult(true, null)
            : new PaymentResult(false, "Gateway timeout");
    }
}
```

### 16.4 Hangfire Jobs

```csharp
// Jobs/Hangfire/EmailJob.cs
using Hangfire;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs.Hangfire;

/// <summary>
/// Hangfire job: Gửi email xác nhận đơn hàng
/// Fire-and-forget từ OrderController
/// </summary>
public class EmailJob
{
    private readonly IEmailService       _emailService;
    private readonly ILogger<EmailJob>   _logger;

    public EmailJob(IEmailService emailService, ILogger<EmailJob> logger)
    {
        _emailService = emailService;
        _logger       = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    [Queue("default")]
    public async Task SendOrderConfirmationEmail(Guid orderId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "EmailJob: Sending order confirmation for order {OrderId}", orderId);

        await _emailService.SendOrderConfirmationAsync(orderId, cancellationToken);
    }

    [AutomaticRetry(Attempts = 2)]
    [Queue("low")]
    public async Task SendMarketingEmail(
        string userId, Guid campaignId, CancellationToken cancellationToken)
    {
        await _emailService.SendMarketingEmailAsync(userId, campaignId, cancellationToken);
    }
}
```

```csharp
// Jobs/Hangfire/CleanupJob.cs
using Hangfire;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs.Hangfire;

/// <summary>
/// Hangfire recurring job: Dọn dẹp session hết hạn
/// Chạy mỗi giờ
/// </summary>
public class CleanupJob
{
    private readonly ISessionService       _sessionService;
    private readonly ILogger<CleanupJob>   _logger;

    public CleanupJob(ISessionService sessionService, ILogger<CleanupJob> logger)
    {
        _sessionService = sessionService;
        _logger         = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    [Queue("low")]
    public async Task CleanupExpiredSessions(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "CleanupJob: Starting expired session cleanup at {Time}", DateTime.UtcNow);

        var deleted = await _sessionService.DeleteExpiredSessionsAsync(
            olderThan: TimeSpan.FromHours(24),
            cancellationToken: cancellationToken
        );

        _logger.LogInformation(
            "CleanupJob: Completed. Deleted {Count} expired sessions", deleted);
    }
}
```

### 16.5 Quartz.NET Jobs

```csharp
// Jobs/Quartz/GenerateDailyRevenueReportJob.cs
using Quartz;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs.Quartz;

/// <summary>
/// Quartz.NET job: Tạo báo cáo doanh thu hàng ngày
/// Trigger: Cron "0 0 0 * * ?" - lúc 00:00:00 mỗi ngày
/// </summary>
[DisallowConcurrentExecution] // Không cho phép chạy song song
[PersistJobDataAfterExecution] // Lưu JobDataMap sau mỗi lần chạy
public class GenerateDailyRevenueReportJob : IJob
{
    private readonly IReportingService                   _reportingService;
    private readonly IEmailService                       _emailService;
    private readonly ILogger<GenerateDailyRevenueReportJob> _logger;

    public GenerateDailyRevenueReportJob(
        IReportingService reportingService,
        IEmailService emailService,
        ILogger<GenerateDailyRevenueReportJob> logger)
    {
        _reportingService = reportingService;
        _emailService     = emailService;
        _logger           = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var reportDate = context.JobDetail.JobDataMap.ContainsKey("ReportDate")
            ? DateTime.Parse(context.JobDetail.JobDataMap.GetString("ReportDate")!)
            : DateTime.UtcNow.AddDays(-1); // Báo cáo ngày hôm qua

        _logger.LogInformation(
            "GenerateDailyRevenueReportJob: Generating report for {Date}",
            reportDate.ToShortDateString());

        try
        {
            // Bước 1: Tạo báo cáo
            var report = await _reportingService.GenerateDailyRevenueReportAsync(
                reportDate, context.CancellationToken);

            // Bước 2: Lưu báo cáo
            await _reportingService.SaveReportAsync(report, context.CancellationToken);

            // Bước 3: Gửi báo cáo cho stakeholders
            await _reportingService.SendReportToStakeholdersAsync(
                report.Id, context.CancellationToken);

            // Lưu thông tin vào JobDataMap để theo dõi
            context.JobDetail.JobDataMap["LastSuccessfulRun"] = DateTime.UtcNow.ToString("O");
            context.JobDetail.JobDataMap["LastReportId"]      = report.Id.ToString();

            _logger.LogInformation(
                "GenerateDailyRevenueReportJob: Completed successfully. Report: {ReportId}",
                report.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateDailyRevenueReportJob: Failed for date {Date}",
                reportDate.ToShortDateString());

            // Throw để Quartz biết job thất bại
            throw new JobExecutionException(msg: ex.Message, cause: ex, refireImmediately: false);
        }
    }
}
```

```csharp
// Jobs/Quartz/ProcessPendingPaymentsJob.cs
using Quartz;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs.Quartz;

/// <summary>
/// Quartz.NET batch job: Xử lý các payment đang pending
/// Trigger: Mỗi 5 phút
/// </summary>
[DisallowConcurrentExecution]
public class ProcessPendingPaymentsJob : IJob
{
    private const int BatchSize = 50; // Xử lý 50 payment mỗi lần

    private readonly IPaymentService                       _paymentService;
    private readonly IEmailService                         _emailService;
    private readonly ILogger<ProcessPendingPaymentsJob>    _logger;

    public ProcessPendingPaymentsJob(
        IPaymentService paymentService,
        IEmailService emailService,
        ILogger<ProcessPendingPaymentsJob> logger)
    {
        _paymentService = paymentService;
        _emailService   = emailService;
        _logger         = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation(
            "ProcessPendingPaymentsJob: Starting batch processing at {Time}", DateTime.UtcNow);

        var pendingPayments = await _paymentService.GetPendingPaymentsAsync(
            BatchSize, context.CancellationToken);

        var paymentList   = pendingPayments.ToList();
        var successCount  = 0;
        var failureCount  = 0;

        foreach (var payment in paymentList)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ProcessPendingPaymentsJob: Cancelled after {Count} payments",
                    successCount + failureCount);
                break;
            }

            try
            {
                var result = await _paymentService.ProcessPaymentAsync(
                    payment, context.CancellationToken);

                if (result.Success)
                {
                    successCount++;
                    // Gửi receipt
                    await _emailService.SendPaymentReceiptAsync(
                        payment.Id, context.CancellationToken);
                }
                else
                {
                    failureCount++;
                    _logger.LogWarning(
                        "Payment {PaymentId} failed: {Error}",
                        payment.Id, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Error processing payment {PaymentId}", payment.Id);
            }
        }

        _logger.LogInformation(
            "ProcessPendingPaymentsJob: Completed. Success: {Success}, Failed: {Failed}, Total: {Total}",
            successCount, failureCount, paymentList.Count);
    }
}
```

### 16.6 OrderController với đầy đủ tính năng

```csharp
// Controllers/OrderController.cs
using Hangfire;
using EcommerceApp.Jobs.Hangfire;
using EcommerceApp.Models;
using EcommerceApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IOrderService           _orderService;
    private readonly IBackgroundJobClient    _backgroundJobs;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        IOrderService orderService,
        IBackgroundJobClient backgroundJobs,
        ILogger<OrderController> logger)
    {
        _orderService   = orderService;
        _backgroundJobs = backgroundJobs;
        _logger         = logger;
    }

    /// <summary>
    /// Tạo đơn hàng mới và kích hoạt chuỗi background jobs
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        // Tạo đơn hàng (đồng bộ - phải hoàn thành trước khi response)
        var order = await _orderService.CreateOrderAsync(request);

        // ─── Fire-and-Forget: Gửi email xác nhận ──────────────────
        // Người dùng không cần chờ email được gửi
        var emailJobId = _backgroundJobs.Enqueue<EmailJob>(
            job => job.SendOrderConfirmationEmail(order.Id, CancellationToken.None)
        );

        // ─── Delayed Job: Hủy đơn nếu chưa thanh toán sau 30 phút ─
        var cancelJobId = _backgroundJobs.Schedule<IOrderService>(
            svc => svc.CancelIfUnpaidAsync(order.Id, CancellationToken.None),
            TimeSpan.FromMinutes(30)
        );

        // Lưu cancelJobId để xóa khi đã thanh toán
        await _orderService.SaveCancelJobIdAsync(order.Id, cancelJobId);

        _logger.LogInformation(
            "Order {OrderId} created. EmailJob: {EmailJobId}, CancelJob: {CancelJobId}",
            order.Id, emailJobId, cancelJobId);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, new
        {
            Order     = order,
            EmailJobId  = emailJobId,
            CancelJobId = cancelJobId
        });
    }

    /// <summary>
    /// Xác nhận thanh toán và kích hoạt chuỗi fulfillment jobs
    /// </summary>
    [HttpPost("{orderId}/payment")]
    public async Task<IActionResult> ConfirmPayment(
        Guid orderId, [FromBody] PaymentConfirmationRequest request)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order is null) return NotFound();

        // Xóa job hủy đơn vì đã thanh toán
        if (order.CancelJobId is not null)
        {
            BackgroundJob.Delete(order.CancelJobId);
            _logger.LogInformation(
                "Cancelled job {JobId} for order {OrderId} (payment confirmed)",
                order.CancelJobId, orderId);
        }

        // Cập nhật trạng thái đơn hàng
        await _orderService.MarkAsPaidAsync(orderId);

        // ─── Job Continuation Chain ───────────────────────────────
        // Job 1: Gửi receipt
        var receiptJobId = _backgroundJobs.Enqueue<IEmailService>(
            svc => svc.SendPaymentReceiptAsync(request.PaymentId, CancellationToken.None),
            new EnqueuedState("critical")
        );

        // Job 2: Thông báo kho hàng (sau khi receipt gửi xong)
        var warehouseJobId = _backgroundJobs.ContinueJobWith<IOrderService>(
            parentId: receiptJobId,
            methodCall: svc => svc.NotifyWarehouseAsync(orderId, CancellationToken.None)
        );

        // Job 3: Cập nhật điểm thưởng (sau khi kho nhận được thông báo)
        _backgroundJobs.ContinueJobWith<ILoyaltyService>(
            parentId: warehouseJobId,
            methodCall: svc => svc.AwardPointsForOrderAsync(orderId, CancellationToken.None)
        );

        return Ok(new { Message = "Payment confirmed and fulfillment started" });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }
}
```

---

## 17. Job Monitoring và Retry Policies

### 17.1 Luồng xử lý Job và Retry

```
┌─────────────────────────────────────────────────────────────────┐
│                    Job Processing Flow                          │
│                                                                 │
│  [Enqueued] ──► [Processing] ──► [Succeeded]                   │
│                      │                                          │
│                      ▼ (on error)                               │
│                  [Failed]                                        │
│                      │                                          │
│            ┌─────────┴──────────┐                               │
│            │                    │                               │
│            ▼                    ▼                               │
│    [Retry (scheduled)]    [Dead Letter]                         │
│         │                   (Deleted)                           │
│         │ (after delay)                                         │
│         ▼                                                       │
│    [Processing] ──► [Succeeded]                                 │
│         │                                                       │
│         ▼ (if max retries exceeded)                             │
│    [Dead Letter]                                                │
└─────────────────────────────────────────────────────────────────┘
```

### 17.2 Dead Letter Queue Handler

```csharp
// Services/DeadLetterHandlerService.cs
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace EcommerceApp.Services;

/// <summary>
/// Service xử lý các job đã vào Dead Letter Queue
/// </summary>
public class DeadLetterHandlerService : IHostedService, IDisposable
{
    private readonly IBackgroundJobClientFactory _jobClientFactory;
    private readonly IMonitoringApi              _monitoringApi;
    private readonly ILogger<DeadLetterHandlerService> _logger;
    private Timer?   _timer;

    public DeadLetterHandlerService(
        IBackgroundJobClientFactory jobClientFactory,
        JobStorage jobStorage,
        ILogger<DeadLetterHandlerService> logger)
    {
        _jobClientFactory = jobClientFactory;
        _monitoringApi    = jobStorage.GetMonitoringApi();
        _logger           = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Kiểm tra dead letter queue mỗi giờ
        _timer = new Timer(CheckDeadLetterQueue, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
        return Task.CompletedTask;
    }

    private void CheckDeadLetterQueue(object? state)
    {
        try
        {
            // Lấy danh sách jobs đã bị xóa (dead)
            var deletedJobs = _monitoringApi.DeletedJobs(0, 100);

            _logger.LogWarning(
                "Dead Letter Queue: {Count} failed jobs found", deletedJobs.Count);

            foreach (var (jobId, job) in deletedJobs)
            {
                _logger.LogError(
                    "Dead job {JobId}: {MethodName} - Last state change: {StateChanged}",
                    jobId,
                    job.Job?.Method.Name ?? "Unknown",
                    job.DeletedAt);

                // Gửi alert (email, Slack, PagerDuty, etc.)
                // _alertService.SendAlert($"Dead job: {jobId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking dead letter queue");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
```

### 17.3 Health Check cho Background Jobs

```csharp
// HealthChecks/HangfireHealthCheck.cs
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EcommerceApp.HealthChecks;

public class HangfireHealthCheck : IHealthCheck
{
    private readonly IMonitoringApi _monitoringApi;

    public HangfireHealthCheck(JobStorage jobStorage)
        => _monitoringApi = jobStorage.GetMonitoringApi();

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stats = _monitoringApi.GetStatistics();

        // Cảnh báo nếu queue quá lớn
        if (stats.Enqueued > 10_000)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Job queue is large: {stats.Enqueued} enqueued jobs"));
        }

        // Lỗi nếu có quá nhiều job thất bại
        if (stats.Failed > 100)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Too many failed jobs: {stats.Failed}"));
        }

        var data = new Dictionary<string, object>
        {
            ["Enqueued"] = stats.Enqueued,
            ["Processing"] = stats.Processing,
            ["Succeeded"] = stats.Succeeded,
            ["Failed"]    = stats.Failed,
            ["Servers"]   = stats.Servers
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            "Hangfire is healthy", data));
    }
}
```

---

## 18. Outbox Pattern với Background Jobs

### 18.1 Outbox Pattern là gì?

**Outbox Pattern** giải quyết vấn đề **dual write**: khi bạn cần đồng thời:
1. Lưu dữ liệu vào database
2. Publish message/event ra ngoài (message broker, email, webhook)

Nếu bước 2 thất bại sau khi bước 1 đã commit, dữ liệu sẽ không nhất quán. Outbox Pattern giải quyết bằng cách:
- Lưu message vào **cùng transaction** với dữ liệu business
- Background job đọc và publish các message chưa được gửi

### 18.2 Luồng Outbox Pattern

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Outbox Pattern Flow                            │
│                                                                     │
│  ┌──────────────┐                                                   │
│  │ API Handler  │                                                   │
│  │              │  ┌─────────────────────────────────────────────┐ │
│  │ 1. Update DB │  │         Database Transaction                │ │
│  │ 2. Write to  │─▶│  ┌──────────────┐  ┌───────────────────┐  │ │
│  │    Outbox    │  │  │ Orders Table │  │  OutboxMessages   │  │ │
│  └──────────────┘  │  │              │  │  Table            │  │ │
│                    │  │ Order #123   │  │  - Id             │  │ │
│                    │  │ Status: Paid │  │  - Type           │  │ │
│                    │  │              │  │  - Payload (JSON) │  │ │
│                    │  └──────────────┘  │  - ProcessedAt    │  │ │
│                    └──────────┬──────────┴───────────────────┘  │ │
│                               │ COMMIT                           │ │
│                               │                                   │
│  ┌────────────────────────────▼──────────────────────────────┐   │
│  │              Outbox Processor (Hangfire Job)               │   │
│  │  Poll every 30s                                            │   │
│  │  1. Read unprocessed messages (ProcessedAt IS NULL)        │   │
│  │  2. Publish to message broker / send email / call webhook  │   │
│  │  3. Mark as processed (ProcessedAt = NOW())                │   │
│  └────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 18.3 Implementation

```csharp
// Models/OutboxMessage.cs
namespace EcommerceApp.Models;

public class OutboxMessage
{
    public Guid     Id           { get; set; } = Guid.NewGuid();
    public string   Type         { get; set; } = string.Empty; // Event type
    public string   Payload      { get; set; } = string.Empty; // JSON payload
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string?  Error        { get; set; }
    public int      RetryCount   { get; set; }
}
```

```csharp
// Services/IOutboxService.cs
using EcommerceApp.Models;

namespace EcommerceApp.Services;

public interface IOutboxService
{
    Task AddMessageAsync<T>(T @event, CancellationToken cancellationToken)
        where T : class;

    Task<IEnumerable<OutboxMessage>> GetUnprocessedAsync(
        int batchSize, CancellationToken cancellationToken);

    Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken);
}
```

```csharp
// Services/OutboxService.cs
using System.Text.Json;
using EcommerceApp.Models;

namespace EcommerceApp.Services;

public class OutboxService : IOutboxService
{
    private readonly IDbContext                _db;
    private readonly ILogger<OutboxService>    _logger;

    public OutboxService(IDbContext db, ILogger<OutboxService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task AddMessageAsync<T>(T @event, CancellationToken cancellationToken)
        where T : class
    {
        var message = new OutboxMessage
        {
            Type    = typeof(T).Name,
            Payload = JsonSerializer.Serialize(@event),
        };

        await _db.OutboxMessages.AddAsync(message, cancellationToken);
        // Không SaveChanges ở đây - để caller commit cùng transaction
    }

    public async Task<IEnumerable<OutboxMessage>> GetUnprocessedAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        return await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < 5)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _db.OutboxMessages.FindAsync(new[] { (object)messageId }, cancellationToken);
        if (message is not null)
        {
            message.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken)
    {
        var message = await _db.OutboxMessages.FindAsync(new[] { (object)messageId }, cancellationToken);
        if (message is not null)
        {
            message.RetryCount++;
            message.Error = error;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
```

```csharp
// Jobs/Hangfire/OutboxProcessorJob.cs
using Hangfire;
using System.Text.Json;
using EcommerceApp.Services;

namespace EcommerceApp.Jobs.Hangfire;

/// <summary>
/// Hangfire recurring job: Xử lý Outbox messages
/// Chạy mỗi 30 giây để đảm bảo message được publish
/// </summary>
public class OutboxProcessorJob
{
    private const int BatchSize = 100;

    private readonly IOutboxService                   _outboxService;
    private readonly IMessagePublisher                _messagePublisher;
    private readonly ILogger<OutboxProcessorJob>      _logger;

    public OutboxProcessorJob(
        IOutboxService outboxService,
        IMessagePublisher messagePublisher,
        ILogger<OutboxProcessorJob> logger)
    {
        _outboxService    = outboxService;
        _messagePublisher = messagePublisher;
        _logger           = logger;
    }

    [AutomaticRetry(Attempts = 0)] // Không retry - job tự handle retry
    [DisableConcurrentExecution(timeoutInSeconds: 60)] // Không chạy song song
    [Queue("default")]
    public async Task ProcessOutboxMessages(CancellationToken cancellationToken)
    {
        var messages = await _outboxService.GetUnprocessedAsync(BatchSize, cancellationToken);
        var messageList = messages.ToList();

        if (!messageList.Any())
        {
            _logger.LogDebug("OutboxProcessor: No messages to process");
            return;
        }

        _logger.LogInformation(
            "OutboxProcessor: Processing {Count} messages", messageList.Count);

        var successCount = 0;
        var failureCount = 0;

        foreach (var message in messageList)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await _messagePublisher.PublishAsync(
                    message.Type, message.Payload, cancellationToken);

                await _outboxService.MarkAsProcessedAsync(message.Id, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex,
                    "OutboxProcessor: Failed to publish message {MessageId} of type {Type}",
                    message.Id, message.Type);

                await _outboxService.MarkAsFailedAsync(
                    message.Id, ex.Message, cancellationToken);
            }
        }

        _logger.LogInformation(
            "OutboxProcessor: Completed. Success: {Success}, Failed: {Failed}",
            successCount, failureCount);
    }
}
```

```csharp
// OrderService với Outbox Pattern
// Services/OrderService.cs
using EcommerceApp.Events;
using EcommerceApp.Models;
using EcommerceApp.Services;

namespace EcommerceApp.Services;

public class OrderService : IOrderService
{
    private readonly IDbContext          _db;
    private readonly IOutboxService      _outboxService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IDbContext db,
        IOutboxService outboxService,
        ILogger<OrderService> logger)
    {
        _db            = db;
        _outboxService = outboxService;
        _logger        = logger;
    }

    public async Task<Order> CreateOrderAsync(
        CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = new Order
        {
            CustomerId    = request.CustomerId,
            CustomerEmail = request.CustomerEmail,
            CustomerName  = request.CustomerName,
            Items         = request.Items.Select(i => new OrderItem
            {
                ProductId   = i.ProductId,
                ProductName = i.ProductName,
                Quantity    = i.Quantity,
                UnitPrice   = i.UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);

        // ─── Atomic: Lưu order VÀ outbox message trong cùng transaction ───
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _db.Orders.AddAsync(order, cancellationToken);

            // Thêm outbox message (cùng transaction - đảm bảo nhất quán)
            await _outboxService.AddMessageAsync(new OrderCreatedEvent
            {
                OrderId       = order.Id,
                CustomerId    = order.CustomerId,
                TotalAmount   = order.TotalAmount,
                OccurredAt    = DateTime.UtcNow
            }, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Order {OrderId} created with outbox message", order.Id);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return order;
    }
}

// Events/OrderCreatedEvent.cs
public record OrderCreatedEvent
{
    public Guid     OrderId    { get; init; }
    public string   CustomerId { get; init; } = string.Empty;
    public decimal  TotalAmount { get; init; }
    public DateTime OccurredAt { get; init; }
}
```

---

## 19. Best Practices

### 19.1 Thiết kế Job

```
✅ DO - Nên làm:

1. Idempotent Jobs: Job có thể chạy nhiều lần với cùng kết quả
   - Kiểm tra trạng thái trước khi thực thi
   - Sử dụng unique constraints trong DB

2. Small & Focused Jobs:
   - Mỗi job làm một việc duy nhất
   - Không làm quá nhiều việc trong một job

3. Proper Error Handling:
   - Catch exception cụ thể, không catch Exception chung chung
   - Log đủ thông tin để debug
   - Phân biệt recoverable vs non-recoverable errors

4. CancellationToken:
   - Luôn accept CancellationToken trong job method
   - Pass token qua tất cả async operations

5. Serialization-safe Parameters:
   - Chỉ pass ID (Guid, int, string) vào job
   - Không pass complex objects (có thể serialize sai)

❌ DON'T - Không nên:

1. Không pass DbContext vào job (vì job có DI scope riêng)
2. Không dùng static state trong job
3. Không để job chạy quá lâu mà không check cancellation
4. Không bỏ qua retry mechanism
```

### 19.2 Idempotent Job Example

```csharp
// Jobs/Hangfire/IdempotentEmailJob.cs
namespace EcommerceApp.Jobs.Hangfire;

public class IdempotentEmailJob
{
    private readonly IEmailService       _emailService;
    private readonly IEmailLogRepository _emailLog;
    private readonly ILogger<IdempotentEmailJob> _logger;

    public IdempotentEmailJob(
        IEmailService emailService,
        IEmailLogRepository emailLog,
        ILogger<IdempotentEmailJob> logger)
    {
        _emailService = emailService;
        _emailLog     = emailLog;
        _logger       = logger;
    }

    public async Task SendOrderConfirmation(Guid orderId, CancellationToken cancellationToken)
    {
        // ─── Idempotency Check ─────────────────────────────────────
        // Kiểm tra xem email đã được gửi chưa (tránh gửi trùng lặp)
        var alreadySent = await _emailLog.ExistsAsync(
            $"order-confirmation-{orderId}", cancellationToken);

        if (alreadySent)
        {
            _logger.LogInformation(
                "Email for order {OrderId} already sent, skipping", orderId);
            return;
        }

        // Gửi email
        await _emailService.SendOrderConfirmationAsync(orderId, cancellationToken);

        // Ghi log để tránh gửi lại
        await _emailLog.RecordAsync(
            $"order-confirmation-{orderId}",
            DateTime.UtcNow,
            cancellationToken);
    }
}
```

### 19.3 Monitoring Checklist

```
□ Cấu hình alerts cho Failed job count vượt ngưỡng
□ Dashboard accessible chỉ cho authorized users
□ Logging đầy đủ: job start, end, duration, errors
□ Health check endpoint cho Hangfire/Quartz
□ Dead letter queue được xử lý định kỳ
□ Job execution time được monitor (tránh timeout)
□ Queue depth được monitor (tránh backlog)
□ Server count được monitor (auto-scaling triggers)
```

### 19.4 Security Best Practices

```csharp
// Bảo vệ Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Chỉ cho phép HTTPS
    Authorization = new[] { new HangfireAuthorizationFilter("Admin") },
    // Đổi path default để giảm rủi ro scan
    // "/hangfire" → "/internal/jobs" (ít phổ biến hơn)
    AppPath = "/admin" // Back link URL
});

// Chống CSRF cho Dashboard
app.UseAntiforgery();
```

---

## 20. Tổng kết

### 20.1 Khi nào dùng gì?

```
┌────────────────────┬──────────────────────┬──────────────────────────┐
│      Tình huống    │     Dùng Hangfire    │      Dùng Quartz.NET     │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Fire-and-forget    │ ✅ BackgroundJob.     │ ❌ Quá phức tạp          │
│                    │    Enqueue()          │                          │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Delayed execution  │ ✅ BackgroundJob.     │ ⚠️ Được, nhưng phức tạp  │
│                    │    Schedule()         │                          │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Simple recurring   │ ✅ RecurringJob.      │ ✅ Cả hai đều phù hợp    │
│                    │    AddOrUpdate()      │                          │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Complex cron       │ ⚠️ Chỉ basic cron    │ ✅ Full cron support      │
│ schedules          │                       │    + Calendars           │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Job dependencies   │ ✅ ContinueJobWith()  │ ❌ Không có built-in     │
│ (chaining)         │                       │                          │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Batch processing   │ ✅ Hangfire Pro       │ ✅ DisallowConcurrent    │
│                    │    (paid)             │    + trigger             │
├────────────────────┼──────────────────────┼──────────────────────────┤
│ Enterprise         │ ✅ Dashboard +        │ ✅ Clustering +           │
│ features           │    monitoring         │    CalDAV                │
└────────────────────┴──────────────────────┴──────────────────────────┘
```

### 20.2 Checklist trước khi đưa vào production

```
□ Jobs được thiết kế idempotent
□ CancellationToken được truyền qua tất cả async calls
□ Retry policy phù hợp với từng loại job
□ Dead letter handling được implement
□ Dashboard được bảo vệ bằng authentication
□ Health checks được thiết lập
□ Alerting được cấu hình
□ Job parameters chỉ chứa IDs, không chứa objects phức tạp
□ Outbox pattern được dùng cho critical message publishing
□ Distributed locking nếu chạy multi-server
□ Job execution time được giám sát
□ Timezone được xác định rõ ràng (không dùng UTC mù quáng)
```

### 20.3 Tài liệu tham khảo

- [Hangfire Documentation](https://docs.hangfire.io)
- [Quartz.NET Documentation](https://www.quartz-scheduler.net/documentation)
- [Outbox Pattern - Microsoft](https://learn.microsoft.com/en-us/azure/architecture/best-practices/transactional-outbox-cosmos)
- [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)

---

*Bài tiếp theo: [Bài 19: Message Queues với RabbitMQ và MassTransit](./19-message-queues.md)*
