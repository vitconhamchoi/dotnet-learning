# Microsoft Agent Framework — Code Mẫu Chạy Được Ngay

Solution này có **3 project console** demo các concept cốt lõi của Microsoft Agent Framework (MAF).
Mở solution, set API key, `dotnet run` là chạy.

> **Tại sao không phải Semantic Kernel nữa?**
> Microsoft Agent Framework là framework thế hệ mới, hợp nhất Semantic Kernel Agents và AutoGen.
> API gọn hơn, tư duy "agent-first", hỗ trợ workflow graph-based, durable execution, và human-in-the-loop natively.

---

## Yêu cầu

- .NET 10 SDK trở lên
- OpenAI API key **hoặc** Azure OpenAI resource (có thể dùng `az login`)

---

## Cấu hình API Key

### Dùng OpenAI
```bash
# Linux / macOS
export OPENAI_API_KEY=sk-...

# Windows PowerShell
$env:OPENAI_API_KEY="sk-..."
```

### Dùng Azure OpenAI
```bash
export AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
export AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o   # tên deployment

# Dùng DefaultAzureCredential (az login)
az login
```

---

## 3 Projects

### 01 — Basic Agent

**Concept:** Tạo `AIAgent` từ chat client qua `.AsAIAgent()`, multi-turn conversation với `AgentSession`.

```
samples/MicrosoftAgentFramework/01-BasicAgent/
```

**Chạy:**
```bash
cd 01-BasicAgent
dotnet run
```

**Học được gì:**
- `chatClient.AsAIAgent(name, instructions)` — agent không cần Kernel, không cần Builder
- `agent.CreateSessionAsync()` — session quản lý toàn bộ conversation state
- `agent.RunStreamingAsync(message, session)` → stream từng token, response qua `AgentResponseUpdate.Text`
- So sánh: SK dùng `ChatCompletionAgent` + `ChatHistoryAgentThread`; MAF dùng `AIAgent` + `AgentSession`

---

### 02 — Agent With Tools (Function Calling)

**Concept:** Gắn tools vào agent dùng `AIFunctionFactory` từ `Microsoft.Extensions.AI`.

```
samples/MicrosoftAgentFramework/02-AgentWithTools/
```

**Chạy:**
```bash
cd 02-AgentWithTools
dotnet run
```

**Học được gì:**
- `AIFunctionFactory.Create(method)` — tạo tool từ bất kỳ method thường nào
- `[Description]` attribute trên method và parameter → LLM đọc để quyết định dùng tool nào
- Tools được truyền vào `chatClient.AsAIAgent(tools: [...])` — gọn hơn SK rất nhiều
- Standard `Microsoft.Extensions.AI` — tools portable, không lock-in

**Demo tools:**
| Tool | Mô tả |
|------|-------|
| `GetCurrentWeather` | Thời tiết theo thành phố |
| `Multiply` | Nhân hai số |
| `GetCurrentDateTime` | Ngày giờ hiện tại VN |

---

### 03 — Multi-Agent Sequential Workflow

**Concept:** `AgentWorkflowBuilder.BuildSequential()` — workflow nhiều agent, thay thế `AgentGroupChat`.

```
samples/MicrosoftAgentFramework/03-WriterCriticWorkflow/
```

**Chạy:**
```bash
cd 03-WriterCriticWorkflow
dotnet run
```

**Học được gì:**
- `AgentWorkflowBuilder.BuildSequential([agents])` — 1 dòng thay vì cả đống SelectionStrategy/TerminationStrategy
- `InProcessExecution.RunStreamingAsync(workflow, task)` — chạy workflow với streaming events
- `TurnToken` — kích hoạt agents bắt đầu xử lý
- `WatchStreamAsync()` — nhận events realtime: `AgentResponseUpdateEvent`, `WorkflowOutputEvent`, `WorkflowErrorEvent`
- `WorkflowBuilder` (nâng cao): graph-based routing với `AddEdge()`, `AddSwitch()`, `AddFanOut()`

**Flow:**
```
User Task
    ↓
Planner  →  lập kế hoạch
    ↓
Executor →  thực thi plan
    ↓
Reviewer →  review kết quả
    ↓ (WorkflowOutputEvent — done)
```

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────────┐
│              Microsoft Agent Framework                │
│                                                       │
│  ┌─────────────┐    ┌───────────────────────────┐   │
│  │  AIAgent     │    │      Workflow              │   │
│  │ .AsAIAgent() │    │  (graph-based, durable)   │   │
│  └──────┬──────┘    └──────────┬────────────────┘   │
│         │                      │                      │
│  ┌──────▼──────────────────────▼────────────────┐   │
│  │           IChatClient (Microsoft.Extensions.AI)│   │
│  │  OpenAI, Azure OpenAI, Anthropic, Ollama...   │   │
│  └───────────────────────────────────────────────┘   │
│                                                       │
│  ┌───────────────────────────────────────────────┐   │
│  │     AgentSession (conversation state)          │   │
│  │     + AIFunctionFactory (portable tools)       │   │
│  └───────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

---

## Packages dùng

| Package | Version | Mục đích |
|---------|---------|---------|
| `Microsoft.Agents.AI.OpenAI` | 1.5.0 | Core agent SDK + OpenAI provider |
| `Microsoft.Agents.AI.Workflows` | 1.5.0 | Workflow orchestration (graph-based) |
| `Microsoft.Extensions.AI` | 10.5.1 | `AIFunctionFactory` tạo tools (chuẩn mở) |
| `Azure.AI.OpenAI` | 2.2.0-beta.4 | Azure OpenAI chat client |
| `Azure.Identity` | 1.13.2 | Azure authentication |

---

## So sánh nhanh SK vs MAF

| Khái niệm | Semantic Kernel | Microsoft Agent Framework |
|-----------|----------------|--------------------------|
| Tạo agent | `new ChatCompletionAgent { Kernel = kernel }` | `chatClient.AsAIAgent(instructions)` |
| Conversation state | `ChatHistoryAgentThread` | `AgentSession` |
| Gọi streaming | `InvokeStreamingAsync(msg, thread)` | `RunStreamingAsync(msg, session)` |
| Tools | `[KernelFunction]` + `KernelPlugin` | `AIFunctionFactory.Create()` + `IList<AITool>` |
| Multi-agent | `AgentGroupChat` + strategies | `AgentWorkflowBuilder` + `WorkflowBuilder` |
| Routing | `TerminationStrategy`, `SelectionStrategy` | `AddEdge()`, `AddSwitch()`, `AddFanOut()` |

---

## Tài liệu liên quan

- [`microsoft-agent-framework-detailed-guide.md`](../../microsoft-agent-framework-detailed-guide.md) — Guide chi tiết kiến trúc và use cases
- [Microsoft Agent Framework GitHub](https://github.com/microsoft/agent-framework) — Repo chính thức, nhiều samples
- [MAF Docs trên MS Learn](https://learn.microsoft.com/en-us/agent-framework/) — Tài liệu chính thức
- [Migration từ SK sang MAF](https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-semantic-kernel)
