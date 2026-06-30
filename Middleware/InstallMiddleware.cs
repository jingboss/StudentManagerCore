namespace StudentManagerCore;

/// <summary>
/// 安装状态检查中间件 — 正常模式下兜底检查 app_installed.lock
/// </summary>
public class InstallMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _contentRootPath;

    public InstallMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _contentRootPath = env.ContentRootPath;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // 安装页面和静态资源不拦截
        if (path == "/install" || path.StartsWith("/install/") ||
            path.StartsWith("/css") || path.StartsWith("/js") ||
            path.StartsWith("/lib") || path.StartsWith("/imge") ||
            path.StartsWith("/uploads"))
        {
            await _next(context);
            return;
        }

        // 检查锁定文件
        var lockPath = Path.Combine(_contentRootPath, "app_installed.lock");
        if (File.Exists(lockPath))
        {
            await _next(context);
            return;
        }

        // 无锁定文件时，检查环境变量是否已配置有效密码（兼容已有生产部署）
        var envConnStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var hasValidEnvConnStr = !string.IsNullOrEmpty(envConnStr) &&
                                 envConnStr.Contains("Password=") &&
                                 !envConnStr.Contains("Password=;");
        if (hasValidEnvConnStr)
        {
            await _next(context);
            return;
        }

        // 未安装，重定向到安装向导
        context.Response.Redirect("/Install");
    }
}
