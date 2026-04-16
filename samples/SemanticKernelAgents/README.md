# Semantic Kernel Agent Framework — Code Mẫu Chạy Được Ngay

Solution này có **3 project console** demo các concept cốt lõi của SK Agent Framework.
Mở solution, set API key, `dotnet run` là chạy.

---

## Yêu cầu

- .NET 9 SDK trở lên
- OpenAI API key **hoặc** Azure OpenAI resource

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
export AZURE_OPENAI_KEY=your-key
export AZURE_OPENAI_DEPLOYMENT=gpt-4o   # tên deployment của mày
```

---

## 3 Projects

### 01 — Basic Chat Agent

**Concept:** `ChatCompletionAgent` + `ChatHistoryAgentThread` cho multi-turn conversation.

```
samples/SemanticKernelAgents/01-BasicChatAgent/
```

**Chạy:**
```bash
cd 01-BasicChatAgent
dotnet run
```

**Học được gì:**
- Tạo `ChatCompletionAgent` với `Instructions` (system prompt)
- `ChatHistoryAgentThread` giữ lịch sử hội thoại
- `InvokeStreamingAsync` để nhận response dạng stream

---

### 02 — Agent With Plugins (Tool Calling)

**Concept:** Gắn `KernelPlugin` vào agent. Agent tự quyết định khi nào gọi tool.

```
samples/SemanticKernelAgents/02-AgentWithPlugins/
```

**Chạy:**
```bash
cd 02-AgentWithPlugins
dotnet run
```

**Học được gì:**
- Định nghĩa plugin bằng `[KernelFunction]` và `[Description]`
- `FunctionChoiceBehavior.Auto()` → LLM tự chọn tool
- Thấy tool nào được gọi qua log `[Tool]` màu vàng

**Demo tools:**
| Tool | Mô tả |
|------|-------|
| `WeatherPlugin.GetCurrentWeather` | Thời tiết theo thành phố |
| `MathPlugin.Multiply` | Nhân hai số |
| `MathPlugin.Add` | Cộng hai số |
| `DateTimePlugin.GetCurrentDateTime` | Ngày giờ hiện tại VN |

---

### 03 — Agent Group Chat (Multi-Agent)

**Concept:** `AgentGroupChat` — nhiều agent phối hợp xử lý một task phức tạp.

```
samples/SemanticKernelAgents/03-AgentGroupChat/
```

**Chạy:**
```bash
cd 03-AgentGroupChat
dotnet run
```

**Học được gì:**
- `AgentGroupChat` điều phối nhiều agent
- `SequentialSelectionStrategy` chọn agent theo thứ tự
- Custom `TerminationStrategy` dừng khi có keyword "APPROVED"

**Flow:**
```
User Task
    ↓
Planner  →  lập kế hoạch
    ↓
Executor →  thực thi plan
    ↓
Reviewer →  review kết quả
    ↓ (nếu chưa APPROVED, quay lại Planner)
    ↓ (nếu APPROVED → kết thúc)
```

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────────┐
│                  SK Agent Framework                   │
│                                                       │
│  ┌─────────────────┐    ┌─────────────────────────┐  │
│  │  ChatCompletion  │    │      AgentGroupChat      │  │
│  │     Agent        │    │  (multi-agent orch.)     │  │
│  └────────┬─────────┘    └───────────┬─────────────┘  │
│           │                          │                 │
│  ┌────────▼─────────────────────────▼─────────────┐  │
│  │                   Kernel                         │  │
│  │  ┌─────────────┐  ┌─────────┐  ┌────────────┐  │  │
│  │  │  IChatComp  │  │Plugins  │  │  Memory    │  │  │
│  │  │  Service    │  │(Tools)  │  │  Services  │  │  │
│  │  └─────────────┘  └─────────┘  └────────────┘  │  │
│  └─────────────────────────────────────────────────┘  │
│                                                       │
│  ┌─────────────────────────────────────────────────┐  │
│  │              AgentThread (history)               │  │
│  └─────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

---

## Packages dùng

| Package | Version | Mục đích |
|---------|---------|---------|
| `Microsoft.SemanticKernel` | 1.74.0 | Core framework |
| `Microsoft.SemanticKernel.Agents.Core` | 1.74.0 | Agent Framework |

> **Lưu ý:** `AgentGroupChat` và các strategy trong `03-AgentGroupChat` là experimental API.
> Được suppress bằng `<NoWarn>SKEXP0110</NoWarn>` trong csproj.

---

## Tài liệu liên quan

- [`microsoft-agent-framework-detailed-guide.md`](../../microsoft-agent-framework-detailed-guide.md) — Guide chi tiết
- [SK Agents Docs](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/)
