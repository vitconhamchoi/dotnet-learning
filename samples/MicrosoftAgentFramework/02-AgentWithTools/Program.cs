/*
 * 02 - Agent With Tools (Function Calling)
 * ==========================================
 * Demo cách gắn tools vào agent trong Microsoft Agent Framework.
 * Agent sẽ tự quyết định khi nào cần gọi tool dựa trên ngữ cảnh.
 *
 * Điểm khác biệt so với Semantic Kernel:
 *   - Không cần [KernelFunction] attribute
 *   - Dùng AIFunctionFactory.Create() từ Microsoft.Extensions.AI (chuẩn mở)
 *   - Tools là IList<AITool> — portable, không lock-in vào một framework
 *   - Descriptions được đọc từ [Description] attribute trên method — y hệt SK
 *
 * Tools được demo:
 *   - GetCurrentWeather   : thời tiết theo thành phố (giả lập)
 *   - Multiply            : nhân hai số
 *   - GetCurrentDateTime  : ngày giờ hiện tại (giờ VN)
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

using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

// ── 1. Định nghĩa tools bằng method thường + [Description] ──────────────────
//
// Không cần [KernelFunction] như Semantic Kernel.
// AIFunctionFactory.Create() sẽ đọc [Description] attribute tự động.
// Đây là chuẩn Microsoft.Extensions.AI — dùng được với bất kỳ AI framework nào.

[Description("Lấy thông tin thời tiết hiện tại của một thành phố ở Việt Nam.")]
static string GetCurrentWeather(
    [Description("Tên thành phố, ví dụ: Hà Nội, Hồ Chí Minh, Đà Nẵng")]
    string city)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"\n  🔧 [Tool] GetCurrentWeather(city: \"{city}\")");
    Console.ResetColor();

    var db = new Dictionary<string, (int Temp, string Condition)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Hà Nội"]      = (32, "nắng nhẹ"),
        ["Hanoi"]       = (32, "nắng nhẹ"),
        ["Hồ Chí Minh"] = (35, "nắng gắt"),
        ["Ho Chi Minh"] = (35, "nắng gắt"),
        ["Đà Nẵng"]     = (30, "có mây"),
        ["Da Nang"]     = (30, "có mây"),
    };

    return db.TryGetValue(city.Trim(), out var w)
        ? $"Thời tiết {city}: {w.Temp}°C, {w.Condition}"
        : $"Không có dữ liệu thời tiết cho {city}.";
}

[Description("Tính kết quả nhân hai số nguyên.")]
static long Multiply(
    [Description("Số thứ nhất")] long a,
    [Description("Số thứ hai")]  long b)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"\n  🔧 [Tool] Multiply({a}, {b})");
    Console.ResetColor();
    return a * b;
}

[Description("Trả về ngày và giờ hiện tại theo giờ Việt Nam (UTC+7).")]
static string GetCurrentDateTime()
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("\n  🔧 [Tool] GetCurrentDateTime()");
    Console.ResetColor();
    var vnTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "SE Asia Standard Time");
    return $"Hiện tại: {vnTime:dddd, dd/MM/yyyy HH:mm} (giờ Việt Nam)";
}

// ── 2. Build danh sách tools với AIFunctionFactory ───────────────────────────
//
// AIFunctionFactory.Create() chuyển bất kỳ method nào thành AIFunction (implements AITool).
// Description được lấy từ [Description] attribute trên method và parameters.

var tools = new List<AITool>
{
    AIFunctionFactory.Create(GetCurrentWeather),
    AIFunctionFactory.Create(Multiply),
    AIFunctionFactory.Create(GetCurrentDateTime),
};

// ── 3. Tạo AIAgent với tools ─────────────────────────────────────────────────

string? openAiKey       = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? azureEndpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
string? azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

AIAgent agent;

if (!string.IsNullOrEmpty(openAiKey))
{
    agent = new OpenAIClient(openAiKey)
        .GetChatClient("gpt-4o-mini")
        .AsAIAgent(
            name: "SmartAssistant",
            instructions: """
                Mày là một trợ lý thông minh. Mày có thể:
                - Tra cứu thời tiết của bất kỳ thành phố nào
                - Tính toán phép nhân
                - Cho biết ngày giờ hiện tại

                Khi user hỏi, hãy dùng tool phù hợp. Trả lời bằng tiếng Việt, ngắn gọn.
                """,
            tools: tools);
    Console.WriteLine("✅ Dùng OpenAI (gpt-4o-mini)");
}
else if (!string.IsNullOrEmpty(azureEndpoint))
{
    agent = new AzureOpenAIClient(new Uri(azureEndpoint), new DefaultAzureCredential())
        .GetChatClient(azureDeployment)
        .AsAIAgent(
            name: "SmartAssistant",
            instructions: """
                Mày là một trợ lý thông minh. Mày có thể:
                - Tra cứu thời tiết của bất kỳ thành phố nào
                - Tính toán phép nhân
                - Cho biết ngày giờ hiện tại

                Khi user hỏi, hãy dùng tool phù hợp. Trả lời bằng tiếng Việt, ngắn gọn.
                """,
            tools: tools);
    Console.WriteLine($"✅ Dùng Azure OpenAI ({azureDeployment})");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("❌ Cần set OPENAI_API_KEY hoặc AZURE_OPENAI_ENDPOINT");
    Console.ResetColor();
    return;
}

// ── 4. Demo tự động + interactive mode ──────────────────────────────────────

AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════════");
Console.WriteLine("  Agent With Tools — Microsoft Agent Framework Demo    ");
Console.WriteLine("  Thử hỏi: thời tiết Hà Nội, 1337*42, hôm nay mấy   ");
Console.WriteLine("  Gõ 'quit' để thoát                                  ");
Console.WriteLine("══════════════════════════════════════════════════════");
Console.WriteLine();

// Demo tự động để thấy tool calling trong action
string[] demoQuestions =
[
    "Thời tiết ở Hà Nội hôm nay thế nào?",
    "Tính 1337 * 42 cho tao",
    "Hôm nay là ngày mấy? Và thời tiết Đà Nẵng thế nào?",
];

Console.WriteLine("📌 Chạy demo tự động...");
Console.WriteLine();

foreach (string question in demoQuestions)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"User: {question}");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write($"{agent.Name}: ");
    Console.ResetColor();

    await foreach (AgentResponseUpdate chunk in agent.RunStreamingAsync(question, session))
    {
        Console.Write(chunk.Text);
    }
    Console.WriteLine("\n");
}

// Interactive mode
Console.WriteLine("── Chế độ tương tác ──────────────────────────────────");
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

    await foreach (AgentResponseUpdate chunk in agent.RunStreamingAsync(userInput, session))
    {
        Console.Write(chunk.Text);
    }
    Console.WriteLine("\n");
}

Console.WriteLine("Bye!");
