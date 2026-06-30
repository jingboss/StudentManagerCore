using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly AuditService _auditService;

    public AccountController(AppDbContext db, AuthService authService, AuditService auditService)
    {
        _db = db;
        _authService = authService;
        _auditService = auditService;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        await PopulateViewBagAsync();
        ViewBag.TimeSyncError = await AuthService.CheckTimeSyncAsync();
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        await PopulateViewBagAsync();

        if (!ModelState.IsValid)
            return View(model);

        // 验证验证码
        if (!CaptchaController.Validate(model.CaptchaCode, HttpContext))
        {
            ModelState.AddModelError("", "验证码错误");
            return View(model);
        }

        // 时间同步检测
        var timeError = await AuthService.CheckTimeSyncAsync();
        if (timeError != null)
        {
            ModelState.AddModelError("", timeError);
            return View(model);
        }

        // 执行登录
        var result = await _authService.LoginAsync(model);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorMessage ?? "登录失败");
            return View(model);
        }

        var admin = result.Admin!;

        // 网站关闭 → 仅管理员可登录
        var siteClosed = await _authService.GetConfigValue("SiteClosed");
        if (siteClosed == "true" && !admin.HasRole("管理员"))
        {
            ModelState.AddModelError("", "网站已关闭，仅管理员可以登录，其他人员禁止登录。");
            return View(model);
        }

        // 生成 JWT
        var tokenString = _authService.GenerateJwtToken(admin, model.RememberMe);

        var cookieExpires = model.RememberMe
            ? DateTimeOffset.UtcNow.AddDays(1)
            : DateTimeOffset.UtcNow.AddMinutes(_authService.GetJwtExpireMinutes());

        Response.Cookies.Append("access_token", tokenString, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = "/",
            Expires = cookieExpires
        });

        // 非管理员且 MustChangePassword = true → 强制修改密码
        var role = admin.PrimaryRole;
        if (role != "管理员")
        {
            bool mustChange = true;
            try
            {
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MustChangePassword FROM Admin WHERE AdminID=@id";
                var p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = admin.AdminID;
                cmd.Parameters.Add(p);
                var obj = await cmd.ExecuteScalarAsync();
                if (obj != null && obj != DBNull.Value)
                    mustChange = Convert.ToInt32(obj) == 1;
                await conn.CloseAsync();
            }
            catch { /* 列不存在时默认要求改密 */ }

            if (mustChange)
                return RedirectToAction("ResetPassword");
        }

        await _auditService.LogAsync("登录", $"{admin.RealName} 登录系统");
        return RedirectToAction("Index", "Home");
    }

    /// <summary>填充登录页 ViewBag</summary>
    private async Task PopulateViewBagAsync()
    {
        ViewBag.SiteName = await _authService.GetConfigValue("SiteName") ?? "华强学校信息管理中心";
        ViewBag.Copyright = await _authService.GetConfigValue("Copyright") ?? "© 2025 华强学校信息管理中心";
        ViewBag.BackgroundImage = await _authService.GetConfigValue("BackgroundImage") ?? "/uploads/imge/login_bg.jpg";
        ViewBag.Logo = await _authService.GetConfigValue("Logo") ?? "";
        var siteClosed = await _authService.GetConfigValue("SiteClosed");
        ViewBag.SiteClosed = siteClosed == "true";
    }

    /// <summary>强制修改密码页</summary>
    [HttpGet]
    public IActionResult ResetPassword() => View();

    /// <summary>提交修改密码</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string oldPassword, string newPassword, string confirmPassword)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return RedirectToAction("Login");

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Username == username);
        if (admin == null)
            return RedirectToAction("Login");

        if (!PasswordHelper.Verify(oldPassword, admin.Password))
        {
            ModelState.AddModelError("", "原密码错误");
            return View();
        }

        var error = PasswordHelper.Validate(newPassword, confirmPassword);
        if (error != null)
        {
            ModelState.AddModelError("", error);
            return View();
        }

        admin.Password = PasswordHelper.Hash(newPassword);
        await _db.SaveChangesAsync();

        // 清除 MustChangePassword 标记
        try { await _db.Database.ExecuteSqlRawAsync("UPDATE Admin SET MustChangePassword=0 WHERE AdminID={0}", admin.AdminID); } catch { }

        TempData["Success"] = "密码修改成功，请使用新密码登录";
        return RedirectToAction("Login");
    }

    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        return RedirectToAction("Login");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Json(new { success = false, message = "未登录" });

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Username == username);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        if (!PasswordHelper.Verify(oldPassword, admin.Password))
            return Json(new { success = false, message = "原密码错误" });

        var error = PasswordHelper.Validate(newPassword);
        if (error != null)
            return Json(new { success = false, message = error });

        admin.Password = PasswordHelper.Hash(newPassword);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "密码修改成功" });
    }
}
