using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentManagerCore.Data;
using StudentManagerCore.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace StudentManagerCore.Controllers;

[AllowAnonymous]
public class AuthCallbackController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly AuthService _authService;

    public AuthCallbackController(AppDbContext db, IConfiguration config, AuthService authService)
    {
        _db = db;
        _config = config;
        _authService = authService;
    }

    [HttpGet("/auth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token))
            return Redirect("http://10.166.193.202/login?error=no_token");

        try
        {
            var secret = _config["JWT_SECRET"] ?? _config["Jwt:SecretKey"] ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("JWT 密钥未配置");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var handler = new JwtSecurityTokenHandler();

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out _);

            if (principal.FindFirst("type")?.Value != "transfer")
                return Redirect("http://10.166.193.202/login?error=bad_token");

            var userIdStr = principal.FindFirst("user_id")?.Value;
            var username = principal.FindFirst("username")?.Value;

            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Redirect("http://10.166.193.202/login?error=bad_token");

            var admin = await _db.Admins.FindAsync(userId);
            if (admin == null)
            {
                admin = await _db.Admins.FirstOrDefaultAsync(a => a.Username == username);
                if (admin == null)
                    return Redirect("http://10.166.193.202/login?error=user_not_found");
            }

            var tokenString = _authService.GenerateJwtToken(admin, false);

            Response.Cookies.Append("access_token", tokenString, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddMinutes(_authService.GetJwtExpireMinutes())
            });

            if (admin.PrimaryRole != "管理员" && !PasswordHelper.IsStrong(admin.Password))
                return RedirectToAction("ResetPassword", "Account");

            return RedirectToAction("Index", "Home");
        }
        catch (SecurityTokenExpiredException)
        {
            return Redirect("http://10.166.193.202/login?error=expired");
        }
        catch
        {
            return Redirect("http://10.166.193.202/login?error=invalid_token");
        }
    }
}
