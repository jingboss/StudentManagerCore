using Microsoft.AspNetCore.Identity;
using System.Text.RegularExpressions;

namespace StudentManagerCore.Services;

/// <summary>
/// 密码哈希与验证工具（使用 ASP.NET Core Identity 的 PBKDF2 算法）
/// </summary>
public static class PasswordHelper
{
    private static readonly PasswordHasher<object> _hasher = new PasswordHasher<object>();

    /// <summary>对明文密码进行哈希</summary>
    public static string Hash(string plainPassword)
    {
        return _hasher.HashPassword(null!, plainPassword);
    }

    /// <summary>验证明文密码是否匹配哈希值。</summary>
    public static bool Verify(string plainPassword, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        var result = _hasher.VerifyHashedPassword(null!, storedHash, plainPassword);
        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <summary>判断密码是否已经哈希过</summary>
    public static bool IsHashed(string? password)
    {
        return password != null && password.StartsWith("AQAAAA");
    }

    /// <summary>
    /// 密码强度规则：至少8位，包含字母和数字
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="confirmPassword">确认密码（可选）</param>
    /// <returns>验证通过返回 null，失败返回错误信息</returns>
    public static string? Validate(string password, string? confirmPassword = null)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return "密码至少8位";
        if (confirmPassword != null && password != confirmPassword)
            return "两次输入的密码不一致";
        if (!Regex.IsMatch(password, "[a-zA-Z]"))
            return "密码必须包含字母";
        if (!Regex.IsMatch(password, "[0-9]"))
            return "密码必须包含数字";
        return null;
    }

    /// <summary>检查密码是否符合强度要求（≥8位、含字母、含数字）</summary>
    public static bool IsStrong(string password)
    {
        return password.Length >= 8
            && Regex.IsMatch(password, "[a-zA-Z]")
            && Regex.IsMatch(password, "[0-9]");
    }
}
