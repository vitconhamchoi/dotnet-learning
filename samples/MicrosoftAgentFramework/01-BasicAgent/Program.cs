/*
 * 01 - Basic Agent
 * ================
 * Ví dụ cơ bản nhất với Microsoft Agent Framework:
 * Tạo một AIAgent với system instructions, rồi chat multi-turn qua AgentSession.
 *
 * Khác gì so với Semantic Kernel?
 *   - Không cần Kernel, không cần IKernelBuilder phức tạp
 *   - Agent được tạo trực tiếp từ chat client qua .AsAIAgent()
 *   - Session được tạo và quản lý độc lập: await agent.CreateSessionAsync()
 *   - API gọn hơn, tư duy "agent-first" thay vì "kernel-with-plugins"
 *
 * Cách chạy:
 *   # OpenAI
 *   export OPENAI_API_KEY=sk-...
 *   dotnet run
 *
 *   # Hoặc Azure OpenAI
 *   export AZURE_OPENAI_ENDPOINT=https://xxx.openai.azure.com/
 *   export AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o
 *   az login   # dùng DefaultAzureCredential
 *   dotnet run
 */

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;

// ── 1. Tạo AIAgent từ chat client ────────────────────────────────────────────
//
// Microsoft Agent Framework không cần Kernel hay Builder phức tạp.
// Chỉ cần một chat client (OpenAI hoặc Azure OpenAI) rồi gọi .AsAIAgent().
//
// AsAIAgent() là extension method từ Microsoft.Agents.AI.OpenAI.
// Nó wrap chat client thành AIAgent abstraction — chuẩn của framework.

string? openAiKey      = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? azureEndpoint  = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string? azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

AIAgent agent;

if (!string.IsNullOrEmpty(openAiKey))
{
    // OpenAI
    agent = new OpenAIClient(openAiKey)
        .GetChatClient("gpt-4o-mini")
        .AsAIAgent(
            name: "VinaDevAssistant",
            instructions: """
                Mày là một senior .NET developer người Việt Nam, cộc lốc nhưng cực kỳ giỏi.
                Trả lời bằng tiếng Việt, ngắn gọn, đúng trọng tâm.
                Khi giải thích code thì dùng ví dụ cụ thể, không lan man.
                Nếu câu hỏi không liên quan đến lập trình thì bảo: "Hỏi cái gì vậy, tao chỉ code thôi."
                """);
    Console.WriteLine("✅ Dùng OpenAI (gpt-4o-mini)");
}
else if (!string.IsNullOrEmpty(azureEndpoint))
{
    // Azure OpenAI — dùng DefaultAzureCredential (az login trước)
    agent = new AzureOpenAIClient(
            new Uri(azureEndpoint),
            new DefaultAzureCredential())
        .GetChatClient(azureDeployment)
        .AsAIAgent(
            name: "VinaDevAssistant",
            instructions: """
                Mày là một senior .NET developer người Việt Nam, cộc lốc nhưng cực kỳ giỏi.
                Trả lời bằng tiếng Việt, ngắn gọn, đúng trọng tâm.
                Khi giải thích code thì dùng ví dụ cụ thể, không lan man.
                Nếu câu hỏi không liên quan đến lập trình thì bảo: "Hỏi cái gì vậy, tao chỉ code thôi."
                """);
    Console.WriteLine($"✅ Dùng Azure OpenAI ({azureDeployment})");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("❌ Cần set OPENAI_API_KEY hoặc AZURE_OPENAI_ENDPOINT");
    Console.ResetColor();
    return;
}

// ── 2. Tạo AgentSession để giữ conversation history ─────────────────────────
//
// AgentSession là abstraction mới của Microsoft Agent Framework.
// Nó theo dõi toàn bộ state của một cuộc hội thoại.
//
// Không giống ChatHistoryAgentThread của SK — session ở đây là first-class citizen:
//   - Có thể serialize/deserialize để lưu bền vững
//   - Có thể custom để lưu state business (xem bài 03 advanced)
//   - Tách biệt hoàn toàn với agent → một agent phục vụ nhiều session song song

AgentSession session = await agent.CreateSessionAsync();

// ── 3. Vòng lặp chat multi-turn ─────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Basic Agent — Microsoft Agent Framework Demo          ");
Console.WriteLine("  Gõ 'quit' hoặc Ctrl+C để thoát                      ");
Console.WriteLine("═══════════════════════════════════════════════════════");
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

    // RunStreamingAsync — nhận response dạng stream (token by token)
    // Session được truyền vào để framework theo dõi lịch sử hội thoại
    // AgentResponseUpdate.Text chứa text chunk của response
    await foreach (AgentResponseUpdate chunk in agent.RunStreamingAsync(userInput, session))
    {
        Console.Write(chunk.Text);
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine("Bye!");
