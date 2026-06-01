# 📚 Comprehensive .NET Libraries Guide for Production

**A complete reference guide of production-ready .NET libraries organized by category, with examples and best practices.**

---

## 📋 Table of Contents

1. [Caching & In-Memory Storage](#caching--in-memory-storage)
2. [Database & ORM](#database--orm)
3. [Messaging & Event Streaming](#messaging--event-streaming)
4. [API & Web Frameworks](#api--web-frameworks)
5. [Authentication & Security](#authentication--security)
6. [Observability (Logging, Tracing, Metrics)](#observability-logging-tracing-metrics)
7. [Healthcare/Medical Records (EMR/FHIR)](#healthcaremedical-records-emrfhir)
8. [Background Jobs & Scheduling](#background-jobs--scheduling)
9. [Data Validation & Serialization](#data-validation--serialization)
10. [Testing & Quality](#testing--quality)
11. [Cloud & Infrastructure](#cloud--infrastructure)
12. [AI & Machine Learning](#ai--machine-learning)

---

## 1️⃣ Caching & In-Memory Storage

### 🟢 **FusionCache** ⭐ RECOMMENDED
- **Status**: Production-ready | Google OSS Award Winner
- **NuGet**: `ZiggyCreatures.FusionCache`
- **What it does**: Hybrid cache (L1 in-memory + L2 distributed)
- **Key features**:
  - Cache stampede protection (distributed)
  - Fail-safe mechanism (stale data fallback)
  - Soft/hard timeouts
  - Eager refresh (background refresh before expiration)
  - Supports null value caching
  - Works with any IDistributedCache backend (Redis, etc.)

**When to use**: 
- Single-server or multi-server .NET apps needing high-speed caching
- Complex cache invalidation requirements
- Need fail-safe + resilience features

**Example**:
```csharp
services.AddFusionCache()
    .WithDistributedCache(redisCache)
    .WithBackplane(redisBackplane)
    .AsHybridCache();

// Usage
var product = cache.GetOrSet<Product>(
    $"product:{id}",
    _ => GetProductFromDb(id),
    options => options
        .SetDuration(TimeSpan.FromSeconds(30))
        .SetFailSafe(true, TimeSpan.FromHours(2))
);
```

---

### 🔵 **StackExchange.Redis**
- **Status**: Production-ready | Industry standard
- **NuGet**: `StackExchange.Redis`
- **What it does**: .NET Redis client
- **Key features**:
  - High-performance async API
  - Pub/Sub support
  - Connection pooling
  - Works with Azure Cache for Redis, AWS ElastiCache

**When to use**:
- Need distributed cache across multiple servers
- Pub/Sub messaging patterns
- Session storage
- Rate limiting

**Example**:
```csharp
var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var db = connection.GetDatabase();
await db.StringSetAsync("key", "value", TimeSpan.FromSeconds(30));
var value = await db.StringGetAsync("key");
```

---

### 🟣 **Microsoft.Extensions.Caching.Distributed**
- **Status**: Built-in | Part of ASP.NET Core
- **NuGet**: Included in `Microsoft.AspNetCore.App`
- **What it does**: Abstraction for distributed caching
- **Key features**:
  - Interface-based design
  - Works with any IDistributedCache implementation
  - Built-in SQL Server and Redis implementations

---

## 2️⃣ Database & ORM

### 🟢 **Entity Framework Core** ⭐ RECOMMENDED
- **Status**: Production-ready | Official Microsoft
- **NuGet**: `Microsoft.EntityFrameworkCore`
- **Supports**: SQL Server, PostgreSQL, MySQL, SQLite, Cosmos DB, etc.
- **Key features**:
  - LINQ to SQL translation
  - Change tracking
  - Migrations
  - Lazy/eager/explicit loading
  - Bulk operations (EF Core 7+)
  - Owned entity types
  - Shadow properties

**When to use**:
- Relational databases (SQL Server, PostgreSQL)
- Complex domain models
- Need migrations and versioning
- ORM with full LINQ support

**Example**:
```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("Default"))
);

// Query
var patients = await _context.Patients
    .Include(p => p.MedicalRecords)
    .Where(p => p.CreatedAt > DateTime.UtcNow.AddDays(-30))
    .ToListAsync();

// Bulk insert
await _context.BulkInsertAsync(records);
```

---

### 🔵 **Marten**
- **Status**: Production-ready | Event sourcing + document store
- **NuGet**: `Marten`
- **Supports**: PostgreSQL only
- **Key features**:
  - Event sourcing out-of-the-box
  - Document store (JSONB)
  - Projections
  - Event subscriptions
  - Multi-tenancy support
  - Snapshots

**When to use**:
- Event sourcing architecture
- Complex domain events
- Time-travel debugging needed
- PostgreSQL backend

**Example**:
```csharp
var store = DocumentStore.For("connection_string");

// Store event
var session = store.LightweightSession();
var @event = new PatientRegistered { PatientId = patientId, Name = "John" };
session.Events.Append(patientId, @event);
await session.SaveChangesAsync();

// Rebuild from events
var patient = await session.Events.AggregateStreamAsync<Patient>(patientId);
```

---

### 🟡 **Dapper**
- **Status**: Production-ready | Micro-ORM
- **NuGet**: `Dapper`
- **What it does**: Lightweight ORM for SQL
- **Key features**:
  - Very fast (minimal overhead)
  - Supports multiple databases
  - Parameter mapping
  - Bulk operations via `Dapper.Contrib`

**When to use**:
- Performance-critical queries
- Simple CRUD operations
- Don't need full ORM complexity
- Legacy databases

---

### 🟣 **MongoDB.Driver**
- **Status**: Production-ready | Official MongoDB client
- **NuGet**: `MongoDB.Driver`
- **What it does**: NoSQL document database client
- **Key features**:
  - Async API
  - LINQ to MongoDB
  - Transaction support (4.0+)
  - Connection pooling

**When to use**:
- Unstructured/semi-structured data
- FHIR documents
- Horizontal scaling needed
- Dynamic schema

---

## 3️⃣ Messaging & Event Streaming

### 🟢 **MassTransit** ⭐ RECOMMENDED
- **Status**: Production-ready
- **NuGet**: `MassTransit`
- **Supports**: RabbitMQ, MSMQ, ActiveMQ, Azure Service Bus, Amazon SQS, etc.
- **Key features**:
  - Consumer pipeline
  - Saga state machine
  - Request-response pattern
  - Retry + circuit breaker built-in
  - Message scheduling
  - Distributed transaction support

**When to use**:
- Message-based architecture
- Saga pattern (distributed transactions)
- Need retry/error handling built-in
- Multiple transport support

**Example**:
```csharp
services.AddMassTransit(x =>
{
    x.AddConsumer<PatientCreatedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq");
        cfg.ConfigureEndpoints(context);
    });
});

// Publish event
await publishEndpoint.Publish(new PatientCreatedEvent { PatientId = id, Name = "John" });
```

---

### 🔵 **Wolverine**
- **Status**: Production-ready | Modern successor to NServiceBus concepts
- **NuGet**: `Wolverine`
- **Supports**: RabbitMQ, MSMQ, PostgreSQL, SQL Server, Kafka, etc.
- **Key features**:
  - Lightweight messaging
  - Built-in outbox pattern
  - Saga support
  - Excellent performance
  - Built-in to .NET Aspire

**When to use**:
- Need lightweight alternative to MassTransit
- .NET Aspire integration desired
- Outbox pattern important
- High throughput messaging

---

### 🟡 **Kafka (Confluent.Kafka)**
- **Status**: Production-ready
- **NuGet**: `Confluent.Kafka`
- **What it does**: Event streaming platform client
- **Key features**:
  - High-throughput
  - Consumer groups
  - Exactly-once semantics
  - Partitioning
  - Replication

**When to use**:
- Millions of events per day
- Event streaming architecture
- Complex event processing
- Real-time analytics

**Example**:
```csharp
using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = "localhost:9092" }).Build())
{
    await producer.ProduceAsync("patient-events", new Message<string, string> 
    { 
        Key = patientId, 
        Value = JsonSerializer.Serialize(patientEvent) 
    });
}
```

---

### 🟣 **NServiceBus**
- **Status**: Production-ready | Enterprise-grade
- **NuGet**: `NServiceBus`
- **What it does**: Enterprise service bus
- **Key features**:
  - Transactional messaging
  - Saga pattern (orchestration)
  - Distributor for load balancing
  - Multiple transports

---

## 4️⃣ API & Web Frameworks

### 🟢 **ASP.NET Core** ⭐ BUILT-IN
- **Status**: Production-ready | Official Microsoft
- **NuGet**: `Microsoft.AspNetCore.App`
- **Key frameworks**:
  - **ASP.NET Core Web API** — RESTful APIs
  - **Minimal APIs** — Lightweight endpoint configuration
  - **gRPC** — Protocol Buffers for high-performance APIs
  - **SignalR** — Real-time communication

**Example (Minimal API)**:
```csharp
app.MapPost("/patients", async (CreatePatientRequest req, ApplicationDbContext db) =>
{
    var patient = new Patient { Name = req.Name, DateOfBirth = req.DateOfBirth };
    db.Patients.Add(patient);
    await db.SaveChangesAsync();
    return Results.Created($"/patients/{patient.Id}", patient);
});
```

---

### 🔵 **Hot Chocolate**
- **Status**: Production-ready | GraphQL server
- **NuGet**: `HotChocolate.AspNetCore`
- **Key features**:
  - Type-safe GraphQL
  - DataLoaders (batch loading)
  - Subscriptions
  - Relay cursor pagination
  - Authorization directives

**When to use**:
- GraphQL API needed
- Complex query patterns
- Multi-client scenarios (web, mobile with different needs)

---

### 🟡 **YARP (Yet Another Reverse Proxy)**
- **Status**: Production-ready | API Gateway
- **NuGet**: `Yarp.ReverseProxy`
- **Key features**:
  - Request/response transformation
  - Load balancing
  - Service discovery
  - Request routing
  - Rate limiting (via Polly)

**When to use**:
- API gateway pattern
- Centralized authentication
- Service routing

---

## 5️⃣ Authentication & Security

### 🟢 **IdentityServer / Duende IdentityServer** ⭐ RECOMMENDED
- **Status**: Production-ready | Industry standard
- **NuGet**: `Duende.IdentityServer`
- **What it does**: OpenID Connect + OAuth 2.0 server
- **Key features**:
  - SSO (Single Sign-On)
  - API protection
  - Refresh tokens
  - Device flow
  - PKCE support
  - User management integration

**When to use**:
- Multi-app authentication needed
- OAuth 2.0 / OIDC required (HIPAA)
- User federation (LDAP, AD)
- API security

**Example**:
```csharp
services.AddIdentityServer()
    .AddInMemoryIdentityResources(IdentityResources)
    .AddInMemoryApiScopes(ApiScopes)
    .AddInMemoryClients(Clients)
    .AddDeveloperSigningCredential();

// In API
app.UseAuthentication();
app.UseAuthorization();
```

---

### 🔵 **AspNetCore.Authentication.JwtBearer**
- **Status**: Built-in | Official Microsoft
- **NuGet**: Included in `Microsoft.AspNetCore.App`
- **What it does**: JWT validation middleware
- **Key features**:
  - Bearer token validation
  - Claims extraction
  - Policy-based authorization

---

### 🟡 **AspNetCore.Authentication.OpenIdConnect**
- **Status**: Built-in | Official Microsoft
- **What it does**: OIDC client middleware
- **Key features**:
  - Redirect to OIDC provider
  - ID token validation
  - Hybrid flow support

---

### 🟣 **Bouncy Castle / NaCl.Core**
- **Status**: Production-ready | Cryptography
- **NuGet**: `BouncyCastle.Cryptography` or `NaCl.Core`
- **Key features**:
  - AES encryption
  - RSA, ECDSA signing
  - Hashing (SHA-256, etc.)
  - Key derivation

**When to use**:
- Encrypt patient data at rest (HIPAA requirement)
- Digital signatures
- Secure password storage

**Example**:
```csharp
// AES encryption
var cipher = new AesEngine();
var encryptor = new PaddedBufferedBlockCipher(new CbcBlockCipher(cipher));
encryptor.Init(true, new KeyParameter(key));
var ciphertext = encryptor.DoFinal(plaintext);
```

---

## 6️⃣ Observability (Logging, Tracing, Metrics)

### 🟢 **Serilog** ⭐ RECOMMENDED
- **Status**: Production-ready
- **NuGet**: `Serilog`, `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Serilog.Sinks.Elasticsearch`
- **What it does**: Structured logging
- **Key features**:
  - Structured properties
  - Multiple sinks (File, Console, Seq, Elasticsearch, etc.)
  - Context enrichment
  - Request/response logging
  - Audit trail logging

**When to use**: 
- Any production application
- HIPAA audit logging required
- Need to track who accessed patient data, when, what changes

**Example**:
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Seq("http://localhost:5341")
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "PatientManagement")
    .CreateLogger();

// Usage
Log.Information("Patient {PatientId} accessed by user {UserId} at {Timestamp}", 
    patientId, userId, DateTime.UtcNow);
```

---

### 🔵 **OpenTelemetry**
- **Status**: Production-ready | CNCF standard
- **NuGet**: `OpenTelemetry`, `OpenTelemetry.Exporter.Console`, `OpenTelemetry.Exporter.Jaeger`
- **What it does**: Distributed tracing + metrics + logs
- **Key features**:
  - Trace distributed requests across services
  - Metrics collection
  - Log correlation
  - Exporters for Jaeger, Zipkin, DataDog, New Relic, etc.

**When to use**:
- Microservices architecture
- Need distributed tracing
- Performance monitoring
- Production debugging

**Example**:
```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation()
            .AddJaegerExporter(jaegerOptions =>
            {
                jaegerOptions.AgentHost = "localhost";
                jaegerOptions.AgentPort = 6831;
            });
    });
```

---

### 🟡 **NLog**
- **Status**: Production-ready
- **NuGet**: `NLog`, `NLog.Web.AspNetCore`
- **Alternative to Serilog**
- **Key features**:
  - Structured logging
  - Multiple targets
  - Conditional filtering
  - Async logging

---

### 🟣 **Prometheus / Metrics**
- **Status**: Production-ready
- **NuGet**: `Prometheus.Client`
- **What it does**: Metrics collection for monitoring
- **Key features**:
  - Counter, Gauge, Histogram
  - Scrape endpoint
  - Alert definition

---

## 7️⃣ Healthcare/Medical Records (EMR/FHIR)

### 🟢 **Hl7.Fhir.R4 (Firely .NET SDK)** ⭐ ESSENTIAL FOR EMR
- **Status**: Production-ready | Most widely used FHIR SDK in .NET
- **NuGet**: `Hl7.Fhir.R4`
- **What it does**: FHIR (Fast Healthcare Interoperability Resources) data models
- **Key features**:
  - FHIR R4 & STU3 resource models
  - Serialization/deserialization (JSON, XML)
  - Validation
  - FHIR REST API client support
  - Used by Microsoft Data API Builder, commercial EMRs

**When to use**:
- Building EMR / medical records system
- Need healthcare data interoperability
- Integration with other FHIR servers
- Standards compliance (HL7 FHIR)

**Example**:
```csharp
// Create a Patient resource
var patient = new Patient
{
    Id = "patient-123",
    Name = new List<HumanName>
    {
        new HumanName { Given = new[] { "John" }, Family = "Doe" }
    },
    BirthDate = "1980-01-01",
    Gender = AdministrativeGender.Male
};

// Serialize to JSON
var json = new FhirJsonSerializer().SerializeToString(patient);

// Deserialize
var parsed = new FhirJsonParser().Parse<Patient>(json);

// Validate against profile
var validator = new FhirValidator();
var result = validator.Validate(patient);
```

---

### 🔵 **nHapi (HL7 v2 Parser)**
- **Status**: Production-ready
- **NuGet**: `nHapi`
- **What it does**: HL7 v2 message parsing (legacy systems)
- **Key features**:
  - Parse HL7 v2 messages
  - Message generation
  - Segment navigation

**When to use**:
- Legacy HL7 v2 system integration
- Lab systems, EHR integration
- Healthcare data exchange with older systems

**Example**:
```csharp
var parser = new PipeParser();
var msg = parser.Parse(hl7MessageString) as ADT_A01;
var patientName = msg.PID.GetPatientName(0);
```

---

### 🟡 **SanteDB**
- **Status**: Production-ready | Open-source health platform
- **GitHub**: `github.com/santedb`
- **What it does**: Complete health information exchange platform
- **Key features**:
  - Supports HL7, FHIR, HL7 CDA
  - Patient identity management
  - Data governance
  - .NET implementation (can run in Azure, on-premises)

**When to use**:
- Need reference EMR implementation
- Complex health information exchange
- Multi-facility coordination

---

## 8️⃣ Background Jobs & Scheduling

### 🟢 **Hangfire** ⭐ RECOMMENDED
- **Status**: Production-ready
- **NuGet**: `Hangfire`
- **Supports**: SQL Server, PostgreSQL, MongoDB, Redis
- **Key features**:
  - Recurring jobs (cron)
  - Delayed jobs
  - Batch processing
  - Retry mechanism
  - Dashboard for monitoring
  - Fire-and-forget jobs

**When to use**:
- Background job processing
- Scheduled tasks (appointment reminders, reports)
- Batch operations
- Email/notification sending

**Example**:
```csharp
services.AddHangfire(configuration => 
    configuration.UseSqlServerStorage("connection_string"));
services.AddHangfireServer();

// Enqueue job
BackgroundJob.Enqueue(() => SendAppointmentReminder(patientId));

// Schedule
BackgroundJob.Schedule(() => GenerateMonthlyReport(), TimeSpan.FromDays(1));

// Recurring
RecurringJob.AddOrUpdate(
    "generate-reports", 
    () => GenerateMonthlyReport(), 
    Cron.Monthly(15, 9));
```

---

### 🔵 **Elsa Workflows**
- **Status**: Production-ready
- **NuGet**: `Elsa.Core`
- **What it does**: Workflow engine
- **Key features**:
  - Visual workflow designer
  - Durable execution
  - Multi-tenancy
  - Variable scoping
  - Decision trees

**When to use**:
- Complex business workflows (approval chains, patient journey)
- Visual workflow design needed
- Non-technical user configuration
- Dynamic workflows

---

### 🟡 **Coravel**
- **Status**: Production-ready | Lightweight alternative
- **NuGet**: `Coravel`
- **Key features**:
  - Task scheduling
  - Queue processing
  - Invocable actions

---

## 9️⃣ Data Validation & Serialization

### 🟢 **FluentValidation** ⭐ RECOMMENDED
- **Status**: Production-ready
- **NuGet**: `FluentValidation`
- **What it does**: Rule-based validation
- **Key features**:
  - Fluent API
  - Async validation
  - Custom rules
  - Integration with ASP.NET Core
  - Localization support

**When to use**:
- Complex validation rules
- Medical records validation (age ranges, dosages)
- Business rule enforcement
- Form validation

**Example**:
```csharp
public class PatientValidator : AbstractValidator<Patient>
{
    public PatientValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 100);

        RuleFor(x => x.DateOfBirth)
            .LessThan(DateTime.Today)
            .WithMessage("Patient must be born in the past");

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}
```

---

### 🔵 **System.Text.Json**
- **Status**: Built-in | Official Microsoft
- **What it does**: JSON serialization/deserialization
- **Key features**:
  - High performance
  - Source generators (compilation)
  - Nullable reference support
  - Custom converters

---

### 🟡 **Newtonsoft.Json (Json.NET)**
- **Status**: Production-ready | Community standard
- **NuGet**: `Newtonsoft.Json`
- **Key features**:
  - LINQ to JSON
  - Custom converters
  - Settings per-type

---

## 🔟 Testing & Quality

### 🟢 **xUnit** ⭐ RECOMMENDED
- **Status**: Production-ready
- **NuGet**: `xunit`
- **Key features**:
  - Modern unit testing
  - Parallel execution
  - IDisposable cleanup
  - Theory-based data testing

**Example**:
```csharp
public class PatientServiceTests
{
    [Fact]
    public async Task CreatePatient_WithValidInput_ReturnsPatientId()
    {
        // Arrange
        var service = new PatientService(mockDb);
        
        // Act
        var result = await service.CreatePatientAsync("John Doe", new DateTime(1980, 1, 1));
        
        // Assert
        Assert.NotNull(result);
    }
}
```

---

### 🔵 **Moq**
- **Status**: Production-ready
- **NuGet**: `Moq`
- **What it does**: Mocking framework
- **Key features**:
  - Type-safe mocks
  - Verification
  - Callback setup

---

### 🟡 **Testcontainers.DotNet**
- **Status**: Production-ready
- **NuGet**: `Testcontainers`
- **What it does**: Docker containers for integration tests
- **Key features**:
  - Start real databases (PostgreSQL, MongoDB)
  - Redis, Kafka containers
  - Automatic cleanup

**Example**:
```csharp
[Collection("Database collection")]
public class PatientRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.StopAsync();

    [Fact]
    public async Task GetPatient_ReturnsCorrectData()
    {
        var db = new ApplicationDbContext(_container.GetConnectionString());
        // test code
    }
}
```

---

### 🟣 **BenchmarkDotNet**
- **Status**: Production-ready
- **NuGet**: `BenchmarkDotNet`
- **What it does**: Performance benchmarking
- **Key features**:
  - Accurate measurements
  - Statistics
  - Comparative benchmarks

---

## 1️⃣1️⃣ Cloud & Infrastructure

### 🟢 **.NET Aspire** ⭐ RECOMMENDED
- **Status**: Production-ready (.NET 8+)
- **NuGet**: `Aspire.*` packages
- **What it does**: Application composition & observability
- **Key features**:
  - Service orchestration (docker-compose alternative)
  - Built-in observability dashboard
  - Health checks
  - Configuration management
  - Service discovery

**Example**:
```csharp
// AppHost project
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");
var postgres = builder.AddPostgres("postgres")
    .AddDatabase("patients_db");

builder.AddProject<Projects.PatientApi>("patient-api")
    .WithReference(redis)
    .WithReference(postgres);

await builder.Build().RunAsync();
```

---

### 🔵 **Azure SDK (Azure.* packages)**
- **Status**: Production-ready | Official Microsoft
- **NuGet**: `Azure.Storage.Blobs`, `Azure.Messaging.ServiceBus`, `Azure.Cosmos`, etc.
- **Key features**:
  - Unified API
  - Managed identities
  - Async throughout
  - Built-in retry

---

### 🟡 **Docker / Kubernetes**
- **Status**: Production-ready | Industry standard
- **What it does**: Containerization & orchestration
- **Integration**: 
  - Use in .NET Aspire
  - Docker support in VS 2022
  - Kubernetes libraries (Fabric8.Kubernetes.Client)

---

## 1️⃣2️⃣ AI & Machine Learning

### 🟢 **Microsoft.Extensions.AI** ⭐ NEW & RECOMMENDED
- **Status**: Production-ready (.NET 9+)
- **NuGet**: `Microsoft.Extensions.AI`
- **What it does**: Standard AI abstractions
- **Key features**:
  - Multi-provider support (OpenAI, Azure OpenAI, Anthropic, Ollama)
  - Embeddings, chat completion, text-to-image
  - Caching layer
  - Telemetry built-in

**Example**:
```csharp
services.AddChatClient(services =>
    services.UseOpenAI("gpt-4o", apiKey: "sk-..."));

var chatClient = app.Services.GetRequiredService<IChatClient>();
var result = await chatClient.CompleteAsync("What are the symptoms of diabetes?");
```

---

### 🔵 **Microsoft Agent Framework**
- **Status**: Production-ready (.NET 10+)
- **NuGet**: `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Workflows`
- **What it does**: Agentic AI framework (successor to Semantic Kernel agents)
- **Key features**:
  - Agent-first design
  - Workflow orchestration (graph-based)
  - Tool calling (function calling)
  - Durable execution
  - Human-in-the-loop

**Use case**: Medical consultation bot, diagnostic assistant

---

### 🟡 **ML.NET**
- **Status**: Production-ready
- **NuGet**: `Microsoft.ML`
- **What it does**: Machine learning for .NET
- **Key features**:
  - Classification, regression, clustering
  - AutoML
  - Model export

**Use case**: Fraud detection, risk prediction in medical systems

---

### 🟣 **Semantic Kernel**
- **Status**: Production-ready (but being superseded by Agent Framework)
- **NuGet**: `Microsoft.SemanticKernel`
- **What it does**: LLM orchestration
- **Note**: Microsoft Agent Framework is the new recommended approach

---

## 📊 Quick Reference Matrix

| Category | Primary | Secondary | Notes |
|----------|---------|-----------|-------|
| **Caching** | FusionCache | StackExchange.Redis | Use hybrid approach |
| **Database** | EF Core | Marten (event sourcing) | Choose per use case |
| **Messaging** | MassTransit | Wolverine | Both production-ready |
| **API** | ASP.NET Core | Hot Chocolate (GraphQL) | Built-in standard |
| **Auth** | IdentityServer | JwtBearer | For HIPAA compliance |
| **Logging** | Serilog | NLog | Structured logging essential |
| **Tracing** | OpenTelemetry | - | Distributed systems must-have |
| **Healthcare** | Hl7.Fhir.R4 | nHapi, SanteDB | FHIR is standard |
| **Jobs** | Hangfire | Elsa Workflows | For complex workflows |
| **Validation** | FluentValidation | - | Essential for EMR |
| **Testing** | xUnit | Testcontainers | Integration testing |
| **Cloud** | .NET Aspire | Azure SDKs | Orchestration |
| **AI** | Microsoft.Extensions.AI | Agent Framework | Latest standard |

---

## 🏥 Production Checklist for EMR System

```
Core Infrastructure:
✅ Database: EF Core + SQL Server/PostgreSQL
✅ Caching: FusionCache + Redis
✅ Messaging: MassTransit (for inter-system events)
✅ API: ASP.NET Core Web API + Minimal APIs

Healthcare Standards:
✅ FHIR: Hl7.Fhir.R4 (Firely SDK)
✅ HL7 v2: nHapi (for legacy integration)
✅ Data Models: Strongly typed per FHIR

Security (HIPAA):
✅ Authentication: IdentityServer / Azure AD
✅ Encryption: Bouncy Castle (at-rest)
✅ HTTPS/TLS: ASP.NET Core built-in
✅ API Security: OAuth 2.0 + JWT Bearer

Observability (HIPAA Audit):
✅ Logging: Serilog with Seq/Elasticsearch sink
✅ Tracing: OpenTelemetry + Jaeger
✅ Metrics: Prometheus
✅ Health Checks: ASP.NET Core Health Checks
✅ Audit Trail: Track who accessed what, when

Resilience:
✅ Retry/Circuit Breaker: Polly v8
✅ Timeouts: Built into HTTP clients
✅ Bulkhead: Polly isolation
✅ Fail-safe caching: FusionCache

Validation:
✅ Input Validation: FluentValidation
✅ Business Rules: Domain-driven validation
✅ Medical Rules: Dosage, age, condition checks

Background Processing:
✅ Jobs: Hangfire for scheduled tasks
✅ Workflows: Elsa for approval chains
✅ Notifications: Email/SMS queues

Testing:
✅ Unit Tests: xUnit + Moq
✅ Integration: Testcontainers
✅ Performance: BenchmarkDotNet

Deployment:
✅ Containerization: Docker
✅ Orchestration: .NET Aspire or Kubernetes
✅ CD/CI: GitHub Actions / Azure Pipelines
```

---

## 📚 Learning Path

1. **Start** → Master ASP.NET Core + EF Core + Serilog
2. **Intermediate** → Add FusionCache, FluentValidation, authentication
3. **Advanced** → Learn MassTransit, FHIR/HL7, OpenTelemetry
4. **Production** → Master Hangfire, resilience patterns, Kubernetes
5. **Latest** → Explore .NET Aspire, Agent Framework, Microsoft.Extensions.AI

---

## 🔗 Resources

- [Official .NET Documentation](https://docs.microsoft.com/dotnet)
- [NuGet.org](https://www.nuget.org/)
- [TechEmpower Benchmarks](https://www.techempower.com/benchmarks/)
- [FHIR Official](https://www.hl7.org/fhir/)
- [HL7 Standards](https://www.hl7.org/)
- [HIPAA Compliance Guide](https://www.hhs.gov/hipaa/)

---

**Last updated**: June 2026 | .NET 10 | C# 14
