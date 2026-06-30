using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Services;

namespace StudentManagerCore.Controllers;

/// <summary>
/// 统一认证系统内部 API — 供 Auth Server (Go) 调用
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthApiController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>验证用户名密码（Auth Server 登录时调用）</summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req)
    {
        // 校验内部 API Key
        var key = _config["Auth:InternalKey"];
        if (!string.IsNullOrEmpty(key) && req.ApiKey != key)
            return Ok(new { success = false, error = "未授权" });

        var user = await _db.Admins.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null)
            return Ok(new { success = false, error = "用户名或密码错误" });

        if (!PasswordHelper.Verify(req.Password, user.Password))
            return Ok(new { success = false, error = "用户名或密码错误" });

        return Ok(new
        {
            success = true,
            user = new
            {
                id = user.AdminID,
                username = user.Username,
                real_name = user.RealName ?? user.Username,
                role = user.PrimaryRole,
                roles = user.Role ?? ""
            }
        });
    }

    /// <summary>修改密码（Auth Server 修改密码时调用）</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePwdRequest req)
    {
        // 校验内部 API Key
        var key = _config["Auth:InternalKey"];
        if (!string.IsNullOrEmpty(key) && req.ApiKey != key)
            return Ok(new { success = false, error = "未授权" });

        var user = await _db.Admins.FirstOrDefaultAsync(u => u.AdminID == req.UserId);
        if (user == null)
            return Ok(new { success = false, error = "用户不存在" });

        if (!PasswordHelper.Verify(req.OldPassword, user.Password))
            return Ok(new { success = false, error = "旧密码错误" });

        var error = PasswordHelper.Validate(req.NewPassword);
        if (error != null)
            return Ok(new { success = false, error = error });

        user.Password = PasswordHelper.Hash(req.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "密码修改成功" });
    }
}

// ========== 请求模型 ==========
public class VerifyRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ApiKey { get; set; }
}

public class ChangePwdRequest
{
    public int UserId { get; set; }
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string? ApiKey { get; set; }
}
