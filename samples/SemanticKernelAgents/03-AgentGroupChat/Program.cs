/*
 * 03 - Agent Group Chat (Multi-Agent)
 * =====================================
 * Demo AgentGroupChat — nhiều agent nói chuyện với nhau để giải quyết task.
 *
 * Pattern: Planner + Executor + Reviewer
 * ───────────────────────────────────────
 * - PlannerAgent  : nhận yêu cầu, lập kế hoạch bước-by-bước
 * - ExecutorAgent : nhận plan, "thực thi" từng bước và báo cáo kết quả
 * - ReviewerAgent : review kết quả, quyết định approve hoặc yêu cầu sửa
 *
 * Termination Strategy:
 * - Chat kết thúc khi ReviewerAgent nói "APPROVED"
 * - Hoặc sau max 10 turns (tránh infinite loop)
 *
 * Selection Strategy:
 * - KernelFunctionSelectionStrategy: dùng LLM để quyết định agent nào nói tiếp
 *
 * Cách chạy:
 *   export OPENAI_API_KEY=sk-...
 *   dotnet run
 */

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;

// ── 1. Kernel setup ──────────────────────────────────────────────────────────

string? openAiKey       = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? azureEndpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string? azureKey        = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
string? azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o";

IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

if (!string.IsNullOrEmpty(openAiKey))
{
    kernelBuilder.AddOpenAIChatCompletion(modelId: "gpt-4o-mini", apiKey: openAiKey);
    Console.WriteLine("✅ Dùng OpenAI (gpt-4o-mini)");
}
else if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
{
    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: azureDeployment,
        endpoint: azureEndpoint,
        apiKey: azureKey);
    Console.WriteLine($"✅ Dùng Azure OpenAI ({azureDeployment})");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("❌ Cần set OPENAI_API_KEY hoặc AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_KEY");
    Console.ResetColor();
    return;
}

Kernel kernel = kernelBuilder.Build();

// ── 2. Định nghĩa các agents ─────────────────────────────────────────────────

// Agent 1: Planner — nhận yêu cầu và lập kế hoạch chi tiết
ChatCompletionAgent plannerAgent = new()
{
    Name   = "Planner",
    Kernel = kernel,
    Instructions = """
        Mày là Planner. Nhiệm vụ của mày là nhận yêu cầu và đưa ra kế hoạch thực hiện rõ ràng.
        
        Khi nhận yêu cầu:
        1. Phân tích yêu cầu
        2. Đưa ra plan gồm các bước cụ thể, đánh số từng bước
        3. Giải thích lý do cho từng bước
        
        Chỉ lên plan, KHÔNG tự thực thi. Trả lời bằng tiếng Việt.
        """,
};

// Agent 2: Executor — nhận plan và "thực thi" (trong demo này là mô phỏng)
ChatCompletionAgent executorAgent = new()
{
    Name   = "Executor",
    Kernel = kernel,
    Instructions = """
        Mày là Executor. Nhiệm vụ của mày là nhận plan từ Planner và thực thi từng bước.
        
        Khi nhận plan:
        1. Đọc từng bước trong plan
        2. Báo cáo kết quả thực thi mỗi bước (mô phỏng kết quả thực tế)
        3. Tổng kết kết quả cuối cùng
        
        Mô phỏng kết quả thực tế, chi tiết và cụ thể. Trả lời bằng tiếng Việt.
        """,
};

// Agent 3: Reviewer — review kết quả và quyết định
ChatCompletionAgent reviewerAgent = new()
{
    Name   = "Reviewer",
    Kernel = kernel,
    Instructions = """
        Mày là Reviewer. Nhiệm vụ của mày là review kết quả từ Executor.
        
        Tiêu chí review:
        1. Plan có logic và đủ chi tiết không?
        2. Kết quả execution có đáp ứng yêu cầu ban đầu không?
        3. Có vấn đề gì cần sửa không?
        
        Nếu kết quả đạt yêu cầu: kết thúc bằng từ "APPROVED"
        Nếu cần sửa: giải thích vấn đề và yêu cầu Planner làm lại.
        
        Trả lời bằng tiếng Việt.
        """,
};

// ── 3. Tạo AgentGroupChat ────────────────────────────────────────────────────
//
// AgentGroupChat điều phối nhiều agent trong một cuộc hội thoại.
// Hai thành phần quan trọng:
//
// SelectionStrategy: quyết định agent nào nói tiếp theo
//   - SequentialSelectionStrategy: theo thứ tự vòng lặp (Planner→Executor→Reviewer→...)
//   - KernelFunctionSelectionStrategy: dùng LLM để chọn
//
// TerminationStrategy: quyết định khi nào dừng
//   - KernelFunctionTerminationStrategy: dùng LLM để quyết định
//   - MaximumIterationTerminationStrategy: dừng sau N turns

AgentGroupChat groupChat = new(plannerAgent, executorAgent, reviewerAgent)
{
    ExecutionSettings = new AgentGroupChatSettings
    {
        // Selection: Planner → Executor → Reviewer (tuần tự, đơn giản)
        SelectionStrategy = new SequentialSelectionStrategy
        {
            InitialAgent = plannerAgent,
        },

        // Termination: dừng khi Reviewer nói "APPROVED" hoặc sau 9 turns
        TerminationStrategy = new ApprovalTerminationStrategy
        {
            Agents = [reviewerAgent],
            MaximumIterations = 9,
        },
    },
};

// ── 4. Chạy demo ─────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("════════════════════════════════════════════════════");
Console.WriteLine("  Agent Group Chat — SK Agent Framework Demo        ");
Console.WriteLine("  Pattern: Planner → Executor → Reviewer            ");
Console.WriteLine("════════════════════════════════════════════════════");
Console.WriteLine();

// Nhiệm vụ demo
const string task = """
    Thiết kế một REST API đơn giản cho hệ thống quản lý thư viện sách.
    Yêu cầu: CRUD cho Book, Author. Dùng ASP.NET Core Minimal API.
    """;

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"📋 Task: {task}");
Console.ResetColor();
Console.WriteLine();

// Đưa task vào group chat
groupChat.AddChatMessage(new ChatMessageContent(AuthorRole.User, task));

// Lắng nghe từng turn của conversation
await foreach (ChatMessageContent message in groupChat.InvokeAsync())
{
    // In tên agent với màu phân biệt
    Console.ForegroundColor = message.AuthorName switch
    {
        "Planner"  => ConsoleColor.Cyan,
        "Executor" => ConsoleColor.Green,
        "Reviewer" => ConsoleColor.Magenta,
        _          => ConsoleColor.White,
    };

    string icon = message.AuthorName switch
    {
        "Planner"  => "🗺️",
        "Executor" => "⚙️",
        "Reviewer" => "🔍",
        _          => "💬",
    };

    Console.WriteLine($"\n{icon} [{message.AuthorName}]");
    Console.ResetColor();
    Console.WriteLine(message.Content);
    Console.WriteLine(new string('─', 60));
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"✅ Group chat kết thúc. IsComplete: {groupChat.IsComplete}");
Console.ResetColor();

// ── Custom Termination Strategy ──────────────────────────────────────────────
//
// KernelFunctionTerminationStrategy dùng LLM để quyết định dừng.
// Ở đây dùng cách đơn giản hơn: check text "APPROVED" trong response.

sealed class ApprovalTerminationStrategy : TerminationStrategy
{
    protected override Task<bool> ShouldAgentTerminateAsync(
        Agent agent,
        IReadOnlyList<ChatMessageContent> history,
        CancellationToken cancellationToken)
    {
        // Dừng nếu tin nhắn cuối từ Reviewer chứa "APPROVED"
        var lastMessage = history[^1];
        bool shouldStop = lastMessage.AuthorName == "Reviewer"
            && (lastMessage.Content?.Contains("APPROVED", StringComparison.OrdinalIgnoreCase) ?? false);

        return Task.FromResult(shouldStop);
    }
}
