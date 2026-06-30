using System.Net;

namespace StudentManagerCore;

/// <summary>
/// IP白名单中间件 — 只允许指定IP地址访问系统。
/// 配置项：appsettings.json 中的 IpRestriction:AllowedIPs，
/// 多个IP用逗号分隔；设为 "*" 或留空则放行所有IP。
/// </summary>
public class IpRestrictionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<IPAddress>? _allowedIPs;
    private readonly bool _allowAll;

    public IpRestrictionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var raw = configuration.GetValue<string>("IpRestriction:AllowedIPs") ?? "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
        {
            _allowAll = true;
        }
        else
        {
            _allowedIPs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ip => IPAddress.TryParse(ip, out var addr) ? addr : null)
                .Where(addr => addr != null)
                .Cast<IPAddress>()
                .ToHashSet();
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 放行以下路径（否则登录页都进不去）
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path == "/account/login" ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/imge"))
        {
            await _next(context);
            return;
        }

        // 获取客户端IP
        var remoteIp = context.Connection.RemoteIpAddress;

        // 端口转发场景（如IIS/Nginx反向代理），从 X-Forwarded-For 取真实IP
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            remoteIp = IPAddress.Parse(forwardedFor.Split(',')[0].Trim());
        }

        // 本地回环地址放行（方便服务器本机调试）
        if (remoteIp != null && IPAddress.IsLoopback(remoteIp))
        {
            await _next(context);
            return;
        }

        // 校验是否允许的IP
        if (!_allowAll && (remoteIp == null || _allowedIPs == null || !_allowedIPs.Contains(remoteIp)))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($@"
<!DOCTYPE html>
<html lang='zh-CN'>
<head><meta charset='UTF-8'><title>访问被拒绝</title>
<style>
    body {{ font-family: 'Microsoft YaHei', sans-serif; background: #f8f9fa; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }}
    .card {{ text-align: center; background: #fff; padding: 50px; border-radius: 16px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); max-width: 480px; }}
    .icon {{ font-size: 64px; color: #dc3545; }}
    h2 {{ color: #333; margin: 16px 0 8px; }}
    p {{ color: #666; font-size: 14px; line-height: 1.6; }}
    .ip {{ background: #f1f3f5; padding: 6px 14px; border-radius: 6px; font-family: monospace; display: inline-block; margin-top: 8px; }}
</style>
</head>
<body>
<div class='card'>
    <div class='icon'>&#128274;</div>
    <h2>访问被拒绝</h2>
    <p>当前设备（<span class='ip'>{remoteIp}</span>）<br>不在允许访问的IP白名单内。</p>
    <p>请联系管理员获取授权访问。</p>
</div>
</body>
</html>");
            return;
        }

        await _next(context);
    }
}
