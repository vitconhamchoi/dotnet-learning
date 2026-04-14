# High-throughput Event Streaming với Kafka: Xây dựng pipeline xử lý hàng triệu event mỗi giây

## 1. Kafka là gì và khi nào nên dùng thay vì RabbitMQ/MassTransit

Apache Kafka là distributed event streaming platform được thiết kế cho throughput cực cao, durability và khả năng replay. Nó không phải message queue truyền thống như RabbitMQ.

**Điểm khác biệt cốt lõi**:

| Feature | RabbitMQ / MassTransit | Kafka |
|---------|------------------------|-------|
| Mô hình | Message queue (push) | Event log (pull) |
| Throughput | 100K msgs/s | 1M+ msgs/s |
| Retention | Message bị xóa sau khi consumed | Giữ message theo retention policy (ngày/GB) |
| Replay | Không (sau khi ack) | Có - consumer có thể đọc lại từ offset bất kỳ |
| Consumer | Competing consumers | Consumer groups, mỗi group đọc độc lập |
| Ordering | Per-queue | Per-partition |
| Use case | Task queue, RPC, saga | Event streaming, log aggregation, CDC |

**Dùng Kafka khi**:
- Cần throughput cực cao (triệu events/giây)
- Nhiều consumer độc lập cần đọc cùng event stream
- Cần replay event (analytics, audit, debug)
- Event-driven architecture với CDC (Change Data Capture)
- Real-time data pipeline

**Dùng RabbitMQ khi**:
- Task queue với complex routing
- Request/reply pattern
- Saga với complex state machine
- Throughput vừa phải, latency quan trọng

---

## 2. Kafka Fundamentals cho .NET developer

### 2.1 Topic, Partition, Offset

```text
Topic: "order-events"
├── Partition 0: [offset 0: OrderPlaced] [offset 1: OrderApproved] [offset 2: OrderCancelled]
├── Partition 1: [offset 0: OrderPlaced] [offset 1: OrderShipped]
└── Partition 2: [offset 0: OrderPlaced] [offset 1: OrderPlaced]

Consumer Group "notification-service":
├── Consumer A → Partition 0 (reads up to offset 2)
├── Consumer B → Partition 1 (reads up to offset 1)
└── Consumer C → Partition 2 (reads up to offset 0)
```

- **Topic**: named log stream, chia thành partitions
- **Partition**: ordered, immutable sequence. Unit of parallelism
- **Offset**: position trong partition. Consumer tự track offset
- **Consumer Group**: nhóm consumer cùng đọc topic, Kafka cân bằng partition giữa các consumer

### 2.2 Partition Key: quyết định ordering

Kafka đảm bảo ordering PER PARTITION. Để đảm bảo events cho một entity được xử lý theo thứ tự, dùng entity ID làm partition key.

```csharp
// Tất cả events của order ABC sẽ vào cùng partition
// Đảm bảo OrderPlaced → OrderApproved → OrderShipped được xử lý theo thứ tự
var message = new Message<string, string>
{
    Key = orderId.ToString(),  // Partition key = order ID
    Value = JsonSerializer.Serialize(orderEvent),
    Headers = new Headers
    {
        { "event-type", Encoding.UTF8.GetBytes(orderEvent.GetType().Name) },
        { "correlation-id", Encoding.UTF8.GetBytes(correlationId) },
        { "produced-at", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) }
    }
};
```

---

## 3. Setup Kafka Producer trong .NET

```bash
dotnet add package Confluent.Kafka
dotnet add package Confluent.SchemaRegistry.Serdes.Json  # Optional: schema registry
```

### 3.1 Producer đơn giản

```csharp
public class KafkaOrderEventProducer : IOrderEventProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaOrderEventProducer> _logger;
    private const string Topic = "order-events";

    public KafkaOrderEventProducer(IConfiguration config, ILogger<KafkaOrderEventProducer> logger)
    {
        _logger = logger;
        
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            
            // Durability settings
            Acks = Acks.All,            // Đợi tất cả ISR (in-sync replicas) acknowledge
            EnableIdempotence = true,   // Exactly-once semantics
            MaxInFlight = 5,            // Phải <= 5 khi dùng idempotence
            
            // Performance
            LingerMs = 5,              // Batch delay (ms) - nhóm messages để gửi cùng lúc
            BatchSize = 65536,         // 64KB batch size
            CompressionType = CompressionType.Snappy,
            
            // Retry
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
            
            // Timeout
            MessageTimeoutMs = 30_000,
            RequestTimeoutMs = 30_000
        };

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => logger.LogError("Kafka producer error: {Error}", e))
            .SetLogHandler((_, msg) => logger.LogDebug("Kafka: [{Level}] {Message}", msg.Level, msg.Message))
            .Build();
    }

    public async Task PublishAsync<T>(string partitionKey, T @event, CancellationToken ct = default) where T : class
    {
        var eventType = typeof(T).Name;
        var payload = JsonSerializer.Serialize(@event);
        
        var message = new Message<string, string>
        {
            Key = partitionKey,
            Value = payload,
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(eventType) },
                { "event-version", Encoding.UTF8.GetBytes("1") },
                { "produced-at", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) }
            }
        };

        try
        {
            var result = await _producer.ProduceAsync(Topic, message, ct);
            
            _logger.LogInformation(
                "Published {EventType} for key {Key} to {Topic}[{Partition}]@{Offset}",
                eventType, partitionKey, result.Topic, result.Partition, result.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} for key {Key}", eventType, partitionKey);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(10)); // Đảm bảo gửi hết trước khi dispose
        _producer.Dispose();
    }
}
```

### 3.2 Transactional Producer: Exactly-Once với Database

Outbox pattern đảm bảo không mất event ngay cả khi crash.

```csharp
// Lưu event vào outbox table cùng database transaction
public class OutboxEventPublisher
{
    private readonly AppDbContext _db;

    public async Task SaveEventAsync<T>(string partitionKey, T @event, CancellationToken ct = default) where T : class
    {
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            PartitionKey = partitionKey,
            EventType = typeof(T).Name,
            Payload = JsonSerializer.Serialize(@event),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending
        };

        _db.OutboxMessages.Add(outboxMessage);
        // Sẽ được commit cùng với domain changes trong cùng một transaction
    }
}

// Background worker: publish outbox messages lên Kafka
public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOutboxAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
        }
    }

    private async Task ProcessOutboxAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Lấy pending messages
        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var msg in messages)
        {
            try
            {
                await _producer.ProduceAsync("order-events", new Message<string, string>
                {
                    Key = msg.PartitionKey,
                    Value = msg.Payload,
                    Headers = new Headers
                    {
                        { "event-type", Encoding.UTF8.GetBytes(msg.EventType) },
                        { "outbox-id", Encoding.UTF8.GetBytes(msg.Id.ToString()) }
                    }
                }, ct);

                msg.Status = OutboxStatus.Published;
                msg.PublishedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox message {Id}", msg.Id);
                msg.RetryCount++;
                if (msg.RetryCount >= 5)
                    msg.Status = OutboxStatus.Failed;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
```

---

## 4. Kafka Consumer: xử lý events

### 4.1 Consumer cơ bản

```csharp
public class OrderEventConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceProvider _services;
    private readonly ILogger<OrderEventConsumer> _logger;

    public OrderEventConsumer(IConfiguration config, IServiceProvider services, ILogger<OrderEventConsumer> logger)
    {
        _services = services;
        _logger = logger;

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = "notification-service",
            
            // Offset management
            AutoOffsetReset = AutoOffsetReset.Earliest,  // Đọc từ đầu nếu group mới
            EnableAutoCommit = false,                     // Manual commit để control at-least-once
            
            // Performance
            FetchMinBytes = 1,
            FetchWaitMaxMs = 500,
            MaxPollIntervalMs = 300_000,  // 5 phút timeout
            SessionTimeoutMs = 30_000,
            
            // Exactly-once read (nếu dùng idempotent consumer)
            IsolationLevel = IsolationLevel.ReadCommitted
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, e) => logger.LogError("Consumer error: {Error}", e))
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                logger.LogInformation("Assigned partitions: {Partitions}", 
                    string.Join(", ", partitions));
            })
            .SetPartitionsRevokedHandler((c, partitions) =>
            {
                logger.LogInformation("Revoked partitions: {Partitions}", 
                    string.Join(", ", partitions));
                // Commit current offsets trước khi rebalance
                c.Commit();
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(["order-events"]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result is null) continue;

                    await ProcessMessageAsync(result, stoppingToken);

                    // Manual commit sau khi xử lý thành công
                    _consumer.Commit(result);
                }
                catch (ConsumeException ex) when (ex.Error.IsFatal)
                {
                    _logger.LogCritical(ex, "Fatal consumer error, stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message, will retry");
                    // Không commit - message sẽ được đọc lại sau restart
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var eventType = result.Message.Headers.GetLastBytes("event-type") is { } bytes
            ? Encoding.UTF8.GetString(bytes)
            : "unknown";

        _logger.LogInformation(
            "Processing {EventType} from [{Partition}]@{Offset}, key={Key}",
            eventType, result.Partition, result.Offset, result.Message.Key);

        using var scope = _services.CreateScope();

        switch (eventType)
        {
            case nameof(OrderPlaced):
                var orderPlaced = JsonSerializer.Deserialize<OrderPlaced>(result.Message.Value)!;
                var handler = scope.ServiceProvider.GetRequiredService<OrderPlacedHandler>();
                await handler.HandleAsync(orderPlaced, ct);
                break;

            case nameof(OrderApproved):
                var orderApproved = JsonSerializer.Deserialize<OrderApproved>(result.Message.Value)!;
                var approvedHandler = scope.ServiceProvider.GetRequiredService<OrderApprovedHandler>();
                await approvedHandler.HandleAsync(orderApproved, ct);
                break;

            default:
                _logger.LogWarning("Unknown event type: {EventType}", eventType);
                break;
        }
    }
}
```

### 4.2 Concurrent Consumer: xử lý song song

```csharp
public class ConcurrentOrderEventConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly Channel<ConsumeResult<string, string>> _channel;
    private const int WorkerCount = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(["order-events"]);

        // Start workers
        var workers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(() => ProcessChannelAsync(stoppingToken), stoppingToken))
            .ToArray();

        // Main loop: đẩy vào channel
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
            if (result is null) continue;
            
            await _channel.Writer.WriteAsync(result, stoppingToken);
        }

        _channel.Writer.Complete();
        await Task.WhenAll(workers);
    }

    private async Task ProcessChannelAsync(CancellationToken ct)
    {
        await foreach (var result in _channel.Reader.ReadAllAsync(ct))
        {
            await ProcessMessageAsync(result, ct);
            
            // Thread-safe commit: StackExchange.Redis không thread safe nhưng
            // Kafka consumer cũng không - cần lock
            lock (_consumer)
            {
                _consumer.StoreOffset(result);
            }
        }
    }
}
```

---

## 5. Dead Letter Queue: xử lý message thất bại

```csharp
public class ResilientKafkaConsumer : BackgroundService
{
    private readonly IProducer<string, string> _dlqProducer;

    private async Task ProcessWithRetryAsync(
        ConsumeResult<string, string> result, 
        CancellationToken ct)
    {
        var retryCount = GetRetryCount(result.Message.Headers);
        
        try
        {
            await ProcessMessageAsync(result, ct);
        }
        catch (Exception ex) when (retryCount < 3)
        {
            _logger.LogWarning(ex, 
                "Processing failed (attempt {Attempt}), will retry", retryCount + 1);
            
            // Publish lại vào retry topic với delay
            var retryTopic = $"order-events.retry.{retryCount + 1}";
            await _dlqProducer.ProduceAsync(retryTopic, new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = new Headers
                {
                    { "original-topic", Encoding.UTF8.GetBytes(result.Topic) },
                    { "original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()) },
                    { "retry-count", Encoding.UTF8.GetBytes((retryCount + 1).ToString()) },
                    { "original-error", Encoding.UTF8.GetBytes(ex.Message) },
                    { "retry-after", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, retryCount) * 5).ToString("O")) }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message exceeded max retries, sending to DLQ");
            
            // Gửi vào Dead Letter Queue
            await _dlqProducer.ProduceAsync("order-events.dlq", new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = new Headers
                {
                    { "failure-reason", Encoding.UTF8.GetBytes(ex.ToString()) },
                    { "failed-at", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) }
                }
            }, ct);
        }
    }

    private static int GetRetryCount(Headers headers)
    {
        var bytes = headers.GetLastBytes("retry-count");
        return bytes is null ? 0 : int.Parse(Encoding.UTF8.GetString(bytes));
    }
}
```

---

## 6. Kafka Streams: xây dựng real-time analytics pipeline

```csharp
// Real-time order metrics aggregation
public class OrderMetricsAggregator : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _producer;
    
    // In-memory windows cho real-time aggregation
    private readonly Dictionary<string, WindowedCount> _minuteWindows = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _consumer.Subscribe(["order-events"]);

        while (!ct.IsCancellationRequested)
        {
            var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
            if (result is null)
            {
                await FlushExpiredWindowsAsync(ct);
                continue;
            }

            var eventType = GetEventType(result.Message.Headers);
            if (eventType != nameof(OrderPlaced)) continue;

            var order = JsonSerializer.Deserialize<OrderPlaced>(result.Message.Value)!;
            
            // Aggregate theo window
            var windowKey = $"{DateTimeOffset.UtcNow:yyyy-MM-dd-HH-mm}";
            if (!_minuteWindows.TryGetValue(windowKey, out var window))
            {
                window = new WindowedCount();
                _minuteWindows[windowKey] = window;
            }
            
            window.OrderCount++;
            window.TotalRevenue += order.TotalAmount;

            _consumer.Commit(result);
        }
    }

    private async Task FlushExpiredWindowsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredKeys = _minuteWindows
            .Where(kv => IsExpired(kv.Key, now))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            var window = _minuteWindows[key];
            
            // Publish aggregated metrics
            await _producer.ProduceAsync("order-metrics", new Message<string, string>
            {
                Key = key,
                Value = JsonSerializer.Serialize(new OrderMetric(
                    WindowKey: key,
                    OrderCount: window.OrderCount,
                    TotalRevenue: window.TotalRevenue,
                    AverageOrderValue: window.TotalRevenue / window.OrderCount))
            }, ct);

            _minuteWindows.Remove(key);
        }
    }
}
```

---

## 7. Consumer Lag Monitoring

```csharp
// Check consumer lag để alert khi consumer tụt hậu
public class KafkaLagMonitor : BackgroundService
{
    private readonly IAdminClient _adminClient;
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger<KafkaLagMonitor> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await CheckLagAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task CheckLagAsync(CancellationToken ct)
    {
        var metadata = _adminClient.GetMetadata("order-events", TimeSpan.FromSeconds(10));
        
        foreach (var partition in metadata.Topics[0].Partitions)
        {
            var tp = new TopicPartition("order-events", partition.PartitionId);
            var committed = _consumer.Committed([tp], TimeSpan.FromSeconds(5));
            var endOffset = _consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
            
            var lag = endOffset.High - committed[0].Offset;
            
            if (lag > 10_000)
            {
                _logger.LogWarning(
                    "Consumer lag HIGH for partition {Partition}: {Lag} messages behind",
                    partition.PartitionId, lag);
            }
        }
    }
}
```

---

## 8. Checklist production cho Kafka

- [ ] `Acks = All` và `EnableIdempotence = true` cho critical events
- [ ] Manual offset commit sau khi xử lý thành công
- [ ] Implement Dead Letter Queue cho messages thất bại
- [ ] Dùng partition key có ý nghĩa domain (entity ID) để đảm bảo ordering
- [ ] Monitor consumer lag - alert khi lag > threshold
- [ ] Retention policy phù hợp: 7 ngày cho events, 30 ngày cho audit logs
- [ ] Số partition >= số consumer tối đa dự kiến
- [ ] Outbox pattern để đảm bảo at-least-once delivery từ DB
- [ ] Schema versioning cho event schema evolution
- [ ] Benchmark throughput với production-like workload trước khi go live
- [ ] Kafka cluster ít nhất 3 brokers cho production
- [ ] Replication factor = 3 cho critical topics
