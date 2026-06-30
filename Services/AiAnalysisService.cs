using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudentManagerCore.Services;

/// <summary>
/// AI成绩分析服务 - 调用云端大模型API（OpenAI 兼容格式）
/// 支持 DeepSeek、通义千问、OpenAI 等
/// </summary>
public class AiAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiAnalysisService> _logger;

    public AiAnalysisService(HttpClient httpClient, ILogger<AiAnalysisService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 调用云端AI API生成分析报告
    /// </summary>
    public async Task<string> GenerateAnalysisAsync(
        string prompt,
        string apiUrl,
        string apiKey,
        string modelName,
        double temperature = 0.7,
        int maxTokens = 2048,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        // 拼接 chat/completions 端点
        var baseUrl = apiUrl.TrimEnd('/');
        if (!baseUrl.EndsWith("/chat/completions"))
            baseUrl += "/chat/completions";

        var requestBody = new
        {
            model = modelName,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = temperature,
            max_tokens = maxTokens,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 设置超时
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // 添加 Authorization 请求头
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        _logger.LogInformation(
            "正在调用AI API, 地址={Url}, 模型={Model}, Prompt长度={Len}字",
            baseUrl, modelName, prompt.Length);

        var response = await _httpClient.SendAsync(request, cts.Token);

        // 读取响应体（不管成功还是失败）
        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            // 尝试从响应体中提取具体的错误信息
            var errorMsg = $"HTTP {(int)response.StatusCode}";
            try
            {
                var errorObj = JsonSerializer.Deserialize<ApiErrorResponse>(responseJson);
                if (errorObj?.Error?.Message != null)
                    errorMsg += $" - {errorObj.Error.Message}";
                else if (errorObj?.ErrorMessage != null)
                    errorMsg += $" - {errorObj.ErrorMessage}";
            }
            catch { }

            throw new HttpRequestException(errorMsg);
        }

        // 解析 OpenAI 兼容格式的响应
        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson);
        var text = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";

        _logger.LogInformation("AI API返回完成, 输出长度={Len}字", text.Length);
        return text;
    }

    /// <summary>
    /// 测试AI API连接是否可用
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(
        string apiUrl,
        string apiKey,
        string modelName,
        double temperature = 0.7,
        int maxTokens = 64,
        int timeoutSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = "请回复数字 200500，不要回复其他内容。";
            var result = await GenerateAnalysisAsync(
                prompt, apiUrl, apiKey, modelName,
                0.1, 64, timeoutSeconds, cancellationToken);

            if (string.IsNullOrWhiteSpace(result))
                return (false, "AI 返回了空内容");

            if (result.Contains("200500"))
                return (true, "✅ 连接成功！API 配置可用。");

            return (true, $"⚠️ 连接成功但响应异常：{result.Truncate(50)}");
        }
        catch (TaskCanceledException)
        {
            return (false, "⏱ 请求超时，请检查API地址和网络连接");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"🌐 网络错误：{ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"❌ 连接失败：{ex.Message}");
        }
    }
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        return value?.Length > maxLength ? value[..maxLength] + "..." : value ?? "";
    }
}

/// <summary>
/// OpenAI Chat Completions API 响应模型
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<Choice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public ApiError? Error { get; set; }
}

public class Choice
{
    [JsonPropertyName("message")]
    public ResponseMessage? Message { get; set; }
}

public class ResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public class ApiError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// 通用的错误响应模型（兼容 OpenAI / DeepSeek 等不同格式）
/// </summary>
public class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ApiErrorDetail? Error { get; set; }

    /// <summary>部分API直接用顶层的 message 字段报错</summary>
    [JsonPropertyName("message")]
    public string? ErrorMessage { get; set; }
}

public class ApiErrorDetail
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
