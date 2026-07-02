using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using StudentManagerCore.Data;
using StudentManagerCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// ===== 安装模式检测：未安装且无有效环境变量时进入安装向导 =====
var contentRoot = builder.Environment.ContentRootPath;
var isInstalled = File.Exists(Path.Combine(contentRoot, "app_installed.lock"));

if (!isInstalled)
{
    var envConnStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    var hasValidEnvConnStr = !string.IsNullOrEmpty(envConnStr) &&
                             envConnStr.Contains("Password=") &&
                             !envConnStr.Contains("Password=;");

    if (!hasValidEnvConnStr)
    {
        // ===== 安装模式：最小化注册，仅用于安装向导 =====
        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddScoped<StudentManagerCore.Services.InstallService>();

        var installApp = builder.Build();

        installApp.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync($"<h3>系统安装模式出错</h3><p>{ex.Message}</p>");
            }
        });

        installApp.UseStaticFiles();
        installApp.UseSession();
        installApp.UseRouting();

        installApp.MapControllerRoute(
            name: "default",
            pattern: "{controller=Install}/{action=Index}/{id?}");

        installApp.Run();
        return;
    }
}

// Add services to the container
builder.Services.AddControllersWithViews();

// Add application services
builder.Services.AddHttpContextAccessor();

// 审计日志服务
builder.Services.AddScoped<StudentManagerCore.Services.AuditService>();

// 认证服务
builder.Services.AddScoped<StudentManagerCore.Services.AuthService>();

// 安装服务
builder.Services.AddScoped<StudentManagerCore.Services.InstallService>();

// 注册AI分析服务（云端API）- 超时由AiAnalysisService内部CancellationToken控制
builder.Services.AddHttpClient<StudentManagerCore.Services.AiAnalysisService>(client =>
{
    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

// Configure anti-forgery to accept header token (for JSON POST requests)
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Add Entity Framework
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString) || connectionString.Contains("Password=;") || !connectionString.Contains("Password="))
{
    // 环境变量读取失败，密码为空或不存在 → 必须通过环境变量注入
    throw new InvalidOperationException(
        "数据库密码未配置！请在服务器上设置环境变量。\n" +
        "方法1（推荐）：运行以下命令设置系统环境变量（管理员权限）：\n" +
        "    setx ConnectionStrings__DefaultConnection \"Server=localhost;Database=StudentManagerDB;User Id=root;Password=你的密码;\" /M\n" +
        "    然后重启 IIS (iisreset)\n\n" +
        "方法2（临时）：在 appsettings.json 的 ConnectionStrings:DefaultConnection 中填入密码。");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString,
        new MySqlServerVersion(new Version(8, 0))));

// Add JWT Authentication
var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
if (string.IsNullOrEmpty(jwtSecretKey))
{
    // 回退到 appsettings.json 读取
    jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
}
if (string.IsNullOrEmpty(jwtSecretKey))
{
    throw new InvalidOperationException(
        "JWT密钥未配置！请在 appsettings.json 的 Jwt:SecretKey 中设置，或设置环境变量 JWT_SECRET_KEY。");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "StudentManager";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "StudentManagerUsers";
var jwtExpireMinutes = int.Parse(builder.Configuration["Jwt:ExpireMinutes"] ?? "120");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
        };

        // 同时支持从 Cookie 读取 JWT（兼容 MVC 表单 POST）
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                    context.Token = context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // AJAX 请求返回 401，页面请求重定向到登录页
                if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                    context.Request.Headers["Accept"].ToString().Contains("application/json"))
                {
                    context.Response.StatusCode = 401;
                }
                else
                {
                    context.Response.Redirect("/Account/Login");
                    context.HandleResponse();
                }
                return Task.CompletedTask;
            }
        };
    });

// Add session (for captcha)
builder.Services.AddDistributedMemoryCache();

// 全局文件上传大小限制
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB 安全上限
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(15);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// 安装状态检查中间件（兜底检测 app_installed.lock）
app.UseMiddleware<InstallMiddleware>();

// Configure the HTTP request pipeline
// 安全响应头（X-Frame-Options, CSP 等）
app.UseMiddleware<SecurityHeadersMiddleware>();

// 请求频率限制（AI接口放宽至30次/分钟，不影响正常使用）
app.UseMiddleware<RateLimitingMiddleware>();

// IP白名单中间件（从 appsettings.json 读取允许的IP）
app.UseMiddleware<IpRestrictionMiddleware>();
// 全局异常处理 — 正式环境输出友好错误，不泄漏堆栈
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($@"<!DOCTYPE html>
<html lang='zh-CN'>
<head><meta charset='UTF-8'><title>系统错误</title>
<style>
    body {{ font-family: 'Microsoft YaHei', sans-serif; background: #f8f9fa; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }}
    .card {{ text-align: center; background: #fff; padding: 50px; border-radius: 16px; box-shadow: 0 4px 20px rgba(0,0,0,0.1); max-width: 480px; }}
    .icon {{ font-size: 64px; color: #dc3545; }}
    h2 {{ color: #333; margin: 16px 0 8px; }}
    p {{ color: #666; font-size: 14px; }}
</style>
</head>
<body>
<div class='card'>
    <div class='icon'>&#9888;</div>
    <h2>系统出了点小问题</h2>
    <p>服务器内部错误，请稍后重试或联系管理员。</p>
</div>
</body>
</html>");
        // 将异常写入日志文件以便管理员排查
        try { System.IO.File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + "error.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n"); } catch { }
    }
});

app.UseHsts();

// 自定义错误页面 — 404 / 403 等状态码
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

// 外部上传目录（Linux 下在应用目录内新建，避免权限问题）
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads", "imge");
if (!Directory.Exists(uploadsPath))
    Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads/imge"
});

// 问卷上传目录
var surveyUploadPath = Path.Combine(app.Environment.ContentRootPath, "uploads", "survey");
if (!Directory.Exists(surveyUploadPath))
    Directory.CreateDirectory(surveyUploadPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(surveyUploadPath),
    RequestPath = "/uploads/survey"
});

app.UseSession();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Ensure imge directory exists
var imgePath = Path.Combine(app.Environment.WebRootPath, "imge");
if (!Directory.Exists(imgePath))
    Directory.CreateDirectory(imgePath);

// Auto-migration: 使用 EF Core 正式迁移
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}
catch (Exception ex)
{
    System.IO.File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "migrate_error.txt",
        $"Time: {DateTime.Now}\nError: {ex.Message}\nStack: {ex.StackTrace}");
}

// 确保 RepairRequest 表存在（迁移失败时兜底）
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS RepairRequest (
            Id int NOT NULL AUTO_INCREMENT,
            Title varchar(200) NOT NULL,
            Description longtext NULL,
            Location varchar(200) NULL,
            ContactPhone varchar(20) NULL,
            Status varchar(20) NOT NULL DEFAULT '待处理',
            CreateTime datetime(6) NOT NULL,
            PreferredTime datetime(6) NULL,
            CreatedBy int NOT NULL,
            CreatorName varchar(50) NULL,
            ProcessTime datetime(6) NULL,
            ProcessedBy int NULL,
            ProcessorName varchar(50) NULL,
            Remark longtext NULL,
            PRIMARY KEY (Id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // 兼容旧表：新增列（如已存在会静默忽略）
        try { db.Database.ExecuteSqlRaw("ALTER TABLE RepairRequest ADD COLUMN PreferredTime datetime(6) NULL"); } catch { }

        // 学生表添加转出时间列
        try { db.Database.ExecuteSqlRaw("ALTER TABLE Student ADD COLUMN TransferOutTime datetime(6) NULL"); } catch { }
    }
}
catch { }

// 确保问卷表存在（迁移失败时兜底）
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS Survey (
            Id int NOT NULL AUTO_INCREMENT,
            Title varchar(200) NOT NULL,
            Description longtext NULL,
            Status varchar(20) NOT NULL DEFAULT '草稿',
            CreatedBy int NOT NULL,
            CreatorName varchar(50) DEFAULT NULL,
            CreateTime datetime(6) NOT NULL,
            UpdateTime datetime(6) DEFAULT NULL,
            PRIMARY KEY (Id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SurveyQuestion (
            Id int NOT NULL AUTO_INCREMENT,
            SurveyId int NOT NULL,
            SortOrder int NOT NULL DEFAULT 0,
            Type varchar(20) NOT NULL,
            IsRequired tinyint(1) NOT NULL DEFAULT 0,
            Title varchar(500) NOT NULL,
            PRIMARY KEY (Id),
            KEY IX_SurveyQuestion_SurveyId (SurveyId),
            CONSTRAINT FK_SurveyQuestion_Survey FOREIGN KEY (SurveyId) REFERENCES Survey(Id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SurveyQuestionOption (
            Id int NOT NULL AUTO_INCREMENT,
            QuestionId int NOT NULL,
            SortOrder int NOT NULL DEFAULT 0,
            OptionText varchar(200) NOT NULL,
            PRIMARY KEY (Id),
            KEY IX_SurveyQuestionOption_QuestionId (QuestionId),
            CONSTRAINT FK_SurveyQuestionOption_SurveyQuestion FOREIGN KEY (QuestionId) REFERENCES SurveyQuestion(Id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SurveySubmission (
            Id int NOT NULL AUTO_INCREMENT,
            SurveyId int NOT NULL,
            SubmittedBy varchar(100) DEFAULT NULL,
            SubmitterName varchar(50) DEFAULT NULL,
            SubmitTime datetime(6) NOT NULL,
            PRIMARY KEY (Id),
            KEY IX_SurveySubmission_SurveyId (SurveyId),
            CONSTRAINT FK_SurveySubmission_Survey FOREIGN KEY (SurveyId) REFERENCES Survey(Id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SurveyAnswer (
            Id int NOT NULL AUTO_INCREMENT,
            SubmissionId int NOT NULL,
            QuestionId int NOT NULL,
            AnswerText longtext NULL,
            FilePath varchar(500) DEFAULT NULL,
            PRIMARY KEY (Id),
            KEY IX_SurveyAnswer_SubmissionId (SubmissionId),
            CONSTRAINT FK_SurveyAnswer_SurveySubmission FOREIGN KEY (SubmissionId) REFERENCES SurveySubmission(Id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }
}
catch { }

app.Run();
