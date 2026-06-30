namespace StudentManagerCore;

/// <summary>
/// 安全响应头中间件 — 添加基础安全 HTTP 头，防止点击劫持/MIME嗅探/XSS等。
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 防止点击劫持
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // 防止 MIME 类型嗅探
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // 控制 Referer 头发送策略
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // 禁用旧版 IE 的 ActiveX 沙箱
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // 防止 DNS 预读取泄漏
        context.Response.Headers["X-DNS-Prefetch-Control"] = "off";

        // Content-Security-Policy
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://code.jquery.com; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://code.jquery.com; " +
            "font-src 'self' https://cdn.jsdelivr.net; " +
            "img-src 'self' data:; " +
            "connect-src 'self';";

        await _next(context);
    }
}
