using System.Collections.Concurrent;

namespace StudentManagerCore;

/// <summary>
/// 请求频率限制中间件 — 按 IP 对不同类型的接口进行限流，防止恶意刷接口。
/// AI分析接口限流宽松（30次/分钟），不影响正常使用。
/// 登录/验证码接口严格限流（10次/分钟），防暴力破解。
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, SlidingWindow> _counters = new();

    // ========== 限流规则 ==========
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(1);

    private static readonly (Func<string, bool> Matcher, int MaxRequests, string Name)[] _rules =
    [
        // AI分析接口 → 宽松（每个请求耗时10-60秒，正常无法超限）
        (path => path.StartsWith("/score/getaianalysis", StringComparison.OrdinalIgnoreCase), 30, "AI学生分析"),
        (path => path.StartsWith("/score/getaiclassanalysis", StringComparison.OrdinalIgnoreCase), 30, "AI班级分析"),
        (path => path.StartsWith("/score/getaisubjectanalysis", StringComparison.OrdinalIgnoreCase), 30, "AI科目分析"),
        (path => path.StartsWith("/score/getaistudents", StringComparison.OrdinalIgnoreCase), 30, "AI学生列表"),
        (path => path.StartsWith("/score/getaiclasslist", StringComparison.OrdinalIgnoreCase), 30, "AI班级列表"),
        (path => path.StartsWith("/score/getaisubjectlist", StringComparison.OrdinalIgnoreCase), 30, "AI科目列表"),
        (path => path.StartsWith("/score/getaisettings", StringComparison.OrdinalIgnoreCase), 20, "AI设置"),

        // 登录/验证码 → 严格防暴力破解
        (path => path == "/account/login" || path.StartsWith("/account/login?", StringComparison.OrdinalIgnoreCase), 10, "登录"),

        // 其他API（含数据增删改查）
        (path => path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase), 60, "API"),
        (path => true, 0, "") // 放行（不匹配任何规则）
    ];

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // 静态文件放行（css/js/图片等）
        if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/imge", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 查找匹配的限流规则
        int maxRequests = 0;
        string ruleName = "";
        foreach (var (matcher, limit, name) in _rules)
        {
            if (matcher(path))
            {
                if (limit == 0) // 放行
                {
                    await _next(context);
                    return;
                }
                maxRequests = limit;
                ruleName = name;
                break;
            }
        }

        if (maxRequests == 0) // 未匹配任何规则，放行
        {
            await _next(context);
            return;
        }

        // 获取客户端IP
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            remoteIp = forwardedFor.Split(',')[0].Trim();
        }

        // 本地回环地址放行（方便本机调试）
        if (context.Connection.RemoteIpAddress != null &&
            System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
        {
            await _next(context);
            return;
        }

        var key = $"{remoteIp}:{ruleName}";
        var now = DateTime.UtcNow;

        var window = _counters.GetOrAdd(key, _ => new SlidingWindow());
        lock (window)
        {
            // 窗口已过期，重置
            if (now - window.WindowStart >= WindowSize)
            {
                window.WindowStart = now;
                window.Count = 0;
            }

            window.Count++;

            if (window.Count > maxRequests)
            {
                var retryAfter = (int)Math.Ceiling((WindowSize - (now - window.WindowStart)).TotalSeconds);
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.WriteAsync($@"
<!DOCTYPE html>
<html lang='zh-CN'>
<head><meta charset='UTF-8'><title>请求过于频繁</title>
<style>
    body {{ font-family: 'Microsoft YaHei', sans-serif; background: #f8f9fa; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }}
    .card {{ text-align: center; background: #fff; padding: 50px; border-radius: 16px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); max-width: 480px; }}
    .icon {{ font-size: 64px; color: #dc3545; }}
    h2 {{ color: #333; margin: 16px 0 8px; }}
    p {{ color: #666; font-size: 14px; line-height: 1.6; }}
</style>
</head>
<body>
<div class='card'>
    <div class='icon'>&#9200;</div>
    <h2>请求过于频繁</h2>
    <p>当前接口（{ruleName}）每分钟限制 {maxRequests} 次请求。</p>
    <p>请等待 <strong>{retryAfter}</strong> 秒后重试。</p>
</div>
</body>
</html>").ConfigureAwait(false);
                return;
            }
        }

        await _next(context);
    }

    private class SlidingWindow
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
    }
}
