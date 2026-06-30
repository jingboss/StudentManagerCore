using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagerCore.Services;

namespace StudentManagerCore.Controllers;

[AllowAnonymous]
public class InstallController : Controller
{
    private readonly InstallService _installService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IHostApplicationLifetime _appLifetime;

    public InstallController(
        InstallService installService,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IHostApplicationLifetime appLifetime)
    {
        _installService = installService;
        _configuration = configuration;
        _env = env;
        _appLifetime = appLifetime;
    }

    [HttpGet]
    public IActionResult Index()
    {
        // 如果已安装且是重复访问，跳转到登录页
        if (InstallService.IsInstalled(_env.ContentRootPath))
        {
            return Redirect("/Account/Login");
        }

        var currentConnStr = _configuration.GetConnectionString("DefaultConnection") ?? "";
        ViewBag.DefaultConnStr = currentConnStr;
        return View();
    }

    /// <summary>步骤1：测试数据库连接</summary>
    [HttpPost]
    public async Task<JsonResult> TestConnection([FromForm] string connectionString)
    {
        var result = await _installService.TestConnectionAsync(connectionString);
        return Json(new { success = result.Success, message = result.Message });
    }

    /// <summary>步骤2：执行建表 SQL</summary>
    [HttpPost]
    public async Task<JsonResult> CreateTables([FromForm] string connectionString)
    {
        var result = await _installService.CreateTablesAsync(connectionString);
        return Json(new { success = result.Success, message = result.Message });
    }

    /// <summary>步骤3：创建管理员</summary>
    [HttpPost]
    public async Task<JsonResult> CreateAdmin(
        [FromForm] string connectionString,
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string realName)
    {
        // 客户端已校验，服务端二次校验
        if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
            return Json(new { success = false, message = "用户名至少2位" });

        var pwdError = PasswordHelper.Validate(password);
        if (pwdError != null)
            return Json(new { success = false, message = pwdError });

        var result = await _installService.CreateAdminAsync(
            connectionString, username, password, realName);
        return Json(new { success = result.Success, message = result.Message });
    }

    /// <summary>步骤4：保存网站配置并完成安装</summary>
    [HttpPost]
    public async Task<JsonResult> CompleteInstall(
        [FromForm] string connectionString,
        [FromForm] string siteName,
        [FromForm] string copyright,
        [FromForm] string jwtSecret)
    {
        try
        {
            // 校验JWT密钥
            if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
                return Json(new { success = false, message = "JWT密钥至少32位，请重新生成" });

            // 1. 保存站点配置到 SiteConfig 表
            var configs = new Dictionary<string, string>
            {
                ["SiteName"] = siteName,
                ["Copyright"] = copyright
            };
            var configResult = await _installService.SaveSiteConfigAsync(connectionString, configs);
            if (!configResult.Success)
                return Json(new { success = false, message = configResult.Message });

            // 2. 将连接字符串写入 appsettings.json
            _installService.SaveConnectionString(connectionString);

            // 3. 将JWT密钥写入 appsettings.json
            _installService.SaveJwtSecret(jwtSecret);

            // 4. 写入锁定文件
            _installService.WriteLockFile();

            // 5. 异步重启应用（等待响应发送后自动重启，使正常模式生效）
            var lifetime = _appLifetime;
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                lifetime.StopApplication();
            });

            return Json(new { success = true, message = "安装完成！系统即将自动重启，请稍后登录..." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"安装完成时出错：{ex.Message}" });
        }
    }
}
