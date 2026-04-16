/*
 * 01 - Basic Chat Agent
 * =====================
 * Ví dụ cơ bản nhất: tạo một ChatCompletionAgent với system prompt,
 * rồi chat multi-turn với nó qua AgentThread.
 *
 * Cách chạy:
 *   export OPENAI_API_KEY=sk-...
 *   dotnet run
 *
 * Hoặc dùng Azure OpenAI:
 *   export AZURE_OPENAI_ENDPOINT=https://xxx.openai.azure.com/
 *   export AZURE_OPENAI_KEY=xxx
 *   export AZURE_OPENAI_DEPLOYMENT=gpt-4o
 *   dotnet run
 */

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

// ── 1. Đọc config từ environment variables ──────────────────────────────────

string? openAiKey        = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? azureEndpoint    = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string? azureKey         = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
string? azureDeployment  = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o";

// ── 2. Tạo Kernel (lớp cốt lõi của Semantic Kernel) ────────────────────────

IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

if (!string.IsNullOrEmpty(openAiKey))
{
    // OpenAI
    kernelBuilder.AddOpenAIChatCompletion(modelId: "gpt-4o-mini", apiKey: openAiKey);
    Console.WriteLine("✅ Dùng OpenAI (gpt-4o-mini)");
}
else if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
{
    // Azure OpenAI
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

// ── 3. Tạo ChatCompletionAgent ───────────────────────────────────────────────
//
// ChatCompletionAgent là loại agent đơn giản nhất trong SK Agent Framework.
// Nó wrap một LLM call với:
//   - Name: tên định danh agent
//   - Instructions: system prompt
//   - Kernel: chứa chat completion service
//
// Khác với việc gọi IChatCompletionService trực tiếp:
//   - Agent có định danh (Name/Id) → dùng được trong multi-agent
//   - Agent tách Instructions ra khỏi business code
//   - Agent có thể gắn thêm plugins/tools (xem bài 02)

ChatCompletionAgent agent = new()
{
    Name         = "VinaDevAssistant",
    Instructions = """
        Mày là một senior .NET developer người Việt Nam, cộc lốc nhưng cực kỳ giỏi.
        Trả lời bằng tiếng Việt, ngắn gọn, đúng trọng tâm.
        Khi giải thích code thì dùng ví dụ cụ thể, không lan man.
        Nếu câu hỏi không liên quan đến lập trình thì bảo: "Hỏi cái gì vậy, tao chỉ code thôi."
        """,
    Kernel       = kernel,
};

// ── 4. Tạo AgentThread để giữ conversation history ─────────────────────────
//
// AgentThread ~ ChatHistory nhưng dành riêng cho agent.
// Mỗi turn conversation đều được lưu trong thread này.
// Thread tách biệt với agent → nhiều thread có thể dùng chung một agent.

ChatHistoryAgentThread thread = new();

// ── 5. Vòng lặp chat multi-turn ─────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine("  Basic Chat Agent — SK Agent Framework Demo   ");
Console.WriteLine("  Gõ 'quit' hoặc Ctrl+C để thoát              ");
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Mày: ");
    Console.ResetColor();

    string? userInput = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(userInput) || userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write($"{agent.Name}: ");
    Console.ResetColor();

    // InvokeStreamingAsync → nhận response dạng stream (token by token)
    // InvokeAsync        → nhận toàn bộ response một lần
    //
    // Ở đây dùng streaming để UX tốt hơn
    await foreach (StreamingChatMessageContent chunk in agent.InvokeStreamingAsync(userInput, thread))
    {
        Console.Write(chunk.Content);
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine("Bye!");
