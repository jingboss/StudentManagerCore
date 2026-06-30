using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace StudentManagerCore.Services;

/// <summary>
/// 认证服务 — 处理登录验证、JWT生成、失败锁定
/// </summary>
public class AuthService
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;
    private readonly string _jwtSecretKey;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _jwtExpireMinutes;

    // ========== 登录失败锁定 ==========
    private static readonly ConcurrentDictionary<string, LoginAttemptInfo> _loginAttempts = new();
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private class LoginAttemptInfo
    {
        public int FailedCount { get; set; }
        public DateTime? LockoutEnd { get; set; }
    }

    public AuthService(AppDbContext db, IConfiguration configuration, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
        _jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? configuration["Jwt:SecretKey"] ?? "";
        _jwtIssuer = configuration["Jwt:Issuer"] ?? "StudentManager";
        _jwtAudience = configuration["Jwt:Audience"] ?? "StudentManagerUsers";
        _jwtExpireMinutes = int.Parse(configuration["Jwt:ExpireMinutes"] ?? "120");
    }

    /// <summary>获取站点配置值</summary>
    public async Task<string?> GetConfigValue(string key)
    {
        var config = await _db.SiteConfigs.FindAsync(key);
        return config?.ConfigValue;
    }

    /// <summary>检测服务器时间与互联网时间偏差（超过5分钟返回错误信息）</summary>
    public static async Task<string?> CheckTimeSyncAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(
                "https://api.m.taobao.com/rest/api3.do?api=mtop.common.getTimestamp");
            var json = JsonDocument.Parse(response);
            var tStr = json.RootElement.GetProperty("data").GetProperty("t").GetString();
            if (tStr == null || !long.TryParse(tStr, out var timestampMs))
                return null;

            var internetTime = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
            var localTime = DateTime.UtcNow;
            var diff = Math.Abs((localTime - internetTime).TotalMinutes);
            if (diff > 5)
                return $"系统时间与互联网时间偏差过大（{diff:F1}分钟），请联系管理员同步服务器时间后重试。";
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>检查登录锁定状态</summary>
    public (bool IsLocked, int? RemainingMinutes) CheckLockout(string username)
    {
        var key = (username ?? "").ToLowerInvariant();
        if (_loginAttempts.TryGetValue(key, out var info) && info.LockoutEnd.HasValue)
        {
            if (DateTime.UtcNow < info.LockoutEnd.Value)
                return (true, (int)Math.Ceiling((info.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes));
            _loginAttempts.TryRemove(key, out _);
        }
        return (false, null);
    }

    /// <summary>记录登录失败</summary>
    public (bool LockedOut, int RemainingAttempts) RecordFailedAttempt(string username)
    {
        var key = (username ?? "").ToLowerInvariant();
        var info = _loginAttempts.GetOrAdd(key, _ => new LoginAttemptInfo());
        info.FailedCount++;

        if (info.FailedCount >= MaxFailedAttempts)
        {
            info.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            return (true, 0);
        }
        return (false, MaxFailedAttempts - info.FailedCount);
    }

    /// <summary>清除登录失败记录</summary>
    public void ClearFailedAttempts(string username)
    {
        var key = (username ?? "").ToLowerInvariant();
        _loginAttempts.TryRemove(key, out _);
    }

    /// <summary>验证用户凭证</summary>
    public async Task<Admin?> ValidateCredentialsAsync(string username, string password)
    {
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Username == username);
        if (admin == null || !PasswordHelper.Verify(password, admin.Password))
            return null;
        return admin;
    }

    /// <summary>生成 JWT Token</summary>
    public string GenerateJwtToken(Admin admin, bool rememberMe)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, admin.Username),
             new Claim(ClaimTypes.Role, admin.PrimaryRole),
            new Claim(ClaimTypes.MobilePhone, admin.Phone ?? ""),
            new Claim("RealName", admin.RealName ?? ""),
            new Claim("AdminID", admin.AdminID.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = rememberMe ? DateTime.UtcNow.AddDays(7) : DateTime.UtcNow.AddMinutes(_jwtExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>获取 JWT 过期分钟数</summary>
    public int GetJwtExpireMinutes() => _jwtExpireMinutes;

    /// <summary>一站式登录</summary>
    public async Task<LoginResult> LoginAsync(LoginViewModel model)
    {
        var (isLocked, _) = CheckLockout(model.Username);
        if (isLocked)
        {
            var (locked, remaining) = CheckLockout(model.Username);
            return LoginResult.Locked(remaining ?? 0);
        }

        var admin = await ValidateCredentialsAsync(model.Username, model.Password);
        if (admin == null)
        {
            var (lockedOut, remainingAttempts) = RecordFailedAttempt(model.Username);
            return LoginResult.InvalidCredentials(remainingAttempts, lockedOut);
        }

        ClearFailedAttempts(model.Username);
        return LoginResult.Success(admin);
    }
}

/// <summary>登录结果</summary>
public class LoginResult
{
    public bool Succeeded { get; private set; }
    public Admin? Admin { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool IsLocked { get; private set; }

    public static LoginResult Success(Admin admin) => new()
    {
        Succeeded = true,
        Admin = admin
    };

    public static LoginResult InvalidCredentials(int remainingAttempts, bool lockedOut) => new()
    {
        Succeeded = false,
        ErrorMessage = lockedOut
            ? "密码错误次数过多，账号已被锁定 15 分钟"
            : $"用户名或密码错误（还剩 {remainingAttempts} 次尝试机会）",
        IsLocked = lockedOut
    };

    public static LoginResult Locked(int remainingMinutes) => new()
    {
        Succeeded = false,
        ErrorMessage = $"该账号已被临时锁定，请 {remainingMinutes} 分钟后再试",
        IsLocked = true
    };
}
