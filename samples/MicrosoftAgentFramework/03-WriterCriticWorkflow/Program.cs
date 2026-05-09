/*
 * 03 - Multi-Agent Sequential Workflow
 * ======================================
 * Demo Microsoft Agent Framework Workflows — thay thế cho AgentGroupChat của Semantic Kernel.
 *
 * Pattern: Planner → Executor → Reviewer (sequential)
 * ─────────────────────────────────────────────────────
 * - PlannerAgent  : nhận yêu cầu, lập kế hoạch bước-by-bước
 * - ExecutorAgent : nhận plan, thực thi từng bước và báo cáo kết quả
 * - ReviewerAgent : review kết quả và đưa ra nhận xét cuối
 *
 * Điểm khác biệt so với SK AgentGroupChat:
 *   - Workflow có cấu trúc rõ ràng, declarative
 *   - AgentWorkflowBuilder.BuildSequential() — gọn hơn nhiều so với AgentGroupChat
 *   - Graph-based: AddEdge(), AddSwitch(), AddFanOut() cho phép tạo flow phức tạp
 *   - Human-in-the-loop, checkpointing, durable execution hỗ trợ natively
 *   - Không cần TerminationStrategy hay SelectionStrategy tự custom
 *
 * Cách chạy:
 *   export OPENAI_API_KEY=sk-...
 *   dotnet run
 *
 *   Hoặc Azure OpenAI:
 *   export AZURE_OPENAI_ENDPOINT=https://xxx.openai.azure.com/
 *   export AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o
 *   az login
 *   dotnet run
 */

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI;
using OpenAI.Chat;

// ── 1. Setup chat client ─────────────────────────────────────────────────────

string? openAiKey       = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? azureEndpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string? azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

ChatClient chatClient;

if (!string.IsNullOrEmpty(openAiKey))
{
    chatClient = new OpenAIClient(openAiKey).GetChatClient("gpt-4o-mini");
    Console.WriteLine("✅ Dùng OpenAI (gpt-4o-mini)");
}
else if (!string.IsNullOrEmpty(azureEndpoint))
{
    chatClient = new AzureOpenAIClient(new Uri(azureEndpoint), new DefaultAzureCredential())
        .GetChatClient(azureDeployment);
    Console.WriteLine($"✅ Dùng Azure OpenAI ({azureDeployment})");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("❌ Cần set OPENAI_API_KEY hoặc AZURE_OPENAI_ENDPOINT");
    Console.ResetColor();
    return;
}

// ── 2. Định nghĩa các agents ─────────────────────────────────────────────────
//
// Mỗi agent chỉ là một AIAgent với instructions riêng.
// Không cần class riêng, không cần inheritance như SK.

AIAgent plannerAgent = chatClient.AsAIAgent(
    name: "Planner",
    instructions: """
        Mày là Planner. Nhiệm vụ của mày là nhận yêu cầu và đưa ra kế hoạch thực hiện rõ ràng.
        Khi nhận yêu cầu:
        1. Phân tích yêu cầu
        2. Đưa ra plan gồm các bước cụ thể, đánh số từng bước
        3. Giải thích lý do cho từng bước
        Chỉ lên plan, KHÔNG tự thực thi. Trả lời bằng tiếng Việt.
        """);

AIAgent executorAgent = chatClient.AsAIAgent(
    name: "Executor",
    instructions: """
        Mày là Executor. Nhiệm vụ của mày là nhận plan từ Planner và thực thi từng bước.
        Khi nhận plan:
        1. Đọc từng bước trong plan
        2. Báo cáo kết quả thực thi mỗi bước (mô phỏng kết quả thực tế, chi tiết và cụ thể)
        3. Tổng kết kết quả cuối cùng
        Trả lời bằng tiếng Việt.
        """);

AIAgent reviewerAgent = chatClient.AsAIAgent(
    name: "Reviewer",
    instructions: """
        Mày là Reviewer. Nhiệm vụ của mày là review kết quả từ Executor.
        Tiêu chí review:
        1. Plan có logic và đủ chi tiết không?
        2. Kết quả execution có đáp ứng yêu cầu ban đầu không?
        3. Có vấn đề gì cần cải thiện không?
        Đưa ra nhận xét khách quan, nêu cả điểm tốt lẫn điểm cần cải thiện.
        Trả lời bằng tiếng Việt.
        """);

// ── 3. Build Workflow với AgentWorkflowBuilder ───────────────────────────────
//
// AgentWorkflowBuilder.BuildSequential() — thay thế cho AgentGroupChat + SequentialSelectionStrategy
//
// Workflow graph: Planner → Executor → Reviewer
//
// Muốn flow phức tạp hơn? Dùng WorkflowBuilder:
//   var workflow = new WorkflowBuilder(plannerAgent)
//       .AddEdge(plannerAgent, executorAgent)
//       .AddSwitch(reviewerAgent, sw => sw                     // routing điều kiện
//           .AddCase<bool>(approved => approved, doneNode)
//           .AddCase<bool>(approved => !approved, plannerAgent))
//       .Build();

Workflow workflow = AgentWorkflowBuilder.BuildSequential(
    workflowName: "PlannerExecutorReviewer",
    agents: [plannerAgent, executorAgent, reviewerAgent]);

// ── 4. Chạy workflow ─────────────────────────────────────────────────────────

const string task = """
    Thiết kế một REST API đơn giản cho hệ thống quản lý thư viện sách.
    Yêu cầu: CRUD cho Book và Author, dùng ASP.NET Core Minimal API với Entity Framework Core.
    """;

Console.WriteLine();
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine("  Multi-Agent Workflow — Microsoft Agent Framework Demo  ");
Console.WriteLine("  Pattern: Planner → Executor → Reviewer                ");
Console.WriteLine("════════════════════════════════════════════════════════");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine($"📋 Task: {task.Trim()}");
Console.ResetColor();
Console.WriteLine();

// InProcessExecution.RunStreamingAsync chạy workflow và trả về StreamingRun
// TInput ở đây là string — framework sẽ tự convert thành ChatMessage(User, task)
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, task);

// TurnToken kích hoạt các agents bắt đầu xử lý
// emitEvents: true → WatchStreamAsync() nhận các event realtime
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// ── 5. Lắng nghe events từ workflow ─────────────────────────────────────────
//
// WatchStreamAsync() trả về IAsyncEnumerable<WorkflowEvent>.
// Các event types quan trọng:
//   AgentResponseUpdateEvent : chunk streaming từ một agent (realtime)
//   AgentResponseEvent       : response đầy đủ từ một agent (sau khi xong)
//   ExecutorCompletedEvent   : một agent/executor đã xong
//   WorkflowOutputEvent      : output cuối cùng của toàn workflow
//   WorkflowErrorEvent       : lỗi xảy ra
//   ExecutorFailedEvent      : một agent/executor bị lỗi

string? currentAgent = null;

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case AgentResponseUpdateEvent update:
            // Chunk streaming — in realtime từng token của agent
            if (update.ExecutorId != currentAgent)
            {
                if (currentAgent != null)
                    Console.WriteLine();
                currentAgent = update.ExecutorId;

                Console.ForegroundColor = currentAgent switch
                {
                    "Planner"  => ConsoleColor.Cyan,
                    "Executor" => ConsoleColor.Green,
                    "Reviewer" => ConsoleColor.Magenta,
                    _          => ConsoleColor.White,
                };
                string icon = currentAgent switch
                {
                    "Planner"  => "🗺️",
                    "Executor" => "⚙️",
                    "Reviewer" => "🔍",
                    _          => "💬",
                };
                Console.WriteLine($"\n{icon} [{currentAgent}]");
                Console.ResetColor();
            }
            Console.Write(update.Update.Text);
            break;

        case WorkflowOutputEvent output:
            Console.WriteLine();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"✅ Workflow hoàn tất. Output cuối cùng từ: {output.ExecutorId}");
            Console.ResetColor();
            break;

        case WorkflowErrorEvent error:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"\n❌ Workflow error: {error.Data}");
            Console.ResetColor();
            break;

        case ExecutorFailedEvent failed:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"\n❌ Agent '{failed.ExecutorId}' failed: {failed.Data}");
            Console.ResetColor();
            break;
    }
}

Console.WriteLine();
