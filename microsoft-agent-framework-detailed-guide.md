# Hướng dẫn cực kỳ chi tiết về Microsoft Agent Framework, kiến trúc, cách dùng và các use case mẫu có code cụ thể

## Mục tiêu của bài viết

Bài này tập trung vào Microsoft Agent Framework theo góc nhìn của một kỹ sư .NET muốn đi từ mức “nghe tên framework” sang mức “biết khi nào nên dùng, nên thiết kế ra sao, và có thể bắt tay code ngay”.

Mục tiêu không phải là giới thiệu lướt qua. Mục tiêu là đi thật cụ thể vào:

- Microsoft Agent Framework là gì
- nó ra đời để giải quyết bài toán gì
- nó khác gì so với Semantic Kernel cũ, AutoGen, hay việc gọi model trực tiếp
- khi nào nên dùng agent, khi nào nên dùng workflow, khi nào chỉ nên viết function bình thường
- cấu trúc cốt lõi của framework
- cách tạo một agent cơ bản trong .NET
- cách thêm tools
- cách làm multi-turn conversation
- cách tổ chức memory, session, state
- cách làm workflow nhiều bước
- các use case mẫu thực dụng có code cụ thể
- các lưu ý triển khai production

Nếu chốt ngắn gọn một câu thì Microsoft Agent Framework là nỗ lực của Microsoft nhằm thống nhất thế giới agentic AI giữa hai hướng trước đó là AutoGen và Semantic Kernel, rồi bổ sung thêm workflow có kiểm soát tốt hơn cho các hệ thống nhiều bước, nhiều agent, hoặc có human-in-the-loop.

---

## 1. Microsoft Agent Framework là gì?

Theo mô tả chính thức từ Microsoft Learn, Agent Framework có hai nhóm capability chính:

1. **Agents**
   - các agent dùng LLM để xử lý input, gọi tool, gọi MCP server, tạo response
2. **Workflows**
   - các đồ thị thực thi nhiều bước, nối agent và function với nhau, có type-safe routing, checkpointing và human-in-the-loop support

Ngoài ra framework còn có các building block nền tảng như:
- model clients
- agent session cho state management
- context providers cho memory
- middleware để chặn hoặc biến đổi luồng xử lý
- MCP clients cho tích hợp tool server

Điểm đáng chú ý ở đây là Microsoft không còn xem “agent” là một object gọi model đơn giản nữa. Họ đang đẩy framework theo hướng:

- có state rõ ràng
- có vòng đời hội thoại rõ ràng
- có tools rõ ràng
- có orchestration rõ ràng
- có khả năng mở rộng thành hệ thống nhiều agent và workflow dài hạn

Nói cách khác, Agent Framework không chỉ là SDK gọi chat completion đẹp hơn. Nó là một application framework cho AI agents.

---

## 2. Vì sao framework này đáng chú ý với dân .NET?

Trước khi có Agent Framework, nếu làm AI app trên .NET, người ta thường rơi vào một trong ba hướng:

1. gọi thẳng API của OpenAI/Azure OpenAI
2. dùng Semantic Kernel
3. dùng AutoGen hoặc framework ngoài hệ Microsoft

Mỗi hướng có ưu và nhược điểm:

### Gọi thẳng model API

Ưu điểm:
- đơn giản
- dễ kiểm soát
- ít abstraction

Nhược điểm:
- tự xử lý tool calling, conversation state, retries, orchestration, memory
- app lớn lên rất nhanh sẽ thành mớ glue code

### Semantic Kernel

Ưu điểm:
- plugin/function abstraction tốt
- enterprise posture khá ổn
- .NET integration tốt

Nhược điểm:
- nhiều lúc vẫn hơi nghiêng về “LLM orchestration toolkit” hơn là một agent runtime rõ ràng
- multi-agent story trước đây không phải lúc nào cũng tự nhiên

### AutoGen

Ưu điểm:
- multi-agent patterns nổi bật
- agent-to-agent interaction tự nhiên

Nhược điểm:
- enterprise state management, type safety, middleware story ở .NET không thống nhất như mong muốn

Microsoft Agent Framework sinh ra để gom lại:
- sự đơn giản và cảm giác agent-first từ AutoGen
- khả năng enterprise, session, middleware, type safety từ Semantic Kernel
- cộng thêm workflow graph-based để orchestration explicit hơn

Đó là lý do nó đáng học, nhất là nếu bạn đang build AI system nghiêm túc trên .NET chứ không chỉ demo chatbot.

---

## 3. Khi nào nên dùng Agent Framework, khi nào không?

Đây là chỗ quan trọng nhất.

### Nên dùng khi

- bạn cần một agent thực sự có tools, state, session
- bạn cần multi-turn conversation có lưu context
- bạn cần workflow nhiều bước giữa function và agent
- bạn cần hệ thống có khả năng mở rộng dần sang multi-agent
- bạn muốn mô hình rõ ràng hơn cho tool invocation, sessions, middleware
- bạn muốn đi theo hướng chính thức của Microsoft cho AI agents trên .NET

### Không nên dùng khi

- bài toán chỉ là một prompt duy nhất gọi model rồi trả về text
- không có tools, không có workflow, không có state
- việc viết một function deterministic đơn giản là đủ
- team chưa hiểu bài toán nghiệp vụ mà đã nhảy vào agent abstraction

Microsoft nói rất đúng một ý: **nếu bạn có thể viết một function để xử lý việc đó, thì hãy viết function thay vì dùng AI agent**.

Đây là nguyên tắc cực quan trọng để tránh biến mọi thứ thành “agent” chỉ vì hype.

---

## 4. Agent vs Workflow trong Microsoft Agent Framework

Trong docs chính thức, Microsoft phân biệt khá rõ:

### Dùng agent khi
- bài toán mở, mang tính hội thoại
- cần planning hoặc autonomous tool use
- một agent với model + tools là đủ xử lý

### Dùng workflow khi
- các bước xử lý đã tương đối rõ
- cần kiểm soát execution order
- nhiều function/agent phải phối hợp
- cần branching, checkpointing, human approval

Nói dễ hiểu hơn:

- **Agent** phù hợp với “hãy tự suy nghĩ và chọn tool nào cần gọi”
- **Workflow** phù hợp với “đi từ bước A sang B sang C, nếu lỗi thì qua nhánh D, nếu cần người duyệt thì pause”

Rất nhiều đội làm AI app bị lỗi ở đây: dùng agent cho thứ vốn nên là workflow. Kết quả là hệ thống khó predict, khó debug, khó test. Ngược lại, cũng có đội làm workflow cho thứ chỉ cần một agent đơn giản, khiến kiến trúc nặng nề vô ích.

---

## 5. Cấu trúc khái niệm cốt lõi của Agent Framework

Trước khi code, cần nắm được mấy khái niệm này.

### 5.1 AIAgent

Đây là abstraction trung tâm đại diện cho một agent. Agent nhận input, có thể dùng model, có thể gọi tool, có session, và trả về response.

### 5.2 AgentSession

Session dùng để theo dõi state của một cuộc hội thoại hoặc một phiên làm việc. Đây là phần cực quan trọng nếu bạn có multi-turn conversation.

### 5.3 Tools

Tool là các function hoặc capability ngoài model mà agent có thể gọi, ví dụ:
- tìm kiếm sản phẩm
- tra giá cổ phiếu
- lấy lịch họp
- tạo ticket hỗ trợ
- gọi database read-only query

### 5.4 Context providers / memory

Đây là nơi gắn thêm bối cảnh hoặc ký ức cho agent. Không phải lúc nào cũng nên đổ cả lịch sử vào prompt, nên memory cần thiết kế cẩn thận.

### 5.5 Middleware

Middleware cho phép can thiệp vào luồng xử lý. Ví dụ:
- log request/response
- chặn tool nguy hiểm
- thêm safety checks
- đo telemetry
- chuẩn hóa instruction theo tenant

### 5.6 Workflows

Workflow là phần orchestration có cấu trúc. Dùng khi cần:
- nhiều bước
- nhiều agent
- branching rõ
- human-in-the-loop
- checkpoint và resume

---

## 6. Cài đặt package và dựng agent đầu tiên

Theo tài liệu Microsoft Learn, ví dụ khởi đầu với Foundry có thể bắt đầu kiểu này:

```bash
dotnet add package Microsoft.Agents.AI.Foundry --prerelease
```

Ví dụ cơ bản trong C#:

```csharp
using System;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

AIAgent agent = new AIProjectClient(
        new Uri("https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project"),
        new AzureCliCredential())
    .AsAIAgent(
        model: "gpt-5.4-mini",
        instructions: "You are a friendly assistant. Keep your answers brief.");

Console.WriteLine(await agent.RunAsync("What is the largest city in France?"));
```

Đây là ví dụ tối thiểu, nhưng trong đời thật chúng ta nên đóng gói nó thành app rõ ràng hơn.

### Ví dụ console app tối thiểu

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

internal class Program
{
    static async Task Main(string[] args)
    {
        var endpoint = new Uri("https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project");

        AIAgent agent = new AIProjectClient(endpoint, new AzureCliCredential())
            .AsAIAgent(
                model: "gpt-5.4-mini",
                name: "HelperBot",
                instructions: "You are a practical assistant for software engineers. Answer clearly and concretely.");

        var result = await agent.RunAsync("Explain dependency injection in ASP.NET Core with a small example.");
        Console.WriteLine(result);
    }
}
```

### Ý nghĩa từng phần

- `AIProjectClient`: client kết nối tới dịch vụ Foundry/Azure AI project
- `AzureCliCredential`: tái sử dụng phiên đăng nhập Azure hiện tại
- `AsAIAgent(...)`: chuyển model client thành agent abstraction
- `instructions`: system prompt cấp agent
- `RunAsync(...)`: chạy agent với input đơn giản

### Khi nào code này chưa đủ?

Khi app của bạn cần:
- hội thoại nhiều lượt
- tools
- session persistence
- logging/telemetry
- workflow
- agent tùy biến

Lúc đó bạn phải đi sâu hơn.

---

## 7. Ví dụ 1, xây dựng một FAQ agent nội bộ cho team engineering

Đây là use case đơn giản nhưng thực tế.

### Mục tiêu

Xây một agent trả lời câu hỏi nội bộ về:
- coding guideline
- naming convention
- release process
- branch strategy

### Phiên bản đơn giản nhất

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

public class EngineeringFaqAgentFactory
{
    public static AIAgent Create()
    {
        var endpoint = new Uri("https://your-foundry-service.services.ai.azure.com/api/projects/internal-assistant");

        return new AIProjectClient(endpoint, new AzureCliCredential())
            .AsAIAgent(
                name: "EngineeringFaqAgent",
                model: "gpt-5.4-mini",
                instructions: @"
You are an internal engineering FAQ assistant.
Answer only based on the engineering handbook and onboarding policies provided in context.
If information is missing, say you are not certain and ask the user to check the official handbook.
Be concise, factual, and avoid inventing policy.");
    }
}

internal class Program
{
    static async Task Main()
    {
        var agent = EngineeringFaqAgentFactory.Create();

        var response = await agent.RunAsync("What is our branch naming convention for bug fixes?");
        Console.WriteLine(response);
    }
}
```

### Nhược điểm của phiên bản này

- chưa có tool để tra handbook thật
- chưa có session
- chưa có memory
- agent có thể hallucinate nếu instruction không đủ chặt

Vì vậy ta cần bước tiếp theo: thêm tools.

---

## 8. Tools trong Agent Framework, tại sao cực kỳ quan trọng?

Agent không nên chỉ sống bằng prompt và trí nhớ nội bộ của model. Nếu cần tương tác thế giới thật hoặc dữ liệu thật, agent nên có tools.

Một vài tool điển hình:
- lấy dữ liệu đơn hàng
- đọc catalog sản phẩm
- tra trạng thái shipment
- tạo support ticket
- tính toán tài chính
- tìm document nội bộ

### Tại sao tool quan trọng hơn nhồi prompt?

Vì tool:
- cập nhật dữ liệu real-time
- giảm hallucination
- cho phép app kiểm soát capability
- dễ test hơn
- dễ audit hơn

### Mẫu use case: Product Support Agent

Agent trả lời khách hàng về sản phẩm, nhưng khi cần giá hiện tại hay tồn kho, nó sẽ gọi tool.

#### ProductService giả lập

```csharp
public record ProductDto(string Sku, string Name, decimal Price, int Stock);

public interface IProductService
{
    Task<ProductDto?> GetBySkuAsync(string sku);
}

public class InMemoryProductService : IProductService
{
    private readonly Dictionary<string, ProductDto> _products = new()
    {
        ["KB-001"] = new("KB-001", "Mechanical Keyboard", 129.99m, 42),
        ["MS-002"] = new("MS-002", "Wireless Mouse", 59.99m, 0),
        ["MN-003"] = new("MN-003", "4K Monitor", 499.00m, 12)
    };

    public Task<ProductDto?> GetBySkuAsync(string sku)
    {
        _products.TryGetValue(sku, out var result);
        return Task.FromResult(result);
    }
}
```

#### Tool wrapper

Vì API tool có thể thay đổi theo package version, cách thực tế là bạn bọc business function gọn, rõ input/output. Ví dụ:

```csharp
public class ProductTools
{
    private readonly IProductService _productService;

    public ProductTools(IProductService productService)
    {
        _productService = productService;
    }

    public async Task<string> GetProductInfoAsync(string sku)
    {
        var product = await _productService.GetBySkuAsync(sku);

        if (product is null)
            return $"Product with SKU '{sku}' was not found.";

        return $"SKU: {product.Sku}, Name: {product.Name}, Price: {product.Price}, Stock: {product.Stock}";
    }
}
```

#### Gắn tool vào agent, về mặt ý tưởng

Pseudo-setup kiểu điển hình sẽ là:

```csharp
var productTools = new ProductTools(new InMemoryProductService());

var agent = projectClient.AsAIAgent(
    name: "SupportAgent",
    model: "gpt-5.4-mini",
    instructions: @"
You are a customer support assistant.
If the user asks about price, SKU, or stock, use the available tools.
Never guess inventory or price.",
    tools: new[]
    {
        // framework-specific tool registration abstraction
        // e.g. function tool wrappers around productTools.GetProductInfoAsync
    });
```

Tùy version package, cú pháp tool registration có thể khác. Nhưng về mặt kiến trúc, bạn nên đi theo pattern này:
- function input rõ kiểu dữ liệu
- output gọn, dễ parse
- tool chỉ expose đúng capability cần thiết
- không lộ toàn bộ service layer thô

### Ví dụ tương tác mong muốn

User:
- “Giá của mã KB-001 là bao nhiêu?”

Agent sẽ:
1. nhận biết cần dùng tool
2. gọi `GetProductInfoAsync("KB-001")`
3. đọc kết quả
4. trả lời:
   - “Mechanical Keyboard, SKU KB-001, giá hiện tại là 129.99 USD, còn 42 sản phẩm trong kho.”

### Một bài học thực chiến

Tool nên càng nhỏ và càng ít side effect càng tốt. Nếu tool vừa đọc dữ liệu vừa tạo đơn hàng vừa gửi mail, agent sẽ khó kiểm soát và hệ thống sẽ rủi ro cao.

---

## 9. Multi-turn conversation và session

Đây là thứ tách “chat completion app” ra khỏi “agent app” nghiêm túc.

Nếu không có session, mỗi lần gọi agent là một lần độc lập. Nhưng trong đời thật, user nói kiểu này:

- “Tôi đang tìm chuột không dây.”
- “Có loại nào dưới 60 đô?”
- “Con đó còn hàng không?”
- “Tóm tắt lại giúp tôi.”

Nếu không có session, lượt 3 sẽ không biết “con đó” là gì.

### Session giải quyết gì?

- giữ lịch sử hội thoại
- gắn state theo người dùng hoặc conversation
- hỗ trợ memory/persistence
- giúp multi-turn logic tự nhiên hơn

### Ví dụ ý tưởng tạo session

Docs chính thức có nhắc đến `AgentSession` và `InMemoryAgentSession`. Với custom agent, bạn có thể có session riêng.

Pseudo-code theo style docs:

```csharp
using Microsoft.Agents.AI;

internal sealed class ShoppingAssistantSession : InMemoryAgentSession
{
    public string? LastViewedSku { get; set; }
    public string? PreferredCategory { get; set; }
}
```

### Một custom agent giữ state đơn giản

```csharp
using Microsoft.Agents.AI;

internal sealed class ShoppingAssistantAgent : AIAgent
{
    public override async Task<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new ShoppingAssistantSession());
    }

    public override async Task<AgentSession> DeserializeSessionAsync(string json, CancellationToken cancellationToken = default)
    {
        var session = System.Text.Json.JsonSerializer.Deserialize<ShoppingAssistantSession>(json)
                      ?? new ShoppingAssistantSession();

        return await Task.FromResult(session);
    }

    public override async Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        CancellationToken cancellationToken = default)
    {
        session ??= await CreateSessionAsync(cancellationToken);
        var shoppingSession = (ShoppingAssistantSession)session;

        var latestUserInput = messages.LastOrDefault()?.Text ?? string.Empty;

        if (latestUserInput.Contains("keyboard", StringComparison.OrdinalIgnoreCase))
        {
            shoppingSession.PreferredCategory = "keyboard";
        }

        var reply = $"You said: {latestUserInput}. Preferred category = {shoppingSession.PreferredCategory ?? "unknown"}";

        return new AgentResponse(reply);
    }
}
```

Đoạn trên mang tính minh họa cho ý tưởng session-aware custom agent. Tên type hoặc constructor cụ thể có thể thay đổi theo package version, nhưng pattern thiết kế là đúng:
- session là state object
- agent đọc/ghi session trong mỗi lượt chạy
- session có thể serialize/deserialize được

### Use case thực tế hơn

Customer support agent:
- lưu `LastOrderId`
- lưu `LastMentionedSku`
- lưu `PreferredLanguage`
- lưu `EscalationRequested`

Ví dụ:

```csharp
internal sealed class SupportSession : InMemoryAgentSession
{
    public string? LastOrderId { get; set; }
    public string? LastMentionedSku { get; set; }
    public bool EscalationRequested { get; set; }
}
```

Điều này giúp agent trả lời các câu kiểu:
- “đơn đó đã giao chưa?”
- “cái tôi hỏi lúc nãy còn hàng không?”

mà không cần user lặp lại toàn bộ context.

---

## 10. Custom agent, khi nào cần tự viết?

Không phải lúc nào cũng cần custom agent. Nếu chỉ dùng model + tools + session built-in là đủ, nên giữ đơn giản.

### Chỉ viết custom agent khi

- bạn cần vòng đời riêng
- bạn cần logic session đặc thù
- bạn cần behavior đặc biệt mà agent mặc định không cung cấp
- bạn muốn kiểm soát rất rõ cách input được xử lý

Theo docs Microsoft, có thể tạo custom agent bằng cách kế thừa `AIAgent`.

### Ví dụ mẫu, UpperCase Parrot Agent kiểu minh họa

```csharp
using Microsoft.Agents.AI;

internal sealed class CustomAgentSession : InMemoryAgentSession
{
}

internal sealed class UpperCaseParrotAgent : AIAgent
{
    public override Task<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AgentSession>(new CustomAgentSession());

    public override Task<AgentSession> DeserializeSessionAsync(string json, CancellationToken cancellationToken = default)
    {
        var session = System.Text.Json.JsonSerializer.Deserialize<CustomAgentSession>(json)
                      ?? new CustomAgentSession();

        return Task.FromResult<AgentSession>(session);
    }

    public override Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var input = string.Join(" ", messages.Select(m => m.Text));
        return Task.FromResult(new AgentResponse(input.ToUpperInvariant()));
    }
}
```

Trong thực tế, custom agent có thể dùng để:
- tiền xử lý input
- chuẩn hóa ngôn ngữ
- cấy policy logic
- route sang nhiều sub-agent
- gắn domain state phức tạp

---

## 11. Middleware, nơi đưa governance và safety vào agent runtime

Middleware là phần nhiều đội bỏ qua lúc demo, nhưng cực quan trọng khi làm production.

### Middleware có thể dùng để làm gì?

- log prompt và tool calls
- chặn prompt injection patterns
- chặn tool nhạy cảm
- thêm tenant context
- đo thời gian thực thi
- áp quota hoặc rate limit mềm
- thêm correlation id cho tracing

### Ví dụ ý tưởng middleware logging

```csharp
public class LoggingAgentMiddleware
{
    private readonly ILogger<LoggingAgentMiddleware> _logger;

    public LoggingAgentMiddleware(ILogger<LoggingAgentMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task<object> InvokeAsync(string input, Func<Task<object>> next)
    {
        _logger.LogInformation("Agent input: {Input}", input);
        var result = await next();
        _logger.LogInformation("Agent output: {Output}", result);
        return result;
    }
}
```

Cách wiring thực tế sẽ tùy theo extension points của package version, nhưng tư duy nên là:
- middleware càng side-effect free càng tốt
- đừng nhét business logic chính vào middleware
- dùng nó cho cross-cutting concerns

### Ví dụ safety middleware cho tool calls

Bạn có một tool tạo refund. Đó là tool nhạy cảm. Middleware có thể bắt buộc:
- user phải có role đúng
- tổng tiền refund dưới ngưỡng nào đó
- phải có human approval nếu vượt ngưỡng

Pseudo-code:

```csharp
public class RefundAuthorizationMiddleware
{
    public async Task<object> InvokeToolAsync(ToolInvocationContext ctx, Func<Task<object>> next)
    {
        if (ctx.ToolName == "CreateRefund")
        {
            var userRole = ctx.Metadata["role"]?.ToString();
            if (userRole != "SupportManager")
            {
                throw new UnauthorizedAccessException("Only SupportManager can invoke refund tool.");
            }
        }

        return await next();
    }
}
```

Đây là cách đúng để làm enterprise AI app. Không đặt niềm tin hoàn toàn vào instruction như “Only issue refunds if allowed”. Phải có enforcement ở application layer.

---

## 12. Memory và context, đừng biến mọi thứ thành prompt dài

Nhiều người nghe “agent memory” là nghĩ tới việc nạp toàn bộ lịch sử hoặc toàn bộ knowledge base vào prompt. Đó là đường ngắn nhất đi tới chi phí cao, latency cao và chất lượng tệ.

### Nguyên tắc memory tốt

- chỉ nạp cái liên quan
- phân biệt short-term session memory và long-term knowledge memory
- dữ liệu nghiệp vụ nên đến qua tool hoặc retrieval thay vì nhét cứng vào prompt
- memory phải có chủ đích, không phải “càng nhiều càng tốt”

### Một thiết kế memory thực tế

#### Short-term memory
- lịch sử 5-10 lượt gần nhất
- last mentioned entities
- user preferences hiện phiên

#### Long-term memory
- hồ sơ khách hàng
- preference đã được lưu bền
- các fact đã được xác minh

#### Retrieval memory
- tìm tài liệu liên quan từ vector store hoặc search index

### Ví dụ use case: Sales Assistant

User hỏi:
- “Khách hàng ACME Corp đang quan tâm gói nào?”

Agent có thể:
1. đọc session để biết current customer context
2. gọi tool tra CRM record
3. lấy thêm note gần nhất từ retrieval system
4. trả lời ngắn gọn theo dữ liệu thật

Không cần nhét cả CRM vào prompt từ đầu.

---

## 13. Workflow trong Agent Framework, chỗ thực sự đáng tiền

Theo tài liệu chính thức, workflow là graph-based, có type-safe routing, checkpointing, và human-in-the-loop support.

Đây là phần rất mạnh nếu bạn xây hệ thống nhiều bước như:
- xử lý hồ sơ bảo hiểm
- review nội dung marketing
- phân loại ticket rồi route sang agent chuyên môn
- tạo báo cáo nhiều bước có kiểm tra chéo

### Khi workflow tốt hơn agent đơn lẻ

Ví dụ bài toán tạo báo cáo nghiên cứu thị trường:
1. thu thập dữ liệu
2. tóm tắt từng nguồn
3. so sánh nguồn
4. viết draft
5. reviewer agent kiểm tra consistency
6. nếu confidence thấp thì gửi người duyệt
7. nếu ổn thì xuất bản

Nếu ép agent đơn lẻ làm hết, bạn sẽ có một prompt to khủng khiếp, khó test, khó kiểm soát.

Workflow tốt hơn vì:
- từng bước rõ ràng
- dễ retry từng đoạn
- dễ checkpoint/resume
- dễ đo lỗi ở đâu
- dễ cắm human approval

### Ví dụ mô hình workflow biên tập nội dung

Giả sử ta có 3 node:
- `CollectResearch`
- `DraftArticle`
- `ReviewArticle`

Pseudo-code concept:

```csharp
public record ResearchRequest(string Topic);
public record ResearchSummary(string Topic, List<string> Notes);
public record ArticleDraft(string Topic, string Content);
public record ReviewResult(string Content, bool Approved, string Feedback);

public class ResearchFunctions
{
    public Task<ResearchSummary> CollectResearchAsync(ResearchRequest request)
    {
        var notes = new List<string>
        {
            $"Collected source A for {request.Topic}",
            $"Collected source B for {request.Topic}",
            $"Collected source C for {request.Topic}"
        };

        return Task.FromResult(new ResearchSummary(request.Topic, notes));
    }
}

public class ArticleAgentFacade
{
    public async Task<ArticleDraft> DraftAsync(AIAgent agent, ResearchSummary summary)
    {
        var prompt = $"Write a practical article about {summary.Topic}. Notes: {string.Join("; ", summary.Notes)}";
        var result = await agent.RunAsync(prompt);
        return new ArticleDraft(summary.Topic, result.ToString());
    }

    public async Task<ReviewResult> ReviewAsync(AIAgent reviewer, ArticleDraft draft)
    {
        var prompt = $"Review this article for factual consistency and clarity: {draft.Content}";
        var result = await reviewer.RunAsync(prompt);

        var text = result.ToString();
        var approved = !text.Contains("major issue", StringComparison.OrdinalIgnoreCase);
        return new ReviewResult(draft.Content, approved, text);
    }
}
```

Workflow engine thực tế sẽ cho bạn graph và routing rõ hơn, nhưng ví dụ trên cho thấy cách nghĩ đúng:
- mỗi step có input/output typed
- agent chỉ tham gia ở bước nào thật sự cần reasoning
- bước deterministic thì cứ viết function thường

### Human-in-the-loop

Đây là điểm rất giá trị.

Ví dụ nếu `ReviewResult.Approved == false`, workflow có thể:
- dừng
- tạo task cho editor
- chờ người sửa hoặc duyệt
- resume từ checkpoint

Đó là kiểu enterprise flow mà agent demo ngoài kia thường không giải quyết đẹp.

---

## 14. Use case mẫu 1, Customer Support Agent có tools, session và escalation

Giờ ta ghép các phần lại thành một ví dụ thực tế hơn.

### Yêu cầu

Agent hỗ trợ khách hàng cần:
- trả lời FAQ
- tra đơn hàng qua tool
- nhớ mã đơn gần nhất trong session
- nếu user yêu cầu người thật thì đánh dấu escalation

### Domain service giả lập

```csharp
public record OrderDto(string OrderId, string Status, DateTime EstimatedDeliveryDate);

public interface IOrderService
{
    Task<OrderDto?> GetOrderAsync(string orderId);
}

public class FakeOrderService : IOrderService
{
    public Task<OrderDto?> GetOrderAsync(string orderId)
    {
        if (orderId == "ORD-1001")
        {
            return Task.FromResult<OrderDto?>(
                new OrderDto("ORD-1001", "In Transit", DateTime.UtcNow.AddDays(2)));
        }

        return Task.FromResult<OrderDto?>(null);
    }
}
```

### Session

```csharp
internal sealed class SupportAgentSession : InMemoryAgentSession
{
    public string? LastOrderId { get; set; }
    public bool EscalationRequested { get; set; }
}
```

### Tools

```csharp
public class SupportTools
{
    private readonly IOrderService _orderService;

    public SupportTools(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<string> GetOrderStatusAsync(string orderId)
    {
        var order = await _orderService.GetOrderAsync(orderId);

        if (order is null)
            return $"Order '{orderId}' was not found.";

        return $"Order {order.OrderId} is currently '{order.Status}' and estimated delivery is {order.EstimatedDeliveryDate:yyyy-MM-dd}.";
    }
}
```

### Agent orchestration service

```csharp
public class SupportAgentService
{
    private readonly AIAgent _agent;
    private readonly SupportTools _tools;

    public SupportAgentService(AIAgent agent, SupportTools tools)
    {
        _agent = agent;
        _tools = tools;
    }

    public async Task<string> HandleAsync(string userInput, SupportAgentSession session)
    {
        if (userInput.Contains("human", StringComparison.OrdinalIgnoreCase) ||
            userInput.Contains("real person", StringComparison.OrdinalIgnoreCase))
        {
            session.EscalationRequested = true;
            return "I understand. I will mark this conversation for human support follow-up.";
        }

        var match = System.Text.RegularExpressions.Regex.Match(userInput, @"ORD-\d+");
        if (match.Success)
        {
            session.LastOrderId = match.Value;
            return await _tools.GetOrderStatusAsync(match.Value);
        }

        if (userInput.Contains("that order", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(session.LastOrderId))
        {
            return await _tools.GetOrderStatusAsync(session.LastOrderId);
        }

        var prompt = $@"
You are a support assistant.
Current session state:
- LastOrderId: {session.LastOrderId}
- EscalationRequested: {session.EscalationRequested}

User message:
{userInput}

Answer helpfully. If the answer depends on real order data and no tool result is available, say you need the order ID.";

        var response = await _agent.RunAsync(prompt);
        return response.ToString();
    }
}
```

### Console demo

```csharp
var foundryAgent = new AIProjectClient(endpoint, new AzureCliCredential())
    .AsAIAgent(
        model: "gpt-5.4-mini",
        name: "SupportAgent",
        instructions: "You are a customer support assistant. Be concise, polite, and practical.");

var service = new SupportAgentService(
    foundryAgent,
    new SupportTools(new FakeOrderService()));

var session = new SupportAgentSession();

Console.WriteLine(await service.HandleAsync("Can you check order ORD-1001?", session));
Console.WriteLine(await service.HandleAsync("When will that order arrive?", session));
Console.WriteLine(await service.HandleAsync("I want a real person.", session));
```

### Kết quả mong muốn

- lần 1: agent/tool tra được ORD-1001
- lần 2: câu “that order” vẫn hiểu là ORD-1001 vì session nhớ
- lần 3: escalation flag được bật

Đây là kiểu use case cực phổ biến và là nơi Agent Framework tỏa sáng hơn việc gọi model thẳng.

---

## 15. Use case mẫu 2, Internal Research Agent có retrieval và reviewer workflow

### Bài toán

Một team strategy muốn nhập topic như:
- “AI coding tools in .NET enterprise teams”

Hệ thống cần:
1. thu thập note từ nguồn dữ liệu nội bộ hoặc search service
2. viết bản tóm tắt đầu tiên
3. reviewer agent kiểm tra chất lượng
4. nếu ổn thì xuất markdown

### Tách phần deterministic và phần agentic

Deterministic:
- lấy tài liệu
- parse source
- khử trùng lặp

Agentic:
- tổng hợp insight
- viết bản nháp
- reviewer đọc và nhận xét

### Mô hình code

```csharp
public record ResearchDocument(string Title, string Content);
public record ResearchPacket(string Topic, IReadOnlyList<ResearchDocument> Documents);
public record DraftReport(string Topic, string Markdown);
public record ReviewDecision(bool Approved, string Feedback);

public interface IResearchRepository
{
    Task<IReadOnlyList<ResearchDocument>> SearchAsync(string topic);
}

public class FakeResearchRepository : IResearchRepository
{
    public Task<IReadOnlyList<ResearchDocument>> SearchAsync(string topic)
    {
        IReadOnlyList<ResearchDocument> docs = new List<ResearchDocument>
        {
            new("Enterprise AI Adoption", "Enterprises care about governance, auditability, and cost."),
            new(".NET Developer Productivity", ".NET teams prefer typed SDKs, observability, and secure deployment models."),
            new("Coding Agent Trends", "Coding agents are increasingly used for scaffolding, test generation, and code review assistance.")
        };

        return Task.FromResult(docs);
    }
}
```

### Draft agent service

```csharp
public class ResearchReportService
{
    private readonly AIAgent _writerAgent;
    private readonly AIAgent _reviewerAgent;
    private readonly IResearchRepository _repository;

    public ResearchReportService(AIAgent writerAgent, AIAgent reviewerAgent, IResearchRepository repository)
    {
        _writerAgent = writerAgent;
        _reviewerAgent = reviewerAgent;
        _repository = repository;
    }

    public async Task<DraftReport> CreateDraftAsync(string topic)
    {
        var docs = await _repository.SearchAsync(topic);
        var context = string.Join("\n\n", docs.Select(d => $"# {d.Title}\n{d.Content}"));

        var prompt = $@"
Write a detailed markdown report about: {topic}
Use only the information below.
Highlight practical recommendations.

{context}
";

        var markdown = await _writerAgent.RunAsync(prompt);
        return new DraftReport(topic, markdown.ToString());
    }

    public async Task<ReviewDecision> ReviewAsync(DraftReport draft)
    {
        var prompt = $@"
Review this markdown report.
Check for clarity, unsupported claims, and missing structure.
Return feedback and say APPROVED or REJECTED.

{draft.Markdown}
";

        var feedback = (await _reviewerAgent.RunAsync(prompt)).ToString();
        var approved = feedback.Contains("APPROVED", StringComparison.OrdinalIgnoreCase);

        return new ReviewDecision(approved, feedback);
    }
}
```

### Orchestration

```csharp
var writer = projectClient.AsAIAgent(
    name: "WriterAgent",
    model: "gpt-5.4-mini",
    instructions: "You are a research writer. Be structured, specific, and practical.");

var reviewer = projectClient.AsAIAgent(
    name: "ReviewerAgent",
    model: "gpt-5.4-mini",
    instructions: "You are a strict reviewer. Reject unsupported claims and vague writing.");

var reportService = new ResearchReportService(writer, reviewer, new FakeResearchRepository());

var draft = await reportService.CreateDraftAsync("AI coding tools in .NET enterprise teams");
var review = await reportService.ReviewAsync(draft);

Console.WriteLine("=== DRAFT ===");
Console.WriteLine(draft.Markdown);
Console.WriteLine("=== REVIEW ===");
Console.WriteLine(review.Feedback);
```

### Nếu muốn production hơn

Bạn sẽ chuyển flow này sang workflow chính thức thay vì orchestration thủ công. Nhưng pattern vẫn là vậy:
- step deterministic riêng
- step agentic riêng
- review riêng
- approval riêng

---

## 16. Use case mẫu 3, triage email hoặc ticket bằng agent + workflow

Đây là một use case AI rất mạnh trong enterprise.

### Bài toán

Bạn có inbox hỗ trợ. Mỗi ticket cần:
1. phân loại chủ đề
2. đánh giá mức độ khẩn cấp
3. nếu là billing thì route team billing
4. nếu là technical issue thì tóm tắt log rồi route kỹ thuật
5. nếu confidence thấp thì đẩy người thật review

### Phần nào nên là agent?

- phân loại nội dung tự nhiên
- trích ý chính
- đánh giá sentiment hoặc urgency sơ bộ

### Phần nào nên là deterministic workflow?

- route queue nào
- ghi DB
- tạo ticket trong hệ thống
- gửi thông báo team đúng nơi

### Mô hình code

```csharp
public record IncomingTicket(string TicketId, string Subject, string Body);
public record TicketClassification(string Category, string Priority, double Confidence, string Summary);

public class TicketTriageService
{
    private readonly AIAgent _classifierAgent;

    public TicketTriageService(AIAgent classifierAgent)
    {
        _classifierAgent = classifierAgent;
    }

    public async Task<TicketClassification> ClassifyAsync(IncomingTicket ticket)
    {
        var prompt = $@"
Classify this support ticket.
Return JSON with fields: category, priority, confidence, summary.
Valid categories: billing, technical, account, general.
Valid priority: low, medium, high, urgent.

Subject: {ticket.Subject}
Body: {ticket.Body}
";

        var raw = (await _classifierAgent.RunAsync(prompt)).ToString();

        // production code should parse JSON robustly
        // simplified placeholder here
        return new TicketClassification("technical", "high", 0.82, "Customer reports login API timeout after deployment.");
    }
}
```

### Workflow routing logic

```csharp
public class TicketRouter
{
    public string ResolveQueue(TicketClassification classification)
    {
        if (classification.Confidence < 0.65)
            return "human-review";

        return classification.Category switch
        {
            "billing" => "billing-queue",
            "technical" => "technical-queue",
            "account" => "account-queue",
            _ => "general-queue"
        };
    }
}
```

### Phần hay của workflow

Nếu confidence thấp:
- pause
- gửi cho human reviewer
- human xác nhận category
- resume workflow

Đây là một ví dụ rất đúng tinh thần Agent Framework: agent dùng để hiểu ngôn ngữ tự nhiên, workflow dùng để điều phối và kiểm soát.

---

## 17. Tích hợp web API, cách đưa agent vào ASP.NET Core

Thay vì console app, đa số bạn sẽ muốn host agent qua API.

### Minimal API mẫu

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var endpoint = new Uri(builder.Configuration["Foundry:Endpoint"]!);
    return new AIProjectClient(endpoint, new AzureCliCredential());
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<AIProjectClient>();
    return client.AsAIAgent(
        name: "ApiHostedAgent",
        model: "gpt-5.4-mini",
        instructions: "You are an API-hosted assistant. Be concise and reliable.");
});

var app = builder.Build();

app.MapPost("/chat", async (ChatRequest request, AIAgent agent) =>
{
    var result = await agent.RunAsync(request.Message);
    return Results.Ok(new ChatResponse(result.ToString()));
});

app.Run();

public record ChatRequest(string Message);
public record ChatResponse(string Message);
```

### Mở rộng cho session theo user

Trong thực tế, bạn cần map session theo user hoặc conversation ID.

```csharp
public interface IAgentSessionStore
{
    Task<SupportAgentSession> GetAsync(string conversationId);
    Task SaveAsync(string conversationId, SupportAgentSession session);
}

public class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly Dictionary<string, SupportAgentSession> _store = new();

    public Task<SupportAgentSession> GetAsync(string conversationId)
    {
        if (!_store.TryGetValue(conversationId, out var session))
        {
            session = new SupportAgentSession();
            _store[conversationId] = session;
        }

        return Task.FromResult(session);
    }

    public Task SaveAsync(string conversationId, SupportAgentSession session)
    {
        _store[conversationId] = session;
        return Task.CompletedTask;
    }
}
```

API endpoint:

```csharp
app.MapPost("/support/chat", async (
    SupportChatRequest request,
    SupportAgentService service,
    IAgentSessionStore store) =>
{
    var session = await store.GetAsync(request.ConversationId);
    var reply = await service.HandleAsync(request.Message, session);
    await store.SaveAsync(request.ConversationId, session);

    return Results.Ok(new SupportChatResponse(reply, session.LastOrderId, session.EscalationRequested));
});

public record SupportChatRequest(string ConversationId, string Message);
public record SupportChatResponse(string Message, string? LastOrderId, bool EscalationRequested);
```

Đây là nền tảng để làm web chat, support bot, internal copilot, v.v.

---

## 18. Production concerns, phần không sexy nhưng cực quan trọng

Nếu bạn chỉ xem demo, Agent Framework trông rất mượt. Nhưng đem vào production thì phải nghĩ nhiều hơn.

### 18.1 Prompt và instruction governance

- version hóa instructions
- đừng hardcode lung tung ở nhiều chỗ
- lưu prompt templates có kiểm soát
- test prompt regression

### 18.2 Tool safety

- phân tách read tool và write tool
- mọi write tool nên có authorization check riêng
- tool side effects lớn cần human approval hoặc policy layer

### 18.3 Logging và observability

Cần log:
- input summary
- tool calls
- latency từng bước
- token usage nếu có
- workflow transitions
- errors và retries

### 18.4 Cost control

- không gửi prompt quá dài vô ích
- dùng model phù hợp, không phải tác vụ nào cũng cần model đắt
- retrieval và tool nên giảm context size
- cache kết quả hợp lý ở các bước deterministic

### 18.5 Hallucination handling

- instruction thôi là không đủ
- cần tool-grounding
- cần explicit “I don’t know” policy
- cần review/human escalation cho case nhạy cảm

### 18.6 Session persistence

Nếu app thật sự cần continuity, session phải persist vào:
- Redis
- SQL/NoSQL store
- blob storage
- hoặc state backend riêng

Không thể dựa vào in-memory session cho production multi-instance.

### 18.7 Testing

Test ở 3 tầng:
1. unit test business function/tool
2. integration test agent setup
3. end-to-end test workflow + safety + fallback

Đừng chỉ test bằng cách chat tay vài câu rồi thấy “ổn phết”.

---

## 19. So sánh Microsoft Agent Framework với các cách tiếp cận khác

### So với gọi OpenAI/Azure OpenAI trực tiếp

Agent Framework hơn ở:
- session abstraction
- tools integration
- middleware
- workflow
- multi-agent direction rõ hơn

Gọi trực tiếp hơn ở:
- đơn giản
- ít abstraction hơn
- hợp với tác vụ nhỏ, deterministic

### So với Semantic Kernel cũ

Agent Framework là thế hệ kế tiếp theo mô tả của chính Microsoft. Nó muốn gom:
- agent model rõ hơn
- workflow graph rõ hơn
- state management mạnh hơn
- multi-agent orchestration rõ hơn

### So với AutoGen

Agent Framework giữ tinh thần agent/multi-agent nhưng thêm:
- enterprise posture tốt hơn
- type safety
- middleware
- session management
- workflow control rõ hơn

### So với các framework agent ngoài hệ Microsoft

Lợi thế:
- rất hợp .NET
- tài liệu chính thống từ Microsoft
- dễ hòa với Azure/Fabric/Foundry ecosystem

Bất lợi:
- ecosystem còn trẻ hơn một số framework agent khác
- API có thể còn evolving, nhất là ở giai đoạn đầu và prerelease/1.0 chuyển giao

---

## 20. Một blueprint kiến trúc thực dụng cho enterprise

Giả sử bạn cần build “Internal Knowledge & Action Assistant” cho công ty.

### Yêu cầu

- trả lời câu hỏi nội bộ
- tra dữ liệu hệ thống nội bộ qua tools
- tạo ticket hỗ trợ nếu cần
- escalate sang người thật với case nhạy cảm
- có audit log
- có session theo user

### Kiến trúc hợp lý

1. **ASP.NET Core API**
   - nhận request từ web/chat clients
2. **Agent Framework**
   - host agent runtime
   - manage sessions
   - call tools
3. **Tool layer**
   - wrappers cho CRM, ticketing, product catalog, search
4. **Workflow layer**
   - xử lý escalation, approvals, review flows
5. **Persistence**
   - session store
   - audit log
   - workflow checkpoint store
6. **Observability**
   - OpenTelemetry
   - structured logging
   - dashboard

### Phân vai rất quan trọng

- Agent: hiểu ý người dùng, chọn tool, tổng hợp câu trả lời
- Tool: cung cấp dữ liệu thật hoặc thực hiện action có kiểm soát
- Workflow: điều phối process nhiều bước, approvals, retries, checkpoint
- API layer: auth, rate limiting, request shaping

Nếu trộn lẫn 4 tầng này vào nhau, hệ thống sẽ nhanh chóng thành spaghetti.

---

## 21. Những sai lầm rất dễ mắc khi dùng Agent Framework

### 1. Dùng agent cho thứ nên là function
Ví dụ format một object JSON sang CSV thì đừng gọi LLM.

### 2. Quá tin vào system prompt
System prompt không thay thế authorization, validation, business rules.

### 3. Tool quá to và quá nguy hiểm
Tool nên nhỏ, rõ input/output, side effect có kiểm soát.

### 4. Không tách workflow khỏi reasoning
Nếu có process nhiều bước, đừng nhét hết vào một prompt duy nhất.

### 5. Không persist session đúng cách
In-memory session chỉ hợp demo hoặc single instance tạm thời.

### 6. Không đo chất lượng đầu ra
AI output cần quality gates, review, và telemetry.

### 7. Không thiết kế fallback
Khi tool fail, model fail, latency tăng hoặc confidence thấp, hệ thống phải có đường fallback.

---

## 22. Checklist triển khai một agent thật sự dùng được

Nếu bạn định bắt đầu với Microsoft Agent Framework, đây là checklist hợp lý:

### Bước 1
Xác định bài toán:
- thật sự cần agent không?
- cần tool không?
- cần workflow không?

### Bước 2
Xác định boundaries:
- tool nào read-only
- tool nào write
- action nào cần approval

### Bước 3
Thiết kế session:
- session giữ gì
- giữ bao lâu
- persist ở đâu

### Bước 4
Thiết kế prompt/instruction:
- persona
- scope
- refusal policy
- safety guidance

### Bước 5
Thiết kế observability:
- log gì
- đo latency/token/cost ra sao
- trace workflow thế nào

### Bước 6
Thiết kế failure modes:
- tool fail thì sao
- confidence thấp thì sao
- cần người thật thì route ra đâu

### Bước 7
Test thực tế:
- happy path
- ambiguous input
- malicious input
- prompt injection
- tool timeout
- user context dài

---

## 23. Kết luận

Microsoft Agent Framework là một bước tiến tự nhiên và quan trọng trong hệ sinh thái AI của Microsoft. Nó đáng chú ý không phải vì nó làm được một chatbot nữa, mà vì nó đưa ra một cấu trúc tương đối rõ cho việc xây agentic application một cách nghiêm túc:

- có agent abstraction
- có sessions
- có tools
- có middleware
- có memory/context
- có workflows
- có đường đi rõ ràng cho multi-agent và long-running scenarios

Điểm mạnh nhất của nó nằm ở chỗ nó cố gắng đặt agent vào đúng vai trò trong một hệ thống phần mềm trưởng thành, thay vì biến mọi thứ thành một prompt to khổng lồ.

Nếu bạn làm .NET và muốn build:
- internal copilot
- support assistant
- sales assistant
- knowledge assistant
- research/report pipeline
- ticket triage system
- multi-step approval flow có AI tham gia

thì Agent Framework là thứ rất đáng học ngay bây giờ.

Nếu phải chốt ngắn gọn bằng một lời khuyên thực dụng:

1. bắt đầu từ một agent nhỏ có tool rõ ràng
2. thêm session khi thật sự cần multi-turn continuity
3. thêm workflow khi process có nhiều bước hoặc cần approval
4. giữ deterministic logic ở function thường
5. xem safety, observability và persistence là requirement từ đầu, không phải phần trang trí về sau

Đó là cách dùng Microsoft Agent Framework để xây hệ thống đáng tin, thay vì chỉ dựng một bản demo nói chuyện hay nhưng không sống nổi ngoài production.
