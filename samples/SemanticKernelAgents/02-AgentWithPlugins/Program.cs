/*
 * 02 - Agent With Plugins (Tools)
 * ================================
 * Ví dụ này demo cách gắn KernelPlugin vào agent.
 * Agent sẽ tự quyết định khi nào gọi tool dựa trên ngữ cảnh.
 *
 * Tools được demo:
 *   - WeatherPlugin  : giả lập lấy thời tiết theo thành phố
 *   - MathPlugin     : tính toán đơn giản
 *   - DateTimePlugin : trả về ngày/giờ hiện tại
 *
 * Concept quan trọng:
 *   - KernelFunction được tạo từ method có [KernelFunction] attribute
 *   - [Description] attribute giúp LLM hiểu tool dùng để làm gì
 *   - ToolCallBehavior.AutoInvokeKernelFunctions → agent tự gọi tool
 *
 * Cách chạy:
 *   export OPENAI_API_KEY=sk-...
 *   dotnet run
 */

using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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

// ── 2. Đăng ký plugins vào Kernel ───────────────────────────────────────────
//
// Mỗi plugin là một class thường, method được đánh dấu [KernelFunction].
// [Description] rất quan trọng — đây là text LLM đọc để quyết định dùng tool nào.

kernelBuilder.Plugins.AddFromType<WeatherPlugin>("Weather");
kernelBuilder.Plugins.AddFromType<MathPlugin>("Math");
kernelBuilder.Plugins.AddFromType<DateTimePlugin>("DateTime");

Kernel kernel = kernelBuilder.Build();

// ── 3. Tạo agent với FunctionChoiceBehavior ──────────────────────────────────
//
// FunctionChoiceBehavior.Auto() → agent tự quyết định khi nào gọi tool
// (thay thế cho ToolCallBehavior.AutoInvokeKernelFunctions của phiên bản cũ)

ChatCompletionAgent agent = new()
{
    Name         = "SmartAssistant",
    Instructions = """
        Mày là một trợ lý thông minh. Mày có thể:
        - Tra cứu thời tiết của bất kỳ thành phố nào
        - Tính toán biểu thức toán học
        - Cho biết ngày giờ hiện tại
        
        Khi user hỏi, hãy dùng tool phù hợp để lấy thông tin thực tế.
        Trả lời bằng tiếng Việt, ngắn gọn.
        """,
    Kernel       = kernel,
    Arguments    = new KernelArguments(new OpenAIPromptExecutionSettings
    {
        // Auto → LLM tự quyết định gọi tool nào, bao nhiêu lần
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    }),
};

// ── 4. Demo conversation ─────────────────────────────────────────────────────

ChatHistoryAgentThread thread = new();

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("  Agent With Plugins — SK Agent Framework Demo    ");
Console.WriteLine("  Thử hỏi: thời tiết Hà Nội, 123*456, hôm nay   ");
Console.WriteLine("  Gõ 'quit' để thoát                             ");
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine();

// Demo một số câu hỏi tự động để thấy tool calling
string[] demoQuestions =
[
    "Thời tiết ở Hà Nội hôm nay thế nào?",
    "Tính 1337 * 42 cho tao",
    "Hôm nay là ngày mấy? Và thời tiết Đà Nẵng thế nào?",
];

Console.WriteLine("📌 Chạy demo tự động trước...");
Console.WriteLine();

foreach (string question in demoQuestions)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"User: {question}");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write($"{agent.Name}: ");
    Console.ResetColor();

    await foreach (StreamingChatMessageContent chunk in agent.InvokeStreamingAsync(question, thread))
    {
        Console.Write(chunk.Content);
    }

    Console.WriteLine();
    Console.WriteLine();
}

// Interactive mode
Console.WriteLine("── Chế độ tương tác ────────────────────────────");
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

    await foreach (StreamingChatMessageContent chunk in agent.InvokeStreamingAsync(userInput, thread))
    {
        Console.Write(chunk.Content);
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine("Bye!");

// ── Plugin Definitions ───────────────────────────────────────────────────────
//
// Plugin là POCO class, không cần implement interface nào.
// [KernelFunction]  → đánh dấu method này là tool
// [Description]     → mô tả cho LLM đọc (rất quan trọng cho accuracy)

public class WeatherPlugin
{
    private static readonly Dictionary<string, (int Temp, string Condition)> FakeWeatherDb = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Hà Nội"]      = (32, "nắng nhẹ"),
        ["Hanoi"]       = (32, "nắng nhẹ"),
        ["Hồ Chí Minh"] = (35, "nắng gắt"),
        ["Ho Chi Minh"] = (35, "nắng gắt"),
        ["Saigon"]      = (35, "nắng gắt"),
        ["Đà Nẵng"]     = (30, "có mây"),
        ["Da Nang"]     = (30, "có mây"),
        ["Hải Phòng"]   = (31, "oi bức"),
        ["Cần Thơ"]     = (34, "nắng"),
    };

    [KernelFunction]
    [Description("Lấy thông tin thời tiết hiện tại của một thành phố ở Việt Nam. Trả về nhiệt độ và tình trạng thời tiết.")]
    public string GetCurrentWeather(
        [Description("Tên thành phố, ví dụ: Hà Nội, Hồ Chí Minh, Đà Nẵng")]
        string city)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n  🔧 [Tool] WeatherPlugin.GetCurrentWeather(city: \"{city}\")");
        Console.ResetColor();

        if (FakeWeatherDb.TryGetValue(city.Trim(), out var weather))
            return $"Thời tiết {city}: {weather.Temp}°C, {weather.Condition}";

        return $"Không có dữ liệu thời tiết cho {city}. Thử các thành phố: Hà Nội, Hồ Chí Minh, Đà Nẵng.";
    }
}

public class MathPlugin
{
    [KernelFunction]
    [Description("Tính kết quả của một phép nhân hai số nguyên.")]
    public long Multiply(
        [Description("Số thứ nhất")] long a,
        [Description("Số thứ hai")] long b)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n  🔧 [Tool] MathPlugin.Multiply({a}, {b})");
        Console.ResetColor();

        return a * b;
    }

    [KernelFunction]
    [Description("Tính tổng của hai số.")]
    public double Add(
        [Description("Số thứ nhất")] double a,
        [Description("Số thứ hai")] double b)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n  🔧 [Tool] MathPlugin.Add({a}, {b})");
        Console.ResetColor();

        return a + b;
    }
}

public class DateTimePlugin
{
    [KernelFunction]
    [Description("Trả về ngày và giờ hiện tại theo giờ Việt Nam (UTC+7).")]
    public string GetCurrentDateTime()
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("\n  🔧 [Tool] DateTimePlugin.GetCurrentDateTime()");
        Console.ResetColor();

        var vnTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTime.UtcNow,
            "SE Asia Standard Time");

        return $"Hiện tại: {vnTime:dddd, dd/MM/yyyy HH:mm} (giờ Việt Nam)";
    }
}
